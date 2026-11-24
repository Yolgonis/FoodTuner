using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FoodTuner
{
    // Unity-free rules: stat metadata, override file format and effective value math.
    // Kept separate so tests can compile it without the game assemblies.
    public enum Stat
    {
        Duration, Health, Stamina, Eitr, Regen,
        EffectDuration, HealthRegen, StaminaRegen, EitrRegen, HealthOverTime, StaminaOverTime, EitrOverTime
    }

    public enum StatKind { Seconds, Amount, RegenMultiplier }

    public sealed class StatInfo
    {
        public readonly Stat Stat;
        public readonly string Key, Label;
        public readonly StatKind Kind;
        public readonly bool Effect;
        public readonly float Minimum;

        public StatInfo(Stat stat, string key, string label, StatKind kind, bool effect, float minimum)
        {
            Stat = stat; Key = key; Label = label; Kind = kind; Effect = effect; Minimum = minimum;
        }
    }

    public static class Stats
    {
        public static readonly StatInfo[] All =
        {
            new StatInfo(Stat.Duration, "burn", "Süre (sn)", StatKind.Seconds, false, 1f),
            new StatInfo(Stat.Health, "health", "Can", StatKind.Amount, false, 0f),
            new StatInfo(Stat.Stamina, "stamina", "Dayanıklılık", StatKind.Amount, false, 0f),
            new StatInfo(Stat.Eitr, "eitr", "Eitr", StatKind.Amount, false, 0f),
            new StatInfo(Stat.Regen, "regen", "Can yenileme (tick)", StatKind.Amount, false, 0f),
            new StatInfo(Stat.EffectDuration, "ttl", "Etki süresi / bekleme (sn)", StatKind.Seconds, true, 1f),
            new StatInfo(Stat.HealthRegen, "hpRegen", "Can yenileme çarpanı", StatKind.RegenMultiplier, true, 0f),
            new StatInfo(Stat.StaminaRegen, "stRegen", "Dayanıklılık yenileme çarpanı", StatKind.RegenMultiplier, true, 0f),
            new StatInfo(Stat.EitrRegen, "eitrRegen", "Eitr yenileme çarpanı", StatKind.RegenMultiplier, true, 0f),
            new StatInfo(Stat.HealthOverTime, "hpOverTime", "Zamanla can", StatKind.Amount, true, 0f),
            new StatInfo(Stat.StaminaOverTime, "stOverTime", "Zamanla dayanıklılık", StatKind.Amount, true, 0f),
            new StatInfo(Stat.EitrOverTime, "eitrOverTime", "Zamanla eitr", StatKind.Amount, true, 0f),
        };

        public static StatInfo Info(Stat stat) => All[(int)stat];

        public static bool TryKey(string key, out Stat stat)
        {
            foreach (var info in All)
                if (string.Equals(info.Key, key, StringComparison.OrdinalIgnoreCase)) { stat = info.Stat; return true; }
            stat = default;
            return false;
        }

        public static bool TryParseNumber(string text, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(text)) return false;
            // Turkish keyboards type decimal commas; the file always uses dots.
            return float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        public static string Clock(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int total = (int)Math.Round(seconds);
            return total >= 3600 ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}" : $"{total / 60}:{total % 60:00}";
        }
    }

    public sealed class Multipliers
    {
        public float Duration = 1f, Health = 1f, Stamina = 1f, Eitr = 1f, Regen = 1f, EffectDuration = 1f, EffectStrength = 1f;
    }

    public static class FoodMath
    {
        public static float Effective(Stat stat, float baseline, float? overrideValue, Multipliers m)
        {
            // Unused stats (a mead's food duration, a food's eitr) stay untouched unless overridden.
            if (!overrideValue.HasValue && baseline <= 0f) return baseline;
            return Math.Max(Stats.Info(stat).Minimum, overrideValue ?? Scaled(stat, baseline, m));
        }

        private static float Scaled(Stat stat, float baseline, Multipliers m)
        {
            switch (stat)
            {
                case Stat.Duration: return baseline * Clamp(m.Duration);
                case Stat.Health: return baseline * Clamp(m.Health);
                case Stat.Stamina: return baseline * Clamp(m.Stamina);
                case Stat.Eitr: return baseline * Clamp(m.Eitr);
                case Stat.Regen: return baseline * Clamp(m.Regen);
                case Stat.EffectDuration: return baseline * Clamp(m.EffectDuration);
                // A regen multiplier of 1.5 is a +50% bonus: strength scales the bonus, not the 1.
                case Stat.HealthRegen:
                case Stat.StaminaRegen:
                case Stat.EitrRegen: return 1f + (baseline - 1f) * Clamp(m.EffectStrength);
                default: return baseline * Clamp(m.EffectStrength);
            }
        }

        private static float Clamp(float multiplier) => float.IsNaN(multiplier) || multiplier < 0f ? 0f : multiplier;
    }

    // Per-item overrides, stored as editable text lines: "CookedMeat: burn=1800, health=40".
    public sealed class OverrideTable
    {
        private readonly Dictionary<string, Dictionary<Stat, float>> items =
            new Dictionary<string, Dictionary<Stat, float>>(StringComparer.Ordinal);

        public IEnumerable<string> Items => items.Keys;
        public int Count => items.Count;

        public float? Get(string prefab, Stat stat) =>
            items.TryGetValue(prefab, out var stats) && stats.TryGetValue(stat, out var value) ? value : (float?)null;

        public bool Has(string prefab) => items.ContainsKey(prefab);

        public void Set(string prefab, Stat stat, float value)
        {
            if (!items.TryGetValue(prefab, out var stats)) items[prefab] = stats = new Dictionary<Stat, float>();
            stats[stat] = Math.Max(Stats.Info(stat).Minimum, value);
        }

        public void Clear(string prefab, Stat stat)
        {
            if (!items.TryGetValue(prefab, out var stats)) return;
            stats.Remove(stat);
            if (stats.Count == 0) items.Remove(prefab);
        }

        public void ClearItem(string prefab) => items.Remove(prefab);

        public static OverrideTable Parse(string text, List<string> errors)
        {
            var table = new OverrideTable();
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int colon = line.IndexOf(':');
                string prefab = colon > 0 ? line.Substring(0, colon).Trim() : "";
                if (prefab.Length == 0 || prefab.Any(char.IsWhiteSpace))
                {
                    errors?.Add($"Satır {i + 1}: 'Eşya: anahtar=değer' biçimi bekleniyordu.");
                    continue;
                }
                foreach (var part in line.Substring(colon + 1).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var pair = part.Split('=');
                    if (pair.Length != 2 || !Stats.TryKey(pair[0].Trim(), out var stat))
                    {
                        errors?.Add($"Satır {i + 1}: bilinmeyen alan '{part.Trim()}'.");
                        continue;
                    }
                    if (!Stats.TryParseNumber(pair[1], out var value))
                    {
                        errors?.Add($"Satır {i + 1}: '{pair[1].Trim()}' sayı değil.");
                        continue;
                    }
                    table.Set(prefab, stat, value);
                }
            }
            return table;
        }

        public string Serialize()
        {
            var text = new StringBuilder();
            text.AppendLine("# FoodTuner eşya ayarları. Biçim: PrefabAdı: anahtar=değer, anahtar=değer");
            text.AppendLine("# Anahtarlar: " + string.Join(", ", Stats.All.Select(s => s.Key)));
            text.AppendLine("# Süreler saniyedir. Yazılmayan alanlar genel çarpanları kullanır.");
            foreach (var item in items.OrderBy(p => p.Key, StringComparer.Ordinal))
                text.AppendLine(item.Key + ": " + string.Join(", ",
                    item.Value.OrderBy(p => p.Key).Select(p => Stats.Info(p.Key).Key + "=" + Stats.Format(p.Value))));
            return text.ToString();
        }
    }
}
