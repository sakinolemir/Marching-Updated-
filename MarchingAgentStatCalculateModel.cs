#nullable disable
using System.Collections.Generic;
using SandBox.GameComponents;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace Marching;

/// <summary>
/// Campaign mod için Agent stat hesaplama modeli
/// Marching durumundaki birlikler için hız ayarlamaları yapar
/// </summary>
public class MarchingAgentStatCalculateModel : SandboxAgentStatCalculateModel
{
    private readonly AgentStatCalculateModel _previousModel;
    
    // Formation hız cache'i - her formation için hızı bir kez hesapla, sonra cache'ten kullan
    // MarchMissionBehavior tarafından 1 saniyede bir temizlenir
    private static Dictionary<Formation, float> _formationSpeedCache = new Dictionary<Formation, float>();
    
    public MarchingAgentStatCalculateModel(AgentStatCalculateModel previousModel)
    {
        _previousModel = previousModel;
    }
    
    public override void InitializeAgentStats(
        Agent agent, 
        Equipment spawnEquipment, 
        AgentDrivenProperties agentDrivenProperties,
        AgentBuildData agentBuildData)
    {
        // Önce mevcut model'in stat'larını uygula
        _previousModel.InitializeAgentStats(agent, spawnEquipment, agentDrivenProperties, agentBuildData);
        
        // Sonra marching ayarlarını uygula
        DoMarching(agent);
    }

    public override void UpdateAgentStats(Agent agent, AgentDrivenProperties agentDrivenProperties)
    {
        // Önce mevcut model'in stat'larını uygula
        _previousModel.UpdateAgentStats(agent, agentDrivenProperties);
        
        // Marş hızlarını uygula - O KADAR!
        DoMarching(agent);
    }

    public static bool IsMarching(Agent agent)
    {
        if (agent == null)
            return false;
            
        Formation formation = null;
        
        if (agent.IsMount)
        {
            if (agent.RiderAgent?.Formation == null)
                return false;
            formation = agent.RiderAgent.Formation;
        }
        else
        {
            formation = agent.Formation;
        }
        
        if (formation == null)
            return false;
        
        // Her iki listede de kontrol et: Manuel (oyuncu) veya Otomatik (AI)
        return MarchMissionBehavior.PlayerMarchingFormations.Contains(formation) || 
               MarchMissionBehavior.AIMarchingFormations.Contains(formation);
    }

    /// <summary>
    /// Formation için hızı cache'ten alır, yoksa hesaplar ve cache'ler
    /// PERFORMANS: Her agent için tekrar hesaplama yerine formation başına 1 kez (60x kazanç)
    /// </summary>
    public static float GetCachedFormationSpeed(Formation formation)
    {
        if (formation == null)
            return MarchGlobalConfig.Instance.InfantrySpeed; // Default
        
        // Cache'te var mı kontrol et
        if (!_formationSpeedCache.TryGetValue(formation, out float speed))
        {
            // İlk kez - hesapla ve cache'le
            speed = MarchGlobalConfig.Instance.GetSpeedForFormation((int)formation.FormationIndex);
            _formationSpeedCache[formation] = speed;
        }
        
        return speed;
    }
    
    /// <summary>
    /// Hız cache'ini temizler - MarchMissionBehavior tarafından 1 saniyede bir çağrılır
    /// Config değişiklikleri için gerekli
    /// </summary>
    public static void ClearSpeedCache()
    {
        _formationSpeedCache.Clear();
    }

    public static void DoMarching(Agent agent)
    {
        // Oyuncu karakterini etkileme
        if (agent != null && agent.IsMainAgent)
            return;
            
        // Oyuncunun bineği mi kontrol et
        if (agent != null && agent.IsMount && agent.RiderAgent != null && agent.RiderAgent.IsMainAgent)
            return;
            
        // Marş değilse hiçbir şey yapma - normal hızlarını korur
        if (!IsMarching(agent))
            return;

        // Formation tipine göre hız al - CACHE KULLANILIR! (60x daha hızlı)
        float speed = MarchGlobalConfig.Instance.InfantrySpeed; // Default - piyade hızı
        
        // Agent'ın formation'ını kontrol et
        if (agent.IsMount)
        {
            // Mount için rider'ın formation'ına bak
            if (agent.RiderAgent != null && agent.RiderAgent.Formation != null)
            {
                // ✅ OPTİMİZASYON: Cache'ten al!
                speed = GetCachedFormationSpeed(agent.RiderAgent.Formation);
            }
        }
        else if (agent.Formation != null)
        {
            // Normal agent için kendi formation'ına bak
            // ✅ OPTİMİZASYON: Cache'ten al!
            speed = GetCachedFormationSpeed(agent.Formation);
        }
        
        // ⚡ ESKI MOD YÖNTEMİ - BASİT VE ETKİLİ ⚡
        if (!agent.IsMount)
        {
            // Rider için MaxSpeedMultiplier
            agent.SetAgentDrivenPropertyValueFromConsole(DrivenProperty.MaxSpeedMultiplier, speed);
            agent.SetAgentDrivenPropertyValueFromConsole(DrivenProperty.CombatMaxSpeedMultiplier, speed);
            agent.UpdateCustomDrivenProperties();
            return; // ← Burada çık, mount'a dokunma!
        }

        // ⚡ MOUNT İÇİN FARKLI PROPERTY - İŞTE GİZLİ ÇÖZÜM! ⚡
        // MountSpeed kullan (eski modun sırrı!)
        agent.SetAgentDrivenPropertyValueFromConsole(DrivenProperty.MountSpeed, speed + 2.25f);
        agent.UpdateCustomDrivenProperties();
    }
    
    /// <summary>
    /// Agent'ın hızını normale döndürür (marş iptal edildiğinde)
    /// </summary>
    public static void ResetSpeed(Agent agent)
    {
        // Oyuncu karakterini etkileme
        if (agent != null && agent.IsMainAgent)
            return;
            
        // Oyuncunun bineği mi kontrol et
        if (agent != null && agent.IsMount && agent.RiderAgent != null && agent.RiderAgent.IsMainAgent)
            return;
            
        // UpdateAgentProperties çağır, oyun default değerleri uygulasın
        if (agent != null && agent.IsActive())
        {
            agent.UpdateAgentProperties();
            
            // Eğer binekli ise, bineği de resetle
            if (agent.HasMount && agent.MountAgent != null && agent.MountAgent.IsActive())
            {
                agent.MountAgent.UpdateAgentProperties();
            }
        }
    }
}

/// <summary>
/// Custom Battle modu için Agent stat hesaplama modeli
/// </summary>
public class CustomMarchingAgentStatCalculateModel : CustomBattleAgentStatCalculateModel
{
    private readonly AgentStatCalculateModel _previousModel;

    public CustomMarchingAgentStatCalculateModel(AgentStatCalculateModel previousModel)
    {
        _previousModel = previousModel;
    }
    
    public override void InitializeAgentStats(
        Agent agent, 
        Equipment spawnEquipment, 
        AgentDrivenProperties agentDrivenProperties,
        AgentBuildData agentBuildData)
    {
        // Önce mevcut model'in stat'larını uygula
        _previousModel.InitializeAgentStats(agent, spawnEquipment, agentDrivenProperties, agentBuildData);
        
        // Sonra marching ayarlarını uygula
        MarchingAgentStatCalculateModel.DoMarching(agent);
    }

    public override void UpdateAgentStats(Agent agent, AgentDrivenProperties agentDrivenProperties)
    {
        // Önce mevcut model'in stat'larını uygula
        _previousModel.UpdateAgentStats(agent, agentDrivenProperties);
        
        // Marş hızlarını uygula - O KADAR!
        MarchingAgentStatCalculateModel.DoMarching(agent);
    }
}
