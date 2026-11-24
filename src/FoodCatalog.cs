using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FoodTuner
{
    internal sealed class Consumable
    {
        public string Prefab, DisplayName;
        public ItemDrop.ItemData.SharedData Shared;
        public StatusEffect Effect;
        public readonly float[] Baseline = new float[Stats.All.Length];
        public readonly bool[] Available = new bool[Stats.All.Length];
        public bool IsFood => Available[(int)Stat.Health] || Available[(int)Stat.Stamina] || Available[(int)Stat.Eitr];
    }

    // Writes effective values into the item prefabs' shared data. Items in inventories normally share
    // that object; world drops and some loaded items carry a copy, which Sync() brings in line.
    internal static class FoodCatalog
    {
        // Baselines survive ObjectDB reloads: prefabs keep our edits across scenes,
        // so values must never be re-read after the first capture.
        private static readonly Dictionary<string, float[]> itemBaselines = new Dictionary<string, float[]>();
        private static readonly Dictionary<StatusEffect, float[]> effectBaselines = new Dictionary<StatusEffect, float[]>();
        // What we last wrote. If the live value differs, another mod (e.g. WackysDatabase) changed it
        // after us, and its value becomes the new original instead of being overwritten.
        private static readonly Dictionary<string, float[]> itemWritten = new Dictionary<string, float[]>();
        private static readonly Dictionary<StatusEffect, float[]> effectWritten = new Dictionary<StatusEffect, float[]>();
        private static readonly Dictionary<string, Consumable> bySharedName = new Dictionary<string, Consumable>();
        public static readonly List<Consumable> Items = new List<Consumable>();

        public static void Rebuild()
        {
            Items.Clear();
            bySharedName.Clear();
            if (!ObjectDB.instance) return;
            foreach (var prefab in ObjectDB.instance.m_items)
            {
                var drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
                var shared = drop?.m_itemData?.m_shared;
                if (shared == null || shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable) continue;
                // Judge by the original values: overriding every stat to 0 must not hide the item.
                var item = Baseline(itemBaselines, itemWritten, prefab.name, ReadItem(shared));
                bool food = item[(int)Stat.Health] > 0f || item[(int)Stat.Stamina] > 0f || item[(int)Stat.Eitr] > 0f;
                if (!food && !shared.m_consumeStatusEffect) continue;

                var entry = new Consumable { Prefab = prefab.name, Shared = shared, Effect = shared.m_consumeStatusEffect };
                entry.DisplayName = Localize(shared.m_name);
                Array.Copy(item, entry.Baseline, 5);
                for (int i = 0; i < 5; i++) entry.Available[i] = food;
                if (entry.Effect)
                {
                    var effect = Baseline(effectBaselines, effectWritten, entry.Effect, ReadEffect(entry.Effect));
                    Array.Copy(effect, 0, entry.Baseline, 5, 7);
                    entry.Available[(int)Stat.EffectDuration] = true;
                    if (entry.Effect is SE_Stats)
                        for (int i = 6; i < 12; i++) entry.Available[i] = true;
                }
                Items.Add(entry);
                bySharedName[shared.m_name] = entry;
            }
            Items.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
        }

        private static float[] Baseline<TKey>(Dictionary<TKey, float[]> baselines, Dictionary<TKey, float[]> written, TKey key, float[] live)
        {
            bool changedElsewhere = written.TryGetValue(key, out var ours) && !Same(ours, live);
            if (changedElsewhere || !baselines.TryGetValue(key, out var baseline))
            {
                baselines[key] = baseline = live;
                written.Remove(key);
            }
            return baseline;
        }

        private static bool Same(float[] a, float[] b)
        {
            for (int i = 0; i < a.Length; i++) if (!Mathf.Approximately(a[i], b[i])) return false;
            return true;
        }

        public static void Apply(OverrideTable overrides, Multipliers multipliers, bool enabled)
        {
            var player = Player.m_localPlayer;
            var activeFoods = player ? player.m_foods : null;
            var oldBurn = activeFoods?.Select(f => f.m_item?.m_shared?.m_foodBurnTime ?? 0f).ToList();

            foreach (var entry in Items)
            {
                var value = new float[entry.Baseline.Length];
                for (int i = 0; i < value.Length; i++)
                    value[i] = enabled && entry.Available[i]
                        ? FoodMath.Effective((Stat)i, entry.Baseline[i], overrides.Get(entry.Prefab, (Stat)i), multipliers)
                        : entry.Baseline[i];
                WriteItem(entry.Shared, value);
                itemWritten[entry.Prefab] = ReadItem(entry.Shared);
                if (entry.Effect)
                {
                    WriteEffect(entry.Effect, value);
                    effectWritten[entry.Effect] = ReadEffect(entry.Effect);
                }
            }

            if (player)
            {
                SyncInventory(player.GetInventory());
                for (int i = 0; i < activeFoods.Count; i++)
                {
                    var food = activeFoods[i];
                    Sync(food.m_item);
                    float now = food.m_item?.m_shared?.m_foodBurnTime ?? 0f;
                    // Keep the same fraction of the meal left, so a longer duration also extends active food.
                    if (oldBurn[i] > 0f && now > 0f && !Mathf.Approximately(oldBurn[i], now))
                        food.m_time = food.m_time * now / oldBurn[i];
                }
            }
        }

        public static void Sync(ItemDrop.ItemData item)
        {
            var shared = item?.m_shared;
            if (shared == null || !bySharedName.TryGetValue(shared.m_name, out var entry) || ReferenceEquals(shared, entry.Shared)) return;
            shared.m_food = entry.Shared.m_food;
            shared.m_foodStamina = entry.Shared.m_foodStamina;
            shared.m_foodEitr = entry.Shared.m_foodEitr;
            shared.m_foodBurnTime = entry.Shared.m_foodBurnTime;
            shared.m_foodRegen = entry.Shared.m_foodRegen;
        }

        public static void SyncInventory(Inventory inventory)
        {
            if (inventory == null || bySharedName.Count == 0) return;
            foreach (var item in inventory.GetAllItems()) Sync(item);
        }

        public static float Current(Consumable entry, Stat stat)
        {
            var values = ReadItem(entry.Shared);
            if ((int)stat < 5) return values[(int)stat];
            return entry.Effect ? ReadEffect(entry.Effect)[(int)stat - 5] : 0f;
        }

        public static void Restore()
        {
            foreach (var entry in Items)
            {
                WriteItem(entry.Shared, entry.Baseline);
                if (entry.Effect) WriteEffect(entry.Effect, entry.Baseline);
            }
            itemWritten.Clear();
            effectWritten.Clear();
        }

        private static float[] ReadItem(ItemDrop.ItemData.SharedData s) =>
            new[] { s.m_foodBurnTime, s.m_food, s.m_foodStamina, s.m_foodEitr, s.m_foodRegen };

        private static void WriteItem(ItemDrop.ItemData.SharedData s, float[] v)
        {
            s.m_foodBurnTime = v[(int)Stat.Duration];
            s.m_food = v[(int)Stat.Health];
            s.m_foodStamina = v[(int)Stat.Stamina];
            s.m_foodEitr = v[(int)Stat.Eitr];
            s.m_foodRegen = v[(int)Stat.Regen];
        }

        private static float[] ReadEffect(StatusEffect effect)
        {
            var v = new float[7];
            v[0] = effect.m_ttl;
            if (effect is SE_Stats s)
            {
                v[1] = s.m_healthRegenMultiplier; v[2] = s.m_staminaRegenMultiplier; v[3] = s.m_eitrRegenMultiplier;
                v[4] = s.m_healthOverTime; v[5] = s.m_staminaOverTime; v[6] = s.m_eitrOverTime;
            }
            return v;
        }

        private static void WriteEffect(StatusEffect effect, float[] v)
        {
            effect.m_ttl = v[(int)Stat.EffectDuration];
            if (!(effect is SE_Stats s)) return;
            s.m_healthRegenMultiplier = v[(int)Stat.HealthRegen];
            s.m_staminaRegenMultiplier = v[(int)Stat.StaminaRegen];
            s.m_eitrRegenMultiplier = v[(int)Stat.EitrRegen];
            s.m_healthOverTime = v[(int)Stat.HealthOverTime];
            s.m_staminaOverTime = v[(int)Stat.StaminaOverTime];
            s.m_eitrOverTime = v[(int)Stat.EitrOverTime];
        }

        internal static string Localize(string token)
        {
            string text;
            try { text = Localization.instance != null ? Localization.instance.Localize(token) : token; }
            catch { text = token; }
            // Database mods put <color> tags in names; list buttons do not render rich text.
            text = Regex.Replace(text ?? "", "<[^>]*>", "").Trim();
            return text.Length > 0 ? text : token;
        }
    }
}
