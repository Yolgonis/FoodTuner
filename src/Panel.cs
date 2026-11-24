using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FoodTuner
{
    public sealed partial class Plugin
    {
        private static readonly string[] Filters = { "Tümü", "Yemekler", "İksirler", "Ayarlananlar" };
        private Rect window = new Rect(40, 40, 980, 680);
        private Vector2 listScroll, detailScroll;
        private string search = "", status = "";
        private int filter;
        private Consumable selected;
        private readonly Dictionary<Stat, string> fields = new Dictionary<Stat, string>();
        private readonly string[] multiplierFields = new string[7];
        private GUIStyle row, rowSelected, title;
        private CursorLockMode previousLock;
        private bool previousVisible;

        private void Update()
        {
            if (Input.GetKeyDown(hotkey.Value))
            {
                if (IsOpen) ClosePanel();
                else if (Player.m_localPlayer && !Console.IsVisible() && !(Chat.instance && Chat.instance.HasFocus())) OpenPanel();
            }
            else if (IsOpen && (Input.GetKeyDown(KeyCode.Escape) || !Player.m_localPlayer)) ClosePanel();
        }

        private void LateUpdate()
        {
            if (IsOpen) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        private void OpenPanel()
        {
            Refresh();
            if (selected != null) selected = FoodCatalog.Items.Find(e => e.Prefab == selected.Prefab);
            previousLock = Cursor.lockState;
            previousVisible = Cursor.visible;
            LoadMultiplierFields();
            if (selected != null) Select(selected);
            IsOpen = true;
        }

        private void ClosePanel()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Cursor.lockState = previousLock;
            Cursor.visible = previousVisible;
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            float scale = Mathf.Clamp(uiScale.Value, 0.6f, 2.5f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            window.width = Mathf.Min(980, Screen.width / scale - 20);
            window.height = Mathf.Min(680, Screen.height / scale - 20);
            window = GUI.Window(0x46_54_00, window, DrawWindow, "FoodTuner " + Version + "  —  " + hotkey.Value + " ile kapat");
        }

        private void DrawWindow(int id)
        {
            DrawMultipliers();
            GUILayout.BeginHorizontal();
            DrawList();
            DrawDetail();
            GUILayout.EndHorizontal();
            DrawActiveFoods();
            GUILayout.Label(status);
            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }

        private void LoadMultiplierFields()
        {
            var values = new[] { duration, health, stamina, eitr, regen, effectDuration, effectStrength };
            for (int i = 0; i < values.Length; i++) multiplierFields[i] = Stats.Format(values[i].Value);
        }

        private void DrawMultipliers()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            bool on = GUILayout.Toggle(modEnabled.Value, " Etkin");
            if (on != modEnabled.Value) { modEnabled.Value = on; status = on ? "Ayarlar etkin." : "Orijinal değerler kullanılıyor."; }
            GUILayout.Label("Genel çarpanlar (1 = orijinal):");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Çarpanları uygula", GUILayout.Width(150))) ApplyMultipliers();
            if (GUILayout.Button("Çarpanları sıfırla", GUILayout.Width(140)))
            {
                for (int i = 0; i < multiplierFields.Length; i++) multiplierFields[i] = "1";
                ApplyMultipliers();
            }
            GUILayout.EndHorizontal();
            string[] labels = { "Yemek süresi", "Can", "Dayanıklılık", "Eitr", "Can yenileme", "İksir süresi", "İksir gücü" };
            GUILayout.BeginHorizontal();
            for (int i = 0; i < labels.Length; i++)
            {
                GUILayout.Label(labels[i], GUILayout.ExpandWidth(false));
                multiplierFields[i] = GUILayout.TextField(multiplierFields[i], GUILayout.Width(44));
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void ApplyMultipliers()
        {
            var entries = new[] { duration, health, stamina, eitr, regen, effectDuration, effectStrength };
            var parsed = new float[entries.Length];
            for (int i = 0; i < entries.Length; i++)
                if (!Stats.TryParseNumber(multiplierFields[i], out parsed[i]) || parsed[i] < 0f || parsed[i] > 100f)
                {
                    status = $"Geçersiz çarpan: '{multiplierFields[i]}' (0–100 arası olmalı).";
                    return;
                }
            // Each assignment raises SettingChanged; suspend so the catalog is written once.
            Config.SaveOnConfigSet = false;
            try { for (int i = 0; i < entries.Length; i++) entries[i].Value = parsed[i]; }
            finally { Config.SaveOnConfigSet = true; }
            Config.Save();
            ApplyAll();
            LoadMultiplierFields();
            if (selected != null) Select(selected);
            status = "Genel çarpanlar uygulandı.";
        }

        private IEnumerable<Consumable> Visible()
        {
            string query = search.Trim();
            return FoodCatalog.Items.Where(e =>
                (filter == 0 || (filter == 1 && e.IsFood) || (filter == 2 && !e.IsFood) || (filter == 3 && overrides.Has(e.Prefab)))
                && (query.Length == 0
                    || e.DisplayName.IndexOf(query, System.StringComparison.CurrentCultureIgnoreCase) >= 0
                    || e.Prefab.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private void DrawList()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(330));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Ara:", GUILayout.ExpandWidth(false));
            search = GUILayout.TextField(search);
            GUILayout.EndHorizontal();
            filter = GUILayout.Toolbar(filter, Filters);
            if (row == null)
            {
                row = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft };
                rowSelected = new GUIStyle(row) { normal = GUI.skin.button.active, fontStyle = FontStyle.Bold };
                title = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 16 };
            }
            listScroll = GUILayout.BeginScrollView(listScroll);
            foreach (var entry in Visible())
            {
                string mark = overrides.Has(entry.Prefab) ? "★ " : "";
                string summary = entry.IsFood
                    ? $"  {Stats.Format(entry.Shared.m_food)}/{Stats.Format(entry.Shared.m_foodStamina)}/{Stats.Format(entry.Shared.m_foodEitr)}  {Stats.Clock(entry.Shared.m_foodBurnTime)}"
                    : entry.Effect ? "  " + Stats.Clock(entry.Effect.m_ttl) : "";
                if (GUILayout.Button(mark + entry.DisplayName + summary, ReferenceEquals(entry, selected) ? rowSelected : row)) Select(entry);
            }
            GUILayout.EndScrollView();
            GUILayout.Label("Can/Dayanıklılık/Eitr  süre", GUI.skin.label);
            GUILayout.EndVertical();
        }

        private void Select(Consumable entry)
        {
            selected = entry;
            fields.Clear();
            foreach (var info in Stats.All)
            {
                var value = overrides.Get(entry.Prefab, info.Stat);
                fields[info.Stat] = value.HasValue ? Stats.Format(value.Value) : "";
            }
        }

        private void DrawDetail()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            if (selected == null)
            {
                GUILayout.Label("Soldan bir yiyecek ya da iksir seç.");
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
                return;
            }
            GUILayout.Label($"<b>{selected.DisplayName}</b>  ({selected.Prefab})", title);
            GUILayout.Label("Özel değer kutusunu boş bırakırsan genel çarpan kullanılır.");
            detailScroll = GUILayout.BeginScrollView(detailScroll);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Alan", GUILayout.Width(220));
            GUILayout.Label("Orijinal", GUILayout.Width(90));
            GUILayout.Label("Şu an", GUILayout.Width(90));
            GUILayout.Label("Özel değer", GUILayout.Width(110));
            GUILayout.EndHorizontal();
            foreach (var info in Stats.All)
            {
                if (!selected.Available[(int)info.Stat]) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label(info.Label, GUILayout.Width(220));
                GUILayout.Label(Show(info, selected.Baseline[(int)info.Stat]), GUILayout.Width(90));
                GUILayout.Label(Show(info, FoodCatalog.Current(selected, info.Stat)), GUILayout.Width(90));
                fields[info.Stat] = GUILayout.TextField(fields[info.Stat], GUILayout.Width(110));
                if (GUILayout.Button("×", GUILayout.Width(26))) fields[info.Stat] = "";
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Uygula ve kaydet")) ApplySelected();
            if (GUILayout.Button("Bu eşyayı sıfırla"))
            {
                overrides.ClearItem(selected.Prefab);
                SaveOverrides(); ApplyAll(); Select(selected);
                status = selected.DisplayName + " genel çarpanlara döndü.";
            }
            if (GUILayout.Button("Dosyadan yeniden yükle"))
            {
                LoadOverrides(); ApplyAll(); Select(selected);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private static string Show(StatInfo info, float value) =>
            info.Kind == StatKind.Seconds ? $"{Stats.Format(value)} ({Stats.Clock(value)})" : Stats.Format(value);

        private void ApplySelected()
        {
            var parsed = new Dictionary<Stat, float?>();
            foreach (var info in Stats.All)
            {
                if (!selected.Available[(int)info.Stat]) continue;
                string text = fields[info.Stat];
                if (string.IsNullOrWhiteSpace(text)) { parsed[info.Stat] = null; continue; }
                if (!Stats.TryParseNumber(text, out var value) || value < 0f)
                {
                    status = $"{info.Label}: '{text}' geçerli bir sayı değil.";
                    return;
                }
                parsed[info.Stat] = value;
            }
            foreach (var pair in parsed)
                if (pair.Value.HasValue) overrides.Set(selected.Prefab, pair.Key, pair.Value.Value);
                else overrides.Clear(selected.Prefab, pair.Key);
            SaveOverrides();
            ApplyAll();
            Select(selected);
            status = selected.DisplayName + " güncellendi ve kaydedildi.";
        }

        private void DrawActiveFoods()
        {
            var player = Player.m_localPlayer;
            if (!player) return;
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label("Aktif:", GUILayout.ExpandWidth(false));
            if (player.m_foods.Count == 0) GUILayout.Label("yemek yok");
            foreach (var food in player.m_foods)
            {
                string name = food.m_item != null ? FoodCatalog.Localize(food.m_item.m_shared.m_name) : food.m_name;
                GUILayout.Label($"{name}: {Stats.Clock(food.m_time)} kaldı, {Stats.Format(Mathf.Round(food.m_health))}/{Stats.Format(Mathf.Round(food.m_stamina))}/{Stats.Format(Mathf.Round(food.m_eitr))}");
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }
}
