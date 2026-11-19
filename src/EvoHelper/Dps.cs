using System;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;

namespace EvoHelper;

// A rolling damage rate: cumulative damage sampled against the run clock, averaged over
// the last Dps.Window seconds. The run clock follows the game speed setting and stops
// while the game is paused, so a rate reads as "per second at normal speed" and holds
// still on the pause screen.
internal sealed class Meter
{
    readonly Queue<(float t, long dmg)> _samples = new();
    float _lastT;
    long _lastDmg;

    public void Clear() => _samples.Clear();

    public void Add(float now, long dmg)
    {
        // A cumulative counter only goes down when the game replaced it (a ball evolved
        // and its stats moved), so the old samples no longer compare.
        if (_samples.Count > 0 && dmg < _lastDmg) _samples.Clear();
        _lastT = now;
        _lastDmg = dmg;
        _samples.Enqueue((now, dmg));
        while (_samples.Count > 1 && now - _samples.Peek().t > Dps.Window) _samples.Dequeue();
    }

    public long Rate
    {
        get
        {
            if (_samples.Count < 2) return 0;
            var first = _samples.Peek();
            float dt = _lastT - first.t;
            return dt > 0.5f ? (long)((_lastDmg - first.dmg) / dt) : 0;
        }
    }
}

// Damage rates for the whole run and for each source the game keeps its own counter for.
//
// The total counts every hit that lands on an enemy, whatever caused it. The per-source
// rates come from the game's own attribution, the same counters its end-of-run screen
// shows, and they overlap: a passive that boosts a ball's hit is credited on both the
// ball and the passive, and a ball's own baby balls count for the ball and for the
// baby-ball counter. So the per-source rates do not add up to the total.
internal static class Dps
{
    public const float Window = 10f;          // seconds of run time a rate is averaged over
    const float SampleEvery = 0.1f;

    public const string BabyKey = "baby";

    // Running sum of damage landed on enemies, fed by DpsPatches.
    public static long Dealt;
    // False when the damage hook could not be installed; the total then falls back to the
    // balls' counters, which miss damage that no ball is credited for.
    public static bool Hooked;

    public static readonly Meter Total = new();
    static readonly Dictionary<string, Meter> Sources = new();
    static float _lastSample = -1f;

    public static string Key(HeroType t) => "ball" + (int)t;
    public static string Key(PassiveType t) => "passive" + (int)t;
    public static string Key(HeroInst h) => Key(h.Type);
    public static string Key(PassiveInst p) => Key(p.Type);

    public static long Rate(string key) => Sources.TryGetValue(key, out var m) ? m.Rate : 0;

    public static void Sample(BattleSaveData b)
    {
        float now = b.ElapsedTime;
        if (now < _lastSample)   // the run clock went back: a new run
        {
            Total.Clear();
            Sources.Clear();
            _lastSample = -1f;
        }
        if (_lastSample >= 0f && now <= _lastSample + SampleEvery) return;
        _lastSample = now;

        long balls = 0;
        var heroes = b.Heroes;
        if (heroes != null)
            for (int i = 0; i < heroes.Count; i++)
            {
                var h = heroes[i];
                if (h == null) continue;
                long dmg = Damage(h);
                balls += dmg;
                Source(Key(h)).Add(now, dmg);
            }

        var passives = b.Passives;
        if (passives != null)
            for (int i = 0; i < passives.Count; i++)
            {
                var p = passives[i];
                if (p != null) Source(Key(p)).Add(now, Damage(p));
            }

        Source(BabyKey).Add(now, b.BabyDamageDealt);
        Total.Add(now, Hooked ? Interlocked.Read(ref Dealt) : balls);
    }

    static Meter Source(string key)
    {
        if (!Sources.TryGetValue(key, out var m)) Sources[key] = m = new Meter();
        return m;
    }

    // The game starts a new stats entry each time a ball or passive levels up.
    static long Damage(HeroInst h)
    {
        long total = 0;
        var stats = h.StatData;
        if (stats == null) return 0;
        for (int i = 0; i < stats.Count; i++)
        {
            var s = stats[i];
            if (s != null) total += s.BounceDmgDealt + s.StatusEffectDmgDealt + s.AltDmgDealt + s.AOEDmgDealt;
        }
        return total;
    }

    static long Damage(PassiveInst p)
    {
        long total = 0;
        var stats = p.StatData;
        if (stats == null) return 0;
        for (int i = 0; i < stats.Count; i++)
            if (stats[i] != null) total += stats[i].BonusDamage;
        return total;
    }
}

// Every hit on an enemy goes through GridPieceInst.Damage, so counting there gives a total
// that includes baby balls, status effects and passives with no ball behind them.
internal static class DpsPatches
{
    // The amount as the caller passed it, before the method scales it. The game's own
    // per-source counters record that same amount, so the total stays comparable to them.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(GridPieceInst), nameof(GridPieceInst.Damage))]
    static void Before(int amt, out int __state) => __state = amt;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GridPieceInst), nameof(GridPieceInst.Damage))]
    static void After(bool __result, int __state, HitType hitType)
    {
        // The end-of-level sweep kills what is left on the board; it is not damage dealt.
        if (__result && __state > 0 && hitType != HitType.kLevelEndInstantKill)
            Interlocked.Add(ref Dps.Dealt, __state);
    }
}
