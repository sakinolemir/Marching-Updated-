using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace Marching;

/// <summary>
/// Harmony patch - Marching durumundaki birliklerin yürüme modunda olmasını sağlar
/// </summary>
[HarmonyPatch(typeof(Agent), "WalkMode", MethodType.Getter)]
public static class WalkModePatch
{
    public static void Postfix(ref bool __result, Agent __instance)
    {
        if (MarchingAgentStatCalculateModel.IsMarching(__instance))
        {
            __result = true;
        }
    }
}

/// <summary>
/// Ana SubModule sınıfı - Modun giriş noktası
/// </summary>
public class SubModule : MBSubModuleBase
{
    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
    }

    protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
    {
        base.OnGameStart(game, gameStarterObject);
        
        // Harmony patch'lerini uygula
        new Harmony("com.marching").PatchAll();
        
        // Campaign modda özel model ekle
        if (gameStarterObject is CampaignGameStarter campaignGameStarter)
        {
            var existingModel = campaignGameStarter.GetExistingModel<AgentStatCalculateModel>();
            if (existingModel != null)
            {
                campaignGameStarter.AddModel(new MarchingAgentStatCalculateModel(existingModel));
            }
            return;
        }
        
        // Custom Battle modda farklı model kullan
        var battleModel = gameStarterObject.GetExistingModel<AgentStatCalculateModel>();
        if (battleModel != null)
        {
            gameStarterObject.AddModel(new CustomMarchingAgentStatCalculateModel(battleModel));
        }
    }

    public override void OnMissionBehaviorInitialize(Mission mission)
    {
        base.OnMissionBehaviorInitialize(mission);
        mission.AddMissionBehavior(new MarchMissionBehavior());
    }
}

/// <summary>
/// Yardımcı metodlar
/// </summary>
internal static class Helper
{
    /// <summary>
    /// Mevcut model'i GameStarter'dan alır
    /// </summary>
    public static TBaseModel? GetExistingModel<TBaseModel>(this IGameStarter gameStarter) 
        where TBaseModel : GameModel
    {
        try
        {
            // 1.3.x'te Models koleksiyonundan doğru model'i buluyoruz
            var model = gameStarter.Models
                .LastOrDefault(m => m is TBaseModel || m.GetType().IsSubclassOf(typeof(TBaseModel)));
            
            return model as TBaseModel;
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Formation'ın görünen ismini alır (ör: "Infantry 1")
    /// </summary>
    public static string GetFormationName(this Formation formation)
    {
        if (formation == null) return "Unknown";
        
        try
        {
            var formationClassTexts = GameTexts.FindAllTextVariations("str_troop_group_name");
            if (formationClassTexts != null && (int)formation.RepresentativeClass < formationClassTexts.Count())
            {
                string className = formationClassTexts.ElementAt((int)formation.RepresentativeClass).ToString();
                return $"{className} {formation.Index + 1}";
            }
        }
        catch
        {
            // Hata durumunda basit isim döndür
        }
        
        return $"Formation {formation.Index + 1}";
    }
}
