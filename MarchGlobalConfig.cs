using System;
using System.Collections.Generic;
using System.Linq;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using MCM.Common;
using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade;

namespace Marching;

/// <summary>
/// Marching modu için global ayarlar
/// MCM (Mod Configuration Menu) kullanarak oyun içi ayar ekranı sağlar
/// </summary>
internal sealed class MarchGlobalConfig : AttributeGlobalSettings<MarchGlobalConfig> 
{
    // Formation hız cache'i - her formation için hızı bir kez hesapla, cache'le
    private static Dictionary<Formation, float> _formationSpeedCache = new Dictionary<Formation, float>();
    
    public override string Id => "MarchingConfig";
    public override string DisplayName => "Marching";
    public override string FolderName => "Marching";
    public override string FormatType => "json2";

    public MarchGlobalConfig()
    {

        var keyNames = GetKeyNames();
        var mIndex = Array.IndexOf(keyNames, "M");
        MarchingHotKey = new Dropdown<string>(keyNames, mIndex >= 0 ? mIndex : 0);
    }

    /// <summary>
    /// InputKey enum'ındaki tüm tuş isimlerini alır
    /// </summary>
    private static string[] GetKeyNames() => Enum.GetNames(typeof(InputKey));
    
    /// <summary>
    /// Marching emri için kullanılacak hotkey
    /// </summary>
    [SettingPropertyDropdown(
        displayName: "{=march_order_key}March Order Key", 
        RequireRestart = true,
        HintText = "{=march_order_key_hint}Press this key while order menu is open to toggle march mode")]
    [SettingPropertyGroup(
        groupName: "{=march_settings}March Settings",
        GroupOrder = 0)]
    public Dropdown<string> MarchingHotKey { get; set; }

    /// <summary>
    /// Artemis modu desteği - mızrak animasyonları için
    /// </summary>
    [SettingPropertyBool(
        displayName: "{=artemis_support}Artemis Spear Animation Support", 
        RequireRestart = true,
        HintText = "{=artemis_support_hint}Enable if you use Artems Lively Animations for better spear animations")]
    [SettingPropertyGroup(
        groupName: "{=march_settings}March Settings",
        GroupOrder = 0)]
    public bool ArtemisSupport { get; set; } = true;
    
    // ========== FORMATION BAŞINA HIZ AYARLARI ==========
    
    /// <summary>
    /// Piyade (Infantry) marş hızı
    /// </summary>
    [SettingPropertyFloatingInteger(
        displayName: "{=infantry_speed}Infantry Speed", 
        minValue: 0.1f, 
        maxValue: 1.0f, 
        RequireRestart = false,
        HintText = "{=infantry_speed_hint}Marching speed for infantry units")]
    [SettingPropertyGroup(
        groupName: "{=formation_speeds}Formation Speeds",
        GroupOrder = 1)]
    public float InfantrySpeed { get; set; } = 0.25f;
    
    /// <summary>
    /// Menzilli (Ranged/Archers) marş hızı
    /// </summary>
    [SettingPropertyFloatingInteger(
        displayName: "{=ranged_speed}Ranged Speed", 
        minValue: 0.1f, 
        maxValue: 1.0f, 
        RequireRestart = false,
        HintText = "{=ranged_speed_hint}Marching speed for ranged units")]
    [SettingPropertyGroup(
        groupName: "{=formation_speeds}Formation Speeds",
        GroupOrder = 1)]
    public float RangedSpeed { get; set; } = 0.25f;
    
    /// <summary>
    /// Süvari (Cavalry - tüm atlılar) marş hızı
    /// </summary>
    [SettingPropertyFloatingInteger(
        displayName: "{=cavalry_speed}Cavalry Speed", 
        minValue: 0.1f, 
        maxValue: 1.0f, 
        RequireRestart = false,
        HintText = "{=cavalry_speed_hint}Marching speed for all cavalry units")]
    [SettingPropertyGroup(
        groupName: "{=formation_speeds}Formation Speeds",
        GroupOrder = 1)]
    public float CavalrySpeed { get; set; } = 0.25f;
    
    // ========== AI MARŞ AYARLARI ==========
    
    /// <summary>
    /// AI birliklerinin marş kullanıp kullanamayacağı
    /// </summary>
    [SettingPropertyBool(
        displayName: "{=ai_march_enabled}Enable AI March", 
        RequireRestart = false,
        HintText = "{=ai_march_enabled_hint}Allow AI controlled formations to use march mode automatically")]
    [SettingPropertyGroup(
        groupName: "{=ai_march_settings}AI March Settings",
        GroupOrder = 2)]
    public bool EnableAIMarch { get; set; } = true;
    
    /// <summary>
    /// AI marş mesafe eşiği (metre)
    /// Düşmandan bu kadar uzakta olunca marş kullanır
    /// </summary>
    [SettingPropertyFloatingInteger(
        displayName: "{=ai_march_distance}March Distance", 
        minValue: 0f, 
        maxValue: 500f, 
        RequireRestart = false,
        HintText = "{=ai_march_distance_hint}AI uses march when enemies are farther than this distance (meters)")]
    [SettingPropertyGroup(
        groupName: "{=ai_march_settings}AI March Settings",
        GroupOrder = 2)]
    public float AIMarchDistance { get; set; } = 65f;
    
    /// <summary>
    /// Verilen formation index'ine göre hızı döndürür
    /// Basitleştirilmiş: Sadece 3 kategori - Piyade, Menzilli, Süvari
    /// </summary>
    public float GetSpeedForFormation(int formationIndex)
    {
        return formationIndex switch
        {
            0 => InfantrySpeed,      // Infantry
            1 => RangedSpeed,        // Ranged (Archers)
            2 => CavalrySpeed,       // Cavalry
            3 => CavalrySpeed,       // HorseArcher - Süvari kategorisi
            4 => RangedSpeed,        // Skirmisher - Menzilli kategorisi
            5 => InfantrySpeed,      // HeavyInfantry - Piyade kategorisi
            6 => CavalrySpeed,       // LightCavalry - Süvari kategorisi
            7 => CavalrySpeed,       // HeavyCavalry - Süvari kategorisi
            _ => InfantrySpeed       // Default - Piyade hızı
        };
    }
    
    /// <summary>
    /// Formation için hızı cache'ten alır, yoksa hesaplar ve cache'ler
    /// PERFORMANS: Her agent için tekrar hesaplama yerine formation başına 1 kez (60x kazanç)
    /// </summary>
    public static float GetCachedFormationSpeed(Formation formation)
    {
        if (formation == null)
            return Instance.InfantrySpeed; // Default
        
        // Cache'te var mı kontrol et
        if (!_formationSpeedCache.TryGetValue(formation, out float speed))
        {
            // İlk kez - hesapla ve cache'le
            speed = Instance.GetSpeedForFormation((int)formation.FormationIndex);
            _formationSpeedCache[formation] = speed;
        }
        
        return speed;
    }
    
    /// <summary>
    /// Hız cache'ini temizler - config değişikliği veya mission sonu için
    /// </summary>
    public static void ClearSpeedCache()
    {
        _formationSpeedCache.Clear();
    }
}
