using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
#if !MONO
using BepInEx.Unity.IL2CPP;
#endif
using HarmonyLib;

namespace EvoHelper;

// Read-only helper: shows what each offered ball/passive can evolve into.
// It never changes game state or save files.
[BepInPlugin("schuah.ballxpit.evohelper", "EvoHelper", Version)]
#if MONO
// BepInEx 5 on the game's Mono build (macOS): the plugin is itself a component.
public class Plugin : BaseUnityPlugin
#else
public class Plugin : BasePlugin
#endif
{
    public const string Version = "1.4.0";

    internal static bool RecipesDumped;
    internal static bool LevelUpOpen;   // level-up / pick screen is showing

#if MONO
    internal static ManualLogSource Log;

    void Awake()
    {
        Log = Logger;
        Load();
    }

    T AddComponent<T>() where T : UnityEngine.Component => gameObject.AddComponent<T>();

    void Load()
    {
#else
    internal static new ManualLogSource Log;

    public override void Load()
    {
        Log = base.Log;
#endif
        Harmony.CreateAndPatchAll(typeof(Patches));
        try { Harmony.CreateAndPatchAll(typeof(EquipPatches)); }
        catch (Exception e) { Log.LogError("Equipment popup patches failed: " + e); }
        try { Harmony.CreateAndPatchAll(typeof(PadPatches)); }
        catch (Exception e) { Log.LogError("Controller input patches failed; inspect mode may let the game react underneath: " + e); }
        StickSwap.Init(Config);
        try { Harmony.CreateAndPatchAll(typeof(StickSwapPatches)); }
        catch (Exception e) { Log.LogError("Swap sticks input patches failed: " + e); }
        try { Harmony.CreateAndPatchAll(typeof(StickSwapMenuPatches)); }
        catch (Exception e) { Log.LogError("Swap sticks options row failed: " + e); }
        try { Harmony.CreateAndPatchAll(typeof(DpsPatches)); Dps.Hooked = true; }
        catch (Exception e) { Log.LogError("Damage hook failed; DPS falls back to the balls' own counters: " + e); }
        try { Harmony.CreateAndPatchAll(typeof(ItemIconPatches)); }
        catch (Exception e) { Log.LogError("Icon patches failed; no per-item DPS on the pause screen or fusion picker: " + e); }
        try { Harmony.CreateAndPatchAll(typeof(LevelUpStripPatches)); }
        catch (Exception e) { Log.LogError("Level-up strip patches failed; no per-item DPS there: " + e); }
        AddComponent<Overlay>();
        AddComponent<Hud>();
        Log.LogInfo($"EvoHelper {Version} loaded. F8 toggles the evolution block, F9 the DPS / bar numbers.");
    }

    // Writes the game's own recipe list next to the DLL once per session, for checking.
    internal static void DumpRecipes()
    {
        if (RecipesDumped || InfoDB.I == null) return;
        RecipesDumped = true;

        var lines = new List<string>();
        void Dump(string kind, IEnumerable<UpgradeInfo> infos)
        {
            foreach (var inf in infos)
            {
                var sets = inf.MergeComponents;
                if (sets == null || sets.Length == 0) continue;
                foreach (var set in sets)
                {
                    if (set == null || set.Length == 0) continue;
                    var parts = new List<string>();
                    foreach (var c in set) parts.Add(c == null ? "?" : c.Name);
                    lines.Add($"{kind}\t{string.Join(" + ", parts)}\t->\t{inf.Name}\t(inGame={inf.IsInGame}, dlc={inf.DLCVersion})");
                }
            }
        }

        var heroes = new List<UpgradeInfo>();
        foreach (var h in InfoDB.I.Heroes) heroes.Add(h);
        var passives = new List<UpgradeInfo>();
        foreach (var p in InfoDB.I.Passives) passives.Add(p);
        Dump("BALL", heroes);
        Dump("PASSIVE", passives);

        var path = Path.Combine(Paths.PluginPath, "EvoHelper", "recipes-dump.tsv");
        File.WriteAllLines(path, lines);
        Log.LogInfo($"Dumped {lines.Count} recipes to {path}");
    }

    internal static void LogBatch()
    {
        foreach (var o in Evo.Offers)
        {
            if (o == null) continue;
            var useful = o.Evos.Where(e => e.Status != EvoStatus.HaveNone)
                .Select(e => $"{e.Result.Name} [{e.Status}] with {string.Join(" + ", e.Partners.Select(p => p.Owned ? $"{p.Name} Lv{p.Lvl}" : p.Name))}");
            Log.LogInfo($"  Offer [{o.Kind}] {o.Self.Name} new={o.IsNew} ownedLv={o.Self.Lvl}; {o.Evos.Count} evos; useful: {(useful.Any() ? string.Join("; ", useful) : "-")}");
        }
        foreach (var p in Evo.Pairs)
            Log.LogInfo($"  Can make {p.Result.Name}: {string.Join(" + ", p.Parts.Select(x => $"{x.Name} Lv{x.Lvl}"))}");
    }
}

internal static class Patches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(LevelUpUI), nameof(LevelUpUI.Activate))]
    private static void OnLevelUpOpened(LevelUpType t)
    {
        try
        {
            Plugin.LevelUpOpen = true;
            Plugin.Log.LogInfo($"Level-up screen opened (type {t}).");
            Plugin.DumpRecipes();
            Plugin.LogBatch();
        }
        catch (Exception e) { Plugin.Log.LogError(e); }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(LevelUpUI), nameof(LevelUpUI.Deactivate))]
    private static void OnLevelUpClosed()
    {
        Plugin.LevelUpOpen = false;
        try { Evo.Clear(); }
        catch (Exception e) { Plugin.Log.LogError(e); }
    }

    // Which offer the player is looking at (mouse hover or controller highlight).
    [HarmonyPostfix]
    [HarmonyPatch(typeof(LevelUpUI), nameof(LevelUpUI.HoverBtn))]
    private static void OnOfferHovered(LevelUpBtn bItem)
    {
        try { if (bItem != null) Evo.SetHovered(bItem.ChoiceIdx); }
        catch (Exception e) { Plugin.Log.LogError(e); }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(LevelUpBtn), nameof(LevelUpBtn.SetHero))]
    private static void OnBallOffered(LevelUpBtn __instance, int idx)
    {
        try { Evo.SetOffer(idx, "ball", __instance); }
        catch (Exception e) { Plugin.Log.LogError(e); }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(LevelUpBtn), nameof(LevelUpBtn.SetPassive))]
    private static void OnPassiveOffered(LevelUpBtn __instance, int idx)
    {
        try { Evo.SetOffer(idx, "passive", __instance); }
        catch (Exception e) { Plugin.Log.LogError(e); }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(LevelUpBtn), nameof(LevelUpBtn.SetUpgrade))]
    private static void OnUpgradeOffered(LevelUpBtn __instance, int idx)
    {
        try { Evo.SetOffer(idx, "upgrade", __instance); }
        catch (Exception e) { Plugin.Log.LogError(e); }
    }
}
