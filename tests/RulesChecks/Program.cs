using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using FoodTuner;

// Checks the Unity-free rules: override file format, number parsing and effective values.
internal static class Program
{
    private static int passed, failed;

    private static void Check(string name, bool ok)
    {
        if (ok) passed++;
        else { failed++; Console.WriteLine("FAIL: " + name); }
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) < 0.0001f;

    private static int Main()
    {
        // The game may run under a Turkish culture; parsing must not depend on it.
        Thread.CurrentThread.CurrentCulture = new CultureInfo("tr-TR");
        var m = new Multipliers();

        Check("identity keeps baseline", Near(FoodMath.Effective(Stat.Health, 50, null, m), 50));
        m.Duration = 2f; m.Health = 1.5f;
        Check("duration multiplier", Near(FoodMath.Effective(Stat.Duration, 1800, null, m), 3600));
        Check("health multiplier", Near(FoodMath.Effective(Stat.Health, 40, null, m), 60));
        Check("override beats multiplier", Near(FoodMath.Effective(Stat.Health, 40, 99, m), 99));
        Check("unused stat stays zero", Near(FoodMath.Effective(Stat.Eitr, 0, null, m), 0));
        Check("unused duration not raised to minimum", Near(FoodMath.Effective(Stat.Duration, 0, null, m), 0));
        Check("override can set unused stat", Near(FoodMath.Effective(Stat.Eitr, 0, 25, m), 25));
        Check("duration minimum 1s", Near(FoodMath.Effective(Stat.Duration, 1800, 0, m), 1));
        m.Duration = 0f;
        Check("zero multiplier clamps duration to 1s", Near(FoodMath.Effective(Stat.Duration, 1800, null, m), 1));
        m.EffectStrength = 2f;
        Check("regen bonus doubled", Near(FoodMath.Effective(Stat.HealthRegen, 1.5f, null, m), 2f));
        Check("regen penalty doubled", Near(FoodMath.Effective(Stat.StaminaRegen, 0.8f, null, m), 0.6f));
        Check("over time scaled", Near(FoodMath.Effective(Stat.HealthOverTime, 50, null, m), 100));
        m.EffectStrength = -3f;
        Check("negative multiplier treated as zero", Near(FoodMath.Effective(Stat.HealthRegen, 1.5f, null, m), 1f));

        Check("comma decimal parses", Stats.TryParseNumber("1,5", out var v) && Near(v, 1.5f));
        Check("dot decimal parses", Stats.TryParseNumber(" 2.25 ", out v) && Near(v, 2.25f));
        Check("garbage rejected", !Stats.TryParseNumber("abc", out _));
        Check("NaN rejected", !Stats.TryParseNumber("NaN", out _));
        Check("clock minutes", Stats.Clock(1800) == "30:00");
        Check("clock hours", Stats.Clock(3725) == "1:02:05");

        var errors = new List<string>();
        var table = OverrideTable.Parse("# yorum\nCookedMeat: burn=1800, HEALTH=40,5\n\nMeadHealthMinor: ttl=60; hpOverTime=75\nBad line\nX: nope=3, regen=abc\n", errors);
        Check("parse count", table.Count == 2);
        Check("parse burn", table.Get("CookedMeat", Stat.Duration) == 1800f);
        Check("case-insensitive key", table.Get("CookedMeat", Stat.Health) == 40f);
        Check("semicolon separator", table.Get("MeadHealthMinor", Stat.HealthOverTime) == 75f);
        Check("missing value null", table.Get("CookedMeat", Stat.Stamina) == null);
        Check("errors reported", errors.Count == 4);
        // "HEALTH=40,5" splits on the comma: 40 is kept, "5" is reported as malformed.

        var again = OverrideTable.Parse(table.Serialize(), errors = new List<string>());
        Check("round trip no errors", errors.Count == 0);
        Check("round trip values", again.Get("MeadHealthMinor", Stat.EffectDuration) == 60f && again.Get("CookedMeat", Stat.Duration) == 1800f);

        table.Set("CookedMeat", Stat.Duration, -5);
        Check("set clamps to minimum", table.Get("CookedMeat", Stat.Duration) == 1f);
        table.Clear("CookedMeat", Stat.Duration); table.Clear("CookedMeat", Stat.Health);
        Check("clearing last stat removes item", !table.Has("CookedMeat"));
        table.ClearItem("MeadHealthMinor");
        Check("clear item", table.Count == 0);

        Console.WriteLine($"{passed} geçti, {failed} başarısız.");
        return failed == 0 ? 0 : 1;
    }
}
