#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace Marching;

public class MarchMissionBehavior : MissionBehavior
{
    public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;
    private InputKey _marchKey;
    private OrderController _orderController;
    
    // İki ayrı liste: Manuel (oyuncu) ve Otomatik (AI)
    public static List<Formation> PlayerMarchingFormations = new List<Formation>();
    public static List<Formation> AIMarchingFormations = new List<Formation>();
    
    // Savaşa girmiş formation'lar - bir kez yaklaşınca bir daha marşa GİREMEZ
    private static HashSet<Formation> _formationsInCombat = new HashSet<Formation>();
    
    // AI kontrol timer'ı - her 1 saniyede bir AI marşını günceller
    private float _aiMarchUpdateTimer;
    private const float AI_MARCH_UPDATE_INTERVAL = 1.0f;

    /// <summary>
    /// Boş veya null formation'ları tüm listelerden temizler
    /// Memory leak ve gereksiz iterasyon önleme için
    /// </summary>
    private void CleanupEmptyFormations()
    {
        // List için RemoveAll kullan
        PlayerMarchingFormations.RemoveAll(f => f == null || f.CountOfUnits == 0);
        AIMarchingFormations.RemoveAll(f => f == null || f.CountOfUnits == 0);
        
        // HashSet için RemoveWhere kullan (RemoveAll yok!)
        _formationsInCombat.RemoveWhere(f => f == null || f.CountOfUnits == 0);
    }

    /// <summary>
    /// Mission'ın gerçek bir savaş olup olmadığını kontrol eder
    /// Arena, turnuva, şehir/köy gezintileri, DENİZ SAVAŞLARI false döner
    /// </summary>
    private bool IsCombatMission()
    {
        if (Mission.Current == null)
            return false;
            
        var missionMode = Mission.Current.Mode;
        
        // Arena ve turnuva modlarını filtrele
        if (missionMode == MissionMode.Duel || missionMode == MissionMode.Tournament)
        {
            return false;
        }
        
        // Şehir/köy gezintisi modunu filtrele
        if (missionMode == MissionMode.Deployment)
        {
            return false;
        }
        
        // ⚡ WAR SAILS DLC UYUMLULUĞU: Deniz savaşlarını filtrele
        // Eğer mission terrain'i su ise veya gemi varsa marş sistemi çalışmasın
        if (Mission.Current.Scene != null)
        {
            var sceneName = Mission.Current.SceneName?.ToLower() ?? "";
            
            // Deniz/gemi sahne isimleri içeriyorsa
            if (sceneName.Contains("naval") || 
                sceneName.Contains("sea") || 
                sceneName.Contains("ship") ||
                sceneName.Contains("ocean"))
            {
                return false; // Deniz savaşı - marş sistemi kapalı
            }
        }
        
        // Sadece gerçek kara savaşları ve kuşatmalar
        return missionMode == MissionMode.Battle || missionMode == MissionMode.Stealth;
    }
    
    /// <summary>
    /// Kuşatma mission'ında bu team savunan taraf mı kontrol eder
    /// </summary>
    private bool IsDefenderInSiege(Team team)
    {
        if (Mission.Current == null || team == null)
            return false;
            
        // Kuşatma mission'ı mı kontrol et
        if (!Mission.Current.IsSiegeBattle)
            return false;
        
        // Savunan taraf genelde BattleSideEnum.Defender
        return team.Side == BattleSideEnum.Defender;
    }
    
    private void AddMarchingFormation(Formation formation)
    {
        if (!PlayerMarchingFormations.Contains(formation))
        {
            PlayerMarchingFormations.Add(formation);
        }
    }
    
    private void RemoveMarchingFormation(Formation formation)
    {
        PlayerMarchingFormations.Remove(formation);
    }
    
    /// <summary>
    /// Ana karakterin portresiyle birlikte mesaj gösterir
    /// </summary>
    private void ShowMarchMessage(TextObject message)
    {
        // Portreli mesaj için doğru overload
        if (Agent.Main != null && Agent.Main.Character != null)
        {
            MBInformationManager.AddQuickInformation(
                message,
                0,
                Agent.Main.Character
            );
        }
        else
        {
            // Fallback: Portresiz
            MBInformationManager.AddQuickInformation(message);
        }
    }
    
    public MarchMissionBehavior()
    {
        _marchKey = (InputKey)Enum.Parse(typeof(InputKey), MarchGlobalConfig.Instance.MarchingHotKey.SelectedValue);
        _aiMarchUpdateTimer = 0f;
    }

    public override void OnMissionTick(float dt)
    {
        base.OnMissionTick(dt);
        
        // Sadece savaş alanında çalış - arena, turnuva, şehir/köy gezintisinde kapalı
        if (!IsCombatMission())
            return;
        
        // AI otomatik marş sistemi - her 1 saniyede bir güncelle
        if (MarchGlobalConfig.Instance.EnableAIMarch)
        {
            _aiMarchUpdateTimer += dt;
            if (_aiMarchUpdateTimer >= AI_MARCH_UPDATE_INTERVAL)
            {
                _aiMarchUpdateTimer = 0f;
                UpdateAIMarch();
                
                // AI marş güncellendiğinde boş formation'ları temizle
                // 1 saniyede bir cleanup - minimal overhead
                CleanupEmptyFormations();
                
                // Cache'i de temizle - config değişikliği 1 saniye içinde yansır
                // Her AI update'te cache yenilenir (lazy invalidation)
                MarchingAgentStatCalculateModel.ClearSpeedCache();
            }
        }
        
        // Manuel marş kontrolü (oyuncu M tuşu)
        if (_marchKey.IsReleased())
        {
            if (Mission.Current == null || Agent.Main == null || Agent.Main.Health <= 0 || !Agent.Main.Team.IsPlayerGeneral) 
                return;
            
            _orderController = Agent.Main.Team.PlayerOrderController;
            if (_orderController == null || !Mission.Current.IsOrderMenuOpen || !_orderController.SelectedFormations.Any())
            {
                ShowMarchMessage(new TextObject("{=no_formations_selected}No formations selected to march!"));
                return;
            }

            if (_orderController.SelectedFormations.Count == 1)
            {
                if (PlayerMarchingFormations.Contains(_orderController.SelectedFormations[0]))
                {
                    TextObject textObject = new TextObject("{=dismiss_single_formation}{FORMATION}, dismiss march!");
                    textObject.SetTextVariable("FORMATION", _orderController.SelectedFormations[0].GetFormationName());
                    ShowMarchMessage(textObject);
                    RemoveMarchingFormation(_orderController.SelectedFormations[0]);
                }
                else
                {
                    TextObject textObject = new TextObject("{=march_single_formation}{FORMATION}, march!");
                    textObject.SetTextVariable("FORMATION", _orderController.SelectedFormations[0].GetFormationName());
                    ShowMarchMessage(textObject);
                    AddMarchingFormation(_orderController.SelectedFormations[0]);
                }
            }
            else if (_orderController.SelectedFormations.Count != Mission.PlayerTeam.FormationsIncludingEmpty.Count(f => _orderController.IsFormationSelectable(f)))
            {
                if (!_orderController.SelectedFormations.All(f => PlayerMarchingFormations.Contains(f)))
                {
                    TextObject textObject = new TextObject("{=march_multiple_formations}{FORMATIONS}, march!");
                    textObject.SetTextVariable("FORMATIONS", string.Join(", ", _orderController.SelectedFormations.Select(f => f.GetFormationName())));
                
                    ShowMarchMessage(textObject);
                    foreach (var formation in _orderController.SelectedFormations)
                    {
                        AddMarchingFormation(formation);
                    }
                }
                else
                {
                    TextObject textObject = new TextObject("{=dismiss_multiple_formations}{FORMATIONS}, dismiss march!");
                    textObject.SetTextVariable("FORMATIONS", string.Join(", ", _orderController.SelectedFormations.Select(f => f.GetFormationName())));
                
                    ShowMarchMessage(textObject);
                    foreach (var formation in _orderController.SelectedFormations)
                    {
                        RemoveMarchingFormation(formation);
                    }
                }
            }
            else if(!PlayerMarchingFormations.Any())
            {
                ShowMarchMessage(new TextObject("{=everyone_march}Everyone! March!"));
                PlayerMarchingFormations.Clear();
                foreach (var formation in _orderController.SelectedFormations)
                {
                    AddMarchingFormation(formation);
                }
            }
            else
            {
                ShowMarchMessage(new TextObject("{=everyone_dismiss}Everyone! Dismiss march!"));
                PlayerMarchingFormations.Clear();
            }
            
            OnMarch();
        }
    }

    public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent, in MissionWeapon affectorWeapon, in Blow blow,
        in AttackCollisionData attackCollisionData)
    {
        base.OnAgentHit(affectedAgent, affectorAgent, in affectorWeapon, in blow, in attackCollisionData);
        if (affectedAgent.IsMainAgent && affectedAgent.Health <= 0)
        {
            PlayerMarchingFormations.Clear();
            AIMarchingFormations.Clear();
            OnMarch();
        }
    }
    
    /// <summary>
    /// AI formation'larını otomatik olarak marş moduna alır/çıkarır
    /// Düşmana mesafeye göre karar verir
    /// 
    /// SADECE AI kontrollü team'ler için çalışır:
    /// - Düşman AI: Otomatik marş ✓
    /// - Müttefik AI: Otomatik marş ✓
    /// - Oyuncu team'i: Manuel marş (M tuşu) ✗
    /// 
    /// YENİ MANTIK: Formation'lar dinamik olarak marşa girebilir/çıkabilir
    /// Düşman yakınsa normal hız, uzaksa marş hızı
    /// 
    /// Kuşatmalarda savunan team'ler için marş kapalıdır
    /// </summary>
    private void UpdateAIMarch()
    {
        if (Mission.Current == null || Mission.Current.Teams == null)
            return;
            
        float marchDistance = MarchGlobalConfig.Instance.AIMarchDistance;
        
        // Tüm team'leri kontrol et
        foreach (var team in Mission.Current.Teams)
        {
            if (team == null || team.IsPlayerTeam)
                continue; // Oyuncu team'ini atla - onlar M tuşu ile manuel marş eder
            
            // Kuşatmada savunan team ise marş yok
            if (IsDefenderInSiege(team))
            {
                // Bu team'in tüm formation'larını marştan çıkar
                var formationsToRemove = AIMarchingFormations.Where(f => f.Team == team).ToList();
                foreach (var formation in formationsToRemove)
                {
                    AIMarchingFormations.Remove(formation);
                }
                continue; // Bir sonraki team'e geç
            }
            
            // Her formation'ı kontrol et
            foreach (var formation in team.FormationsIncludingEmpty)
            {
                if (formation == null || formation.CountOfUnits == 0)
                    continue;
                
                // ⚡ KRİTİK: Bir kez savaşa girdiyse BİR DAHA MARŞA GİREMEZ!
                if (_formationsInCombat.Contains(formation))
                {
                    // Eğer hala marş listesindeyse çıkart
                    if (AIMarchingFormations.Contains(formation))
                    {
                        AIMarchingFormations.Remove(formation);
                    }
                    continue; // Bu formation marş yasağı altında
                }
                
                // Formation'ın GERÇEK pozisyonunu al (ortalama pozisyon)
                Vec2 formationPos = GetFormationActualPosition(formation);
                
                // En yakın düşman mesafesini bul
                float closestEnemyDistance = GetClosestEnemyDistance(formation, formationPos);
                
                // Mesafe kontrolü - dinamik marş sistemi
                bool shouldMarch = closestEnemyDistance > marchDistance;
                bool isCurrentlyMarching = AIMarchingFormations.Contains(formation);
                
                // Dinamik marş davranışı
                if (shouldMarch && !isCurrentlyMarching)
                {
                    // Düşman uzakta - marş moduna al
                    AIMarchingFormations.Add(formation);
                }
                else if (!shouldMarch && isCurrentlyMarching)
                {
                    // ⚡ Düşman yakın - marştan çıkar VE SAVAŞ MODUNA EKLE!
                    // Bir kez savaşa girince BİR DAHA MARŞA GİREMEZ
                    AIMarchingFormations.Remove(formation);
                    _formationsInCombat.Add(formation); // ← YASAK LİSTESİNE EKLE!
                }
            }
        }
        
        // Değişiklik olduysa agent'ları güncelle
        OnMarch();
    }
    
    /// <summary>
    /// Formation'ın GERÇEK pozisyonunu döndürür (agent'ların ortalama pozisyonu)
    /// OrderPosition değil, formation'daki agent'ların fiziksel konumu
    /// </summary>
    private Vec2 GetFormationActualPosition(Formation formation)
    {
        if (formation == null || formation.CountOfUnits == 0)
            return Vec2.Zero;
        
        // Formation'ın median pozisyonunu al - bu gerçek merkez pozisyonu
        // OrderPosition yerine bu kullanılmalı çünkü formation henüz emre ulaşmamış olabilir
        return formation.GetMedianAgent(excludeDetachedUnits: true, excludePlayer: true, 
                                        formation.GetAveragePositionOfUnits(excludeDetachedUnits: true, excludePlayer: true))
               .Position.AsVec2;
    }
    
    /// <summary>
    /// Formation'ın pozisyonunu döndürür (eski metod - artık kullanılmıyor)
    /// </summary>
    private Vec2 GetFormationPosition(Formation formation)
    {
        if (formation == null)
            return Vec2.Zero;
        
        // Formation'ın emir pozisyonu zaten Vec2 tipinde
        return formation.OrderPosition;
    }
    
    /// <summary>
    /// Formation'a en yakın düşman mesafesini hesaplar
    /// PERFORMANS OPTİMİZASYONU: Maksimum 200 agent kontrol
    /// 
    /// ⚡ OYUNCU KARAKTER DAHİL: Oyuncu formation'da olmasa bile kontrol edilir!
    /// </summary>
    private float GetClosestEnemyDistance(Formation formation, Vec2 formationPos)
    {
        if (formation == null || formation.Team == null || Mission.Current == null)
            return float.MaxValue;
        
        float minDistance = float.MaxValue;
        const int MAX_AGENTS_TO_CHECK = 200; // Performans için maksimum agent sayısı
        
        // ⚡ ÖNCELİKLE OYUNCU KARAKTERİ KONTROL ET!
        // Oyuncu formation'da olmayabilir ama savaşa katılıyordur
        if (Agent.Main != null && Agent.Main.IsActive())
        {
            // Oyuncu farklı team'de mi?
            if (Agent.Main.Team != null && Agent.Main.Team != formation.Team)
            {
                Vec2 playerPos = Agent.Main.Position.AsVec2;
                float playerDistance = (formationPos - playerPos).Length;
                if (playerDistance < minDistance)
                {
                    minDistance = playerDistance;
                }
            }
        }
        
        // Tüm team'leri kontrol et (kendi team'i hariç)
        foreach (var team in Mission.Current.Teams)
        {
            // Sadece kendi team'ini atla
            // Diğer tüm team'ler (düşman + müttefik) mesafe hesabına dahil
            if (team == null || team == formation.Team)
                continue;
            
            // Bu team'in agent'larını kontrol et (maksimum 200 agent)
            int checkedCount = 0;
            foreach (var enemyAgent in team.ActiveAgents)
            {
                if (enemyAgent == null || !enemyAgent.IsActive())
                    continue;
                
                // PERFORMANS: İlk 200 agent'tan sonra dur
                if (checkedCount >= MAX_AGENTS_TO_CHECK)
                    break;
                
                checkedCount++;
                
                // Mesafe hesapla: iki vektör arasındaki uzaklık
                Vec2 enemyPos = enemyAgent.Position.AsVec2;
                float distance = (formationPos - enemyPos).Length;
                if (distance < minDistance)
                {
                    minDistance = distance;
                }
            }
        }
        
        return minDistance;
    }
    
    /// <summary>
    /// Marş toggle edildiğinde çağrılır
    /// ESKİ MOD YÖNTEMİ: UpdateAgentProperties çağır (DoMarching değil!)
    /// UpdateAgentStats otomatik çağrılacak ve DoMarching orada yapılacak
    /// 
    /// NOT: ESKİ MOD sadece Player team'e bakıyordu ama BİZDE AI de var!
    /// </summary>
    private void OnMarch()
    {
        if (Mission.Current == null || Mission.Current.Teams == null)
            return;
        
        // HEM PLAYER HEM AI team'lerini güncelle
        foreach (var team in Mission.Current.Teams)
        {
            if (team == null || team.ActiveAgents == null)
                continue;
            
            foreach (var agent in team.ActiveAgents)
            {
                agent.UpdateAgentProperties();
                if (agent.HasMount)
                {
                    agent.MountAgent.UpdateAgentProperties();
                }
            }
        }
    }
    
    /// <summary>
    /// Her 0.1 saniyede marş hızlarını uygular (PERFORMANS OPTİMİZE EDİLMİŞ)
    
    public override void OnMissionStateDeactivated()
    {
        base.OnMissionStateDeactivated();
        
        // Mission bittiğinde tüm veriyi temizle
        PlayerMarchingFormations.Clear();
        AIMarchingFormations.Clear();
        _formationsInCombat.Clear();
        MarchingAgentStatCalculateModel.ClearSpeedCache(); // Cache'i de temizle
    }
}
