using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EvoHelper;

// The damage rate of each equipped ball and passive, drawn on the game's own icons for
// them: the pause screen's equipment tab (plus one extra entry there for the baby balls),
// the level-up screen's current-equipment strip, and the fusion picker. F9 hides them with
// the rest of the HUD numbers.
public partial class Hud
{
    const string LabelName = "EvoHelperDps", BabyName = "EvoHelperBaby";

    sealed class SourceLabel
    {
        public RectTransform Host;
        public string Key;
        public TextMeshProUGUI Main, Shadow;
    }

    // One label per icon. The game reuses its icons for other items and for empty slots,
    // so the entry is re-keyed every time the game fills an icon in.
    static readonly Dictionary<IntPtr, SourceLabel> SourceLabels = new();

    internal static bool Hidden => _hidden;

    internal static void Track(RectTransform host, string key)
    {
        var src = InGameUI.I != null ? InGameUI.I.TxtCurHealth : null;
        if (host == null || src == null) return;

        if (!SourceLabels.TryGetValue(host.Pointer, out var label) || label.Main == null)
        {
            var rt = NewRect(LabelName, host);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            float size = Mathf.Max(8f, src.fontSize * 0.5f);
            rt.sizeDelta = new Vector2(0f, size * 1.4f);
            rt.anchoredPosition = Vector2.zero;
            var (main, shadow) = ShadowText(rt, src, size);
            SourceLabels[host.Pointer] = label = new SourceLabel { Host = host, Main = main, Shadow = shadow };
        }
        label.Key = key;
        label.Main.transform.parent.SetAsLastSibling();
    }

    // The icon now shows something that has no damage rate (an empty slot, a pet upgrade).
    internal static void Untrack(RectTransform host)
    {
        if (host != null && SourceLabels.TryGetValue(host.Pointer, out var label)) label.Key = null;
    }

    // The baby-ball entry: an icon cell of our own at the end of the pause screen's row.
    internal static void TrackBabies(PauseUI pause)
    {
        var row = pause.WrapperEquipBtns;
        if (row == null) return;

        RectTransform cell = null;
        for (int i = 0; i < row.childCount; i++)
            if (row.GetChild(i).name == BabyName) cell = row.GetChild(i).Cast<RectTransform>();

        if (cell == null)
        {
            // Same footprint as the game's own icons, whichever layout the row uses.
            var sample = Enumerable.Range(0, row.childCount).Select(i => row.GetChild(i))
                .FirstOrDefault(c => c.gameObject.activeSelf)?.Cast<RectTransform>();
            var size = sample != null && sample.rect.width > 1f ? sample.rect.size : new Vector2(50f, 50f);

            cell = NewRect(BabyName, row);
            cell.sizeDelta = size;
            var layout = cell.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = size.x;
            layout.preferredHeight = size.y;

            var iconRt = NewRect("Icon", cell);
            iconRt.anchorMin = iconRt.anchorMax = iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.sizeDelta = Mathf.Min(size.x, size.y) * 0.5f * Vector2.one;
            var icon = iconRt.gameObject.AddComponent<Image>();
            icon.sprite = IconMgr.I != null ? IconMgr.I.IconBabyBall : null;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
        }
        cell.SetAsLastSibling();
        Track(cell, Dps.BabyKey);
    }

    void RefreshSourceLabels(BattleSaveData b)
    {
        if (SourceLabels.Count == 0) return;
        List<IntPtr> gone = null;
        foreach (var entry in SourceLabels)
        {
            var label = entry.Value;
            if (label.Host == null || label.Main == null) { (gone ??= new()).Add(entry.Key); continue; }
            if (!label.Host.gameObject.activeInHierarchy && label.Key != Dps.BabyKey) continue;

            // Nothing to say about a passive that deals no damage of its own, or about baby
            // balls in a run that has none.
            long rate = _hidden || label.Key == null ? 0 : Dps.Rate(label.Key);
            if (label.Key == Dps.BabyKey)
            {
                bool any = b.BabyDamageDealt > 0 && !_hidden;
                if (label.Host.gameObject.activeSelf != any) label.Host.gameObject.SetActive(any);
            }
            string text = rate > 0 ? Fmt(rate) : "";
            if (label.Main.text != text)
            {
                label.Main.text = text;
                label.Shadow.text = text;
            }
        }
        if (gone != null) foreach (var key in gone) SourceLabels.Remove(key);
    }
}

// Every way the level-up screen fills a current-equipment slot. Only a slot holding a ball
// or passive the player owns has a damage rate; previews of offered items, pet upgrades and
// empty slots do not.
[HarmonyPatch]
internal static class LevelUpStripPatches
{
    static IEnumerable<MethodBase> TargetMethods() =>
        AccessTools.GetDeclaredMethods(typeof(LevelUpCurEquipItem))
            .Where(m => m.Name == nameof(LevelUpCurEquipItem.Init) || m.Name == nameof(LevelUpCurEquipItem.InitEmpty));

    static void Postfix(LevelUpCurEquipItem __instance)
    {
        try
        {
            var item = __instance;
            bool owned = item.TgtEquipmentIdx >= 0;
            if (owned && item.TgtHero != HeroType.kNum) Hud.Track(item.Xfm, Dps.Key(item.TgtHero));
            else if (owned && item.TgtPassive != PassiveType.kNum) Hud.Track(item.Xfm, Dps.Key(item.TgtPassive));
            else Hud.Untrack(item.Xfm);
        }
        catch (Exception e) { Plugin.Log.LogError(e); }
    }
}

internal static class ItemIconPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ComboSelectItem), nameof(ComboSelectItem.InitComboComponent))]
    static void OnFusionChoice(ComboSelectItem __instance, int heroIdx)
    {
        try { Hud.Track(__instance.Xfm, Dps.Key(BattleSaveData.I.Heroes[heroIdx])); }
        catch (Exception e) { Plugin.Log.LogError(e); }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PauseEquipInfoBtn), nameof(PauseEquipInfoBtn.InitHero))]
    static void OnBallIcon(PauseEquipInfoBtn __instance, HeroInst h)
    {
        try { Hud.Track(__instance.transform.Cast<RectTransform>(), Dps.Key(h)); }
        catch (Exception e) { Plugin.Log.LogError(e); }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PauseEquipInfoBtn), nameof(PauseEquipInfoBtn.InitPassive))]
    static void OnPassiveIcon(PauseEquipInfoBtn __instance, PassiveInst p)
    {
        try { Hud.Track(__instance.transform.Cast<RectTransform>(), Dps.Key(p)); }
        catch (Exception e) { Plugin.Log.LogError(e); }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PauseUI), nameof(PauseUI.Activate))]
    static void OnPauseOpened(PauseUI __instance)
    {
        try { Hud.TrackBabies(__instance); }
        catch (Exception e) { Plugin.Log.LogError(e); }
    }
}
