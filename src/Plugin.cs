using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace FoodTuner
{
    [BepInPlugin(Id, "FoodTuner", Version)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Id = "local.foodtuner";
        public const string Version = "1.0.0";
        internal static Plugin Instance;
        internal static bool IsOpen;

        private ConfigEntry<KeyCode> hotkey;
        private ConfigEntry<bool> modEnabled;
        private ConfigEntry<float> uiScale;
        private ConfigEntry<float> duration, health, stamina, eitr, regen, effectDuration, effectStrength;
        private OverrideTable overrides = new OverrideTable();
        private string overridePath;
        private Harmony harmony;

        private void Awake()
        {
            Instance = this;
            hotkey = Config.Bind("Arayüz", "Açma tuşu", KeyCode.F7, "FoodTuner panelini aç/kapat.");
            uiScale = Config.Bind("Arayüz", "Ölçek", 1f, new ConfigDescription("Panel ölçeği.", new AcceptableValueRange<float>(0.6f, 2.5f)));
            modEnabled = Config.Bind("Genel", "Etkin", true, "Kapalıyken tüm yiyecekler oyunun orijinal değerlerine döner.");
            duration = Multiplier("Yemek süresi", "Tüm yemeklerin süresi.");
            health = Multiplier("Can", "Yemeklerin verdiği can.");
            stamina = Multiplier("Dayanıklılık", "Yemeklerin verdiği dayanıklılık (stamina).");
            eitr = Multiplier("Eitr", "Yemeklerin verdiği eitr.");
            regen = Multiplier("Can yenileme", "Yemeklerin can yenileme (regen) değeri.");
            effectDuration = Multiplier("İksir etki süresi", "Mead/iksir etkilerinin süresi ve tekrar içme beklemesi.");
            effectStrength = Multiplier("İksir etki gücü", "Mead yenileme bonusları ve zamanla verilen can/dayanıklılık/eitr.");

            overridePath = Path.Combine(Paths.ConfigPath, "FoodTuner.overrides.txt");
            LoadOverrides();
            Config.SettingChanged += (_, __) => ApplyAll();
            harmony = new Harmony(Id);
            harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo("FoodTuner " + Version + " hazır. Panel: " + hotkey.Value);
        }

        private ConfigEntry<float> Multiplier(string name, string description) =>
            Config.Bind("Çarpanlar", name, 1f, new ConfigDescription(description + " 1 = orijinal.", new AcceptableValueRange<float>(0f, 100f)));

        private void OnDestroy()
        {
            ClosePanel();
            harmony?.UnpatchSelf();
            FoodCatalog.Restore();
            Instance = null;
        }

        internal Multipliers CurrentMultipliers() => new Multipliers
        {
            Duration = duration.Value, Health = health.Value, Stamina = stamina.Value, Eitr = eitr.Value, Regen = regen.Value,
            EffectDuration = effectDuration.Value, EffectStrength = effectStrength.Value
        };

        internal void ApplyAll()
        {
            try { FoodCatalog.Apply(overrides, CurrentMultipliers(), modEnabled.Value); }
            catch (Exception ex) { Logger.LogError("Değerler uygulanamadı: " + ex); }
        }

        private void LoadOverrides()
        {
            var errors = new List<string>();
            try
            {
                overrides = File.Exists(overridePath) ? OverrideTable.Parse(File.ReadAllText(overridePath), errors) : new OverrideTable();
                foreach (var error in errors) Logger.LogWarning("FoodTuner.overrides.txt " + error);
                status = $"{overrides.Count} eşya ayarı yüklendi" + (errors.Count > 0 ? $", {errors.Count} satır atlandı (log'a bak)." : ".");
            }
            catch (Exception ex)
            {
                status = "Ayar dosyası okunamadı: " + ex.Message;
                Logger.LogError(status);
            }
        }

        private void SaveOverrides()
        {
            try
            {
                string temp = overridePath + ".tmp";
                File.WriteAllText(temp, overrides.Serialize());
                if (File.Exists(overridePath)) File.Replace(temp, overridePath, null);
                else File.Move(temp, overridePath);
            }
            catch (Exception ex)
            {
                status = "Kaydedilemedi: " + ex.Message;
                Logger.LogError(status);
            }
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
        private static class ObjectDBAwake
        {
            private static void Postfix() => Refresh();
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        private static class ObjectDBCopy
        {
            private static void Postfix() => Refresh();
        }

        // Database mods add or edit items after ObjectDB.Awake (often on world join).
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class PlayerSpawned
        {
            private static void Postfix(Player __instance) { if (__instance == Player.m_localPlayer) Refresh(); }
        }

        internal static void Refresh()
        {
            if (!Instance) return;
            try { FoodCatalog.Rebuild(); }
            catch (Exception ex) { Instance.Logger.LogError("Yiyecek listesi okunamadı: " + ex); }
            Instance.ApplyAll();
        }

        // Picked-up and loaded items may carry their own copy of the shared data.
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.Changed))]
        private static class InventoryChanged
        {
            private static void Postfix(Inventory __instance) => FoodCatalog.SyncInventory(__instance);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.EatFood))]
        private static class EatFood
        {
            private static void Prefix(ItemDrop.ItemData item) => FoodCatalog.Sync(item);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.TakeInput))]
        private static class PlayerInput { private static void Postfix(ref bool __result) { if (IsOpen) __result = false; } }

        [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.TakeInput))]
        private static class ControllerInput { private static void Postfix(ref bool __result) { if (IsOpen) __result = false; } }
    }
}
