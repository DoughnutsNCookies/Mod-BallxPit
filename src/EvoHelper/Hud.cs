using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EvoHelper;

// In-run HUD additions, read-only:
//  - DPS readout at the top middle of the screen (all damage landed on enemies over
//    the last few seconds of run time; see Dps).
//  - "current / max" numbers on the health orb.
//  - "current / needed" numbers on the XP bar.
// F9 toggles all three.
public partial class Hud : MonoBehaviour
{
#if !MONO
    public Hud(IntPtr ptr) : base(ptr) { }
#endif

    const float Refresh = 0.5f;       // real seconds between DPS text updates, so it stays readable

    static bool _hidden;
    bool _errorLogged;

    TextMeshProUGUI _dps, _dpsShadow, _xp, _xpShadow, _hp, _hpShadow;
    RectTransform _dpsBg;
    Canvas _root;
    int _xpLogs, _lastLoggedXp = -1;
    bool _placeLogged;
    InGameUI _builtFor;

    float _nextDpsText;

    void OnGUI()
    {
        var ev = Event.current;
        if (ev != null && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.F9)
        {
            _hidden = !_hidden;
            ev.Use();
        }
    }

    void LateUpdate()
    {
        try { Tick(); }
        catch (Exception e)
        {
            if (!_errorLogged) { _errorLogged = true; Plugin.Log.LogError("HUD: " + e); }
        }
    }

    void Tick()
    {
        var ui = InGameUI.I;
        var battle = BattleSaveData.I;
        if (ui == null || battle == null) { SetVisible(false); return; }

        if (_builtFor == null || _builtFor.Pointer != ui.Pointer || _dps == null) Build(ui);

        UpdateDps(battle);
        RefreshSourceLabels(battle);

        // Safety net in case the screen closed without the game calling Deactivate.
        if (Plugin.LevelUpOpen && (LevelUpUI.I == null || !LevelUpUI.I.isActiveAndEnabled)) Plugin.LevelUpOpen = false;

        // Hidden on the level-up screen, where the bars' spots are covered by the pick panel.
        bool show = !_hidden && !Plugin.LevelUpOpen && ui.gameObject.activeInHierarchy;
        if (show) UpdateBars(ui, battle);
        SetVisible(show);
    }

    // ---------- DPS ----------

    void UpdateDps(BattleSaveData b)
    {
        Dps.Sample(b);
        if (Time.unscaledTime < _nextDpsText) return;
        _nextDpsText = Time.unscaledTime + Refresh;
        SetText(_dps, _dpsShadow, $"DPS <color=#F2C14E>{Fmt(Dps.Total.Rate)}</color>");
        if (_dpsBg != null)
            _dpsBg.sizeDelta = new Vector2(Mathf.Round(_dps.preferredWidth + _dps.fontSize * 1.2f), Mathf.Round(_dps.fontSize * 1.6f));
    }

    // ---------- health / XP numbers ----------

    void UpdateBars(InGameUI ui, BattleSaveData b)
    {
        var mgr = UpgradeMgr.I;
        if (mgr == null) return;

        if (_hp != null)
        {
            // Prefer the game's own (hidden) value text; fall back to the run data.
            string cur = ui.TxtCurHealth != null ? ui.TxtCurHealth.text : null;
            string max = ui.TxtTotalHealth != null ? ui.TxtTotalHealth.text : null;
            string text = !string.IsNullOrWhiteSpace(cur) && !string.IsNullOrWhiteSpace(max)
                ? $"{cur.Trim().TrimStart('/')} / {max.Trim().TrimStart('/').Trim()}"
                : $"{Fmt(Mathf.CeilToInt(b.CurHealth))} / {Fmt(mgr.MaxHealth)}";
            SetText(_hp, _hpShadow, text);
            Follow(ui.HealthFill != null ? ui.HealthFill.rectTransform : null, _hp, false);   // dead centre of the orb
        }

        if (_xp != null)
        {
            int need = mgr.TgtXP;
            // What the game's XP bar is showing right now (BattleSaveData.CurXP is only the saved copy).
            float shown = ui._displayedXP;
            int curXp = Mathf.FloorToInt(Mathf.Max(shown, 0f));
            SetText(_xp, _xpShadow, need > 0 ? $"{Fmt(curXp)} / {Fmt(need)}" : Fmt(curXp));
            Follow(ui.WrapperXP != null ? ui.WrapperXP : ui.XPFill?.rectTransform, _xp, true);   // just above the XP tower

            if (_xpLogs < 6 && curXp != _lastLoggedXp)
            {
                _xpLogs++;
                _lastLoggedXp = curXp;
                Plugin.Log.LogInfo($"HUD check: XP shown {shown}, saved {b.CurXP}, need {need} (bar fill {ui.XPFill?.fillAmount}), HP {b.CurHealth}/{mgr.MaxHealth}, game HP text '{ui.TxtCurHealth?.text}' '{ui.TxtTotalHealth?.text}'");
            }
        }
    }

    // Keeps our label centred on a game HUD element. Both live under the same root canvas,
    // so a plain world -> canvas-local conversion is exact (no cameras involved).
    void Follow(RectTransform target, TextMeshProUGUI label, bool above)
    {
        var rt = label.transform.parent.Cast<RectTransform>();
        if (target == null || !target.gameObject.activeInHierarchy) { rt.gameObject.SetActive(false); return; }
        var rootRt = _root.transform.Cast<RectTransform>();
        var r = target.rect;
        var point = above ? new Vector2(r.center.x, r.yMax) : r.center;
        Vector3 local = rootRt.InverseTransformPoint(target.TransformPoint(point));
        float yOffset = above ? rt.sizeDelta.y * 0.5f + 4f : 0f;
        // Our labels are anchored at the canvas centre; convert from pivot-relative to that.
        Vector2 centre = rootRt.rect.center;
        rt.anchoredPosition = new Vector2(local.x - centre.x, local.y - centre.y + yOffset);
        if (!_hidden && !rt.gameObject.activeSelf) rt.gameObject.SetActive(true);

        // Draw just above the bar's own art: same sorting layer, higher order.
        var tc = target.GetComponentInParent<Canvas>();
        var ours = rt.GetComponent<Canvas>();
        if (tc != null && ours != null)
        {
            int layer = tc.sortingLayerID;
            int order = Math.Max(tc.sortingOrder, tc.rootCanvas != null ? tc.rootCanvas.sortingOrder : 0) + 500;
            if (ours.sortingLayerID != layer) ours.sortingLayerID = layer;
            if (ours.sortingOrder < order) ours.sortingOrder = order;
        }

        if (!_placeLogged && label == _xp)
        {
            _placeLogged = true;
            Plugin.Log.LogInfo($"HUD place: canvas {rootRt.rect} mode {_root.renderMode}; XP bar at {rt.anchoredPosition}, HP orb at {(_hp != null ? _hp.transform.parent.Cast<RectTransform>().anchoredPosition : Vector2.zero)}; bar canvas '{tc?.name}' layer {tc?.sortingLayerName} order {tc?.sortingOrder}, ours layer {ours?.sortingLayerName} order {ours?.sortingOrder}");
        }
    }

    // Invariant culture: some locales group digits with a narrow no-break space the game font lacks (shows as "?").
    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
    internal static string Fmt(long v) =>
        v >= 10_000_000 ? (v / 1_000_000f).ToString("0.#", Inv) + "M"
        : v >= 100_000 ? (v / 1000f).ToString("0.#", Inv) + "k"
        : v.ToString("N0", Inv);

    // ---------- building ----------

    void Build(InGameUI ui)
    {
        if (_dps != null) Destroy(_dps.transform.parent.gameObject);
        if (_xp != null) Destroy(_xp.transform.parent.gameObject);
        if (_hp != null) Destroy(_hp.transform.parent.gameObject);
        _dps = _xp = _hp = null;
        _builtFor = ui;

        var src = ui.TxtCurHealth;
        if (src == null) throw new Exception("InGameUI.TxtCurHealth missing");
        var canvas = src.canvas != null ? src.canvas.rootCanvas : null;
        if (canvas == null) throw new Exception("HUD canvas missing");

        _root = canvas;

        // DPS: top middle of the screen.
        var dpsRt = NewRect("EvoHelperDPS", canvas.transform);
        dpsRt.anchorMin = dpsRt.anchorMax = dpsRt.pivot = new Vector2(0.5f, 1f);
        dpsRt.anchoredPosition = new Vector2(0f, -10f);
        dpsRt.sizeDelta = new Vector2(300f, src.fontSize * 1.2f);
        OnTop(dpsRt);
        // Semi-transparent backing so it reads over busy backgrounds; sized to the text each update.
        _dpsBg = NewRect("Bg", dpsRt);
        _dpsBg.anchorMin = _dpsBg.anchorMax = _dpsBg.pivot = new Vector2(0.5f, 0.5f);
        _dpsBg.sizeDelta = new Vector2(120f, dpsRt.sizeDelta.y);
        var bg = _dpsBg.gameObject.AddComponent<Image>();
        bg.color = new Color(0.06f, 0.04f, 0.06f, 0.6f);
        bg.raycastTarget = false;
        (_dps, _dpsShadow) = ShadowText(dpsRt, src, src.fontSize * 0.75f);

        // Health and XP numbers: on the root canvas, above everything, moved onto the bars each frame.
        (_hp, _hpShadow) = Label("EvoHelperHP", canvas, src, src.fontSize * 0.7f);
        (_xp, _xpShadow) = Label("EvoHelperXP", canvas, src, src.fontSize * 0.6f);
        Plugin.Log.LogInfo($"HUD built on canvas '{canvas.name}' (XP bar {(ui.XPFill != null ? ui.XPFill.name : "missing")}).");
    }

    static (TextMeshProUGUI, TextMeshProUGUI) Label(string name, Canvas canvas, TextMeshProUGUI src, float size)
    {
        var rt = NewRect(name, canvas.transform);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(200f, size * 1.5f);
        OnTop(rt);
        return ShadowText(rt, src, size);
    }

    // Own sorting layer so nested HUD canvases (the health orb) can't draw over it.
    static void OnTop(RectTransform rt)
    {
        var c = rt.gameObject.AddComponent<Canvas>();
        c.overrideSorting = true;
        c.sortingOrder = 1000;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.layer = parent.gameObject.layer;
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    // Text plus a dark copy offset by 2px underneath (outlines crash on new TMP text here).
    static (TextMeshProUGUI, TextMeshProUGUI) ShadowText(RectTransform parent, TextMeshProUGUI src, float size)
    {
        TextMeshProUGUI Make(string name, Vector2 offset, Color color)
        {
            var rt = NewRect(name, parent);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = offset; rt.offsetMax = offset;
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = src.font;
            t.fontSharedMaterial = src.fontSharedMaterial;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.richText = true;
            return t;
        }
        var shadow = Make("Shadow", new Vector2(2f, -2f), new Color(0f, 0f, 0f, 0.85f));
        var main = Make("Text", Vector2.zero, Color.white);
        return (main, shadow);
    }

    static void SetText(TextMeshProUGUI main, TextMeshProUGUI shadow, string text)
    {
        if (main == null || main.text == text) return;
        main.text = text;
        // The shadow ignores colour tags so it stays a plain dark copy.
        shadow.text = System.Text.RegularExpressions.Regex.Replace(text, "</?color[^>]*>", "");
    }

    void SetVisible(bool on)
    {
        if (_dps != null && _dps.transform.parent.gameObject.activeSelf != on) _dps.transform.parent.gameObject.SetActive(on);
        // HP / XP labels: Follow() shows them when their bar is visible; hide them when the HUD is off.
        if (!on)
        {
            if (_xp != null) _xp.transform.parent.gameObject.SetActive(false);
            if (_hp != null) _hp.transform.parent.gameObject.SetActive(false);
        }
    }
}
