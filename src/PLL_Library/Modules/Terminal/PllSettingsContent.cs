using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zorro.Core;
using Zorro.Settings;
using Zorro.Settings.DebugUI;

namespace PLL.Library
{
    internal static class PllMenu
    {
        private static int _warned;

        internal static void Show(Component page, int index)
        {
            try
            {
                if (page == null) return;
                var fill = page.GetComponent<PllMenuFill>() ?? page.gameObject.AddComponent<PllMenuFill>();
                fill.Show(index);
            }
            catch (Exception e) { Warn("couldn't fill the tab: " + e); }
        }

        internal static void Warn(string s)
        {
            if (_warned >= 8) return;
            _warned++;
            Plugin.PLLLog.LogWarning("Settings page: " + s);
        }
    }

    internal static class PllSettingsData
    {
        public const string Guid = "af.pll.library";
        public const string LogSource = "AF.PLL.Library";

        public static readonly string[] NativeStops = { "Beach", "Tropics", "Roots", "Alpine", "Mesa", "Volcano", "Citadel", "Nadir" };

        public const string DefaultLayout = "Beach > Tropics > Roots > Alpine > Mesa > Volcano > Citadel > Nadir";

        public sealed class Custom
        {
            public string Id;
            public readonly List<string> SegmentIds = new List<string>();

            public bool Matches(string token) =>
                string.Equals(Id, token, StringComparison.OrdinalIgnoreCase) ||
                SegmentIds.Any(x => string.Equals(x, token, StringComparison.OrdinalIgnoreCase));
            public string BiomeId;
            public string Name;
            public ConfigFile File;
        }

        public static ConfigFile Config => StaticField("PLL.Library.FileManager", "Config") as ConfigFile;

        public static ConfigEntryBase Entry(ConfigFile cfg, string section, string key)
        {
            if (cfg == null) return null;
            try
            {
                foreach (ConfigDefinition d in cfg.Keys)
                    if (d.Section == section && d.Key == key) return cfg[d];
            }
            catch { }
            return null;
        }

        public static List<ConfigEntryBase> Section(ConfigFile cfg, string section)
        {
            var list = new List<ConfigEntryBase>();
            if (cfg == null) return list;
            try
            {
                foreach (ConfigDefinition d in cfg.Keys.ToList())
                    if (d.Section == section) list.Add(cfg[d]);
            }
            catch { }
            return list;
        }

        public static List<string> Sections(ConfigFile cfg)
        {
            var list = new List<string>();
            if (cfg == null) return list;
            try { foreach (ConfigDefinition d in cfg.Keys.ToList()) if (!list.Contains(d.Section)) list.Add(d.Section); }
            catch { }
            return list;
        }

        public static List<Custom> CustomBiomes()
        {
            var result = new List<Custom>();
            var names = new Dictionary<string, Custom>(StringComparer.Ordinal);
            try
            {
                if (StaticField("PLL.Library.CustomBiomeConfigRegistry", "Configurations") is IDictionary configs)
                    foreach (object c in configs.Values)
                    {
                        object id = Prop(c, "BiomeId");
                        string idv = Prop(id, "Value") as string;
                        if (string.IsNullOrEmpty(idv)) continue;
                        var file = Prop(c, "File") as ConfigFile;
                        string name = Entry(file, "Biome", "Name")?.BoxedValue as string;
                        names[idv] = new Custom { BiomeId = idv, Name = string.IsNullOrEmpty(name) ? idv : name, File = file };
                    }
            }
            catch { }

            var prefixes = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                if (StaticField("PLL.Library.SegmentRegistry", "PendingGroups") is IDictionary groups)
                    foreach (object g in groups.Values)
                        if (Prop(g, "Prefixes") is IEnumerable ps)
                            foreach (object s in ps) { string sid = Prop(s, "Id") as string; if (sid != null) prefixes.Add(sid); }
            }
            catch { }

            try
            {
                if (StaticField("PLL.Library.SegmentRegistry", "Pending") is IDictionary segs)
                    foreach (object s in segs.Values)
                    {
                        string sid = Prop(s, "Id") as string;
                        if (string.IsNullOrEmpty(sid) || prefixes.Contains(sid)) continue;
                        string bid = Prop(Prop(s, "Biome"), "Id") as string ?? sid;
                        names.TryGetValue(bid, out Custom info);
                        Custom have = result.FirstOrDefault(r => r.BiomeId == bid);
                        if (have == null) result.Add(have = new Custom { Id = bid, BiomeId = bid, Name = info?.Name ?? bid, File = info?.File });
                        have.SegmentIds.Add(sid);
                    }
            }
            catch { }

            foreach (Custom c in names.Values)
                if (!result.Any(r => r.BiomeId == c.BiomeId) && !prefixes.Contains(c.BiomeId))
                    result.Add(new Custom { Id = c.BiomeId, BiomeId = c.BiomeId, Name = c.Name, File = c.File });

            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public static List<Custom> CustomConfigs()
        {
            var list = new List<Custom>();
            try
            {
                if (StaticField("PLL.Library.CustomBiomeConfigRegistry", "Configurations") is IDictionary configs)
                    foreach (object c in configs.Values)
                    {
                        string idv = Prop(Prop(c, "BiomeId"), "Value") as string;
                        var file = Prop(c, "File") as ConfigFile;
                        if (string.IsNullOrEmpty(idv) || file == null) continue;
                        string name = Entry(file, "Biome", "Name")?.BoxedValue as string;
                        list.Add(new Custom { Id = idv, BiomeId = idv, Name = string.IsNullOrEmpty(name) ? idv : name, File = file });
                    }
            }
            catch { }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public static List<string> BundleFiles()
        {
            var list = new List<string>();
            try
            {
                if (StaticField("PLL.Library.BiomeRegistration", "RetainedFiles") is IEnumerable files)
                    foreach (object f in files)
                    {
                        string p = Prop(f, "Path") as string;
                        if (!string.IsNullOrEmpty(p)) list.Add(p);
                    }
            }
            catch { }
            return list;
        }

        public static string Version() => MyPluginInfo.PLUGIN_VERSION;

        public static List<string> Stops(string layout) =>
            (layout ?? "").Split('>').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        public static List<string> LoggedIssues(int max)
        {
            var found = new List<string>();
            try
            {
                string path = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");
                if (!File.Exists(path)) return found;
                string text;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs)) text = r.ReadToEnd();
                foreach (string raw in text.Split('\n'))
                {
                    string line = raw.TrimEnd('\r');
                    int tag = line.IndexOf(":" + LogSource + "]", StringComparison.Ordinal);
                    if (tag < 0 || !line.StartsWith("[")) continue;
                    string level = line.Substring(1, tag - 1).Trim();
                    bool bad = level == "Error" || level == "Warning" || level == "Fatal";
                    string msg = line.Substring(tag + LogSource.Length + 2).Trim();
                    if (!bad && msg.Contains("bundle-result") && !msg.Contains("outcome=accepted")) bad = true;
                    if (!bad || msg.Length == 0) continue;
                    string entry = (level == "Warning" ? "Warning: " : level == "Info" || level == "Message" ? "" : "Error: ") + msg;
                    found.Remove(entry);
                    found.Add(entry);
                }
            }
            catch (Exception e) { found.Add("(Couldn't read the log: " + e.Message + ")"); }
            if (found.Count > max) found.RemoveRange(0, found.Count - max);
            return found;
        }


        private static object StaticField(string type, string field)
        {
            try
            {
                Type t = AccessTools.TypeByName(type);
                if (t == null) return null;
                FieldInfo f = AccessTools.Field(t, field);
                if (f != null) return f.GetValue(null);
                PropertyInfo p = AccessTools.Property(t, field);
                return p?.GetValue(null);
            }
            catch { return null; }
        }

        public static object Prop(object o, string name)
        {
            if (o == null) return null;
            try
            {
                PropertyInfo p = AccessTools.Property(o.GetType(), name);
                if (p != null) return p.GetValue(o);
                FieldInfo f = AccessTools.Field(o.GetType(), name);
                return f?.GetValue(o);
            }
            catch { return null; }
        }
    }

    internal interface IKeyed { string Key { get; set; } }

    internal sealed class ChoiceRow : Setting, IEnumSetting, IExposedSetting, IKeyed
    {
        private readonly List<string> _choices;
        private readonly Func<int> _get;
        private readonly Action<int> _set;
        public string Key { get; set; }

        public ChoiceRow(List<string> choices, Func<int> get, Action<int> set) { _choices = choices; _get = get; _set = set; }

        public List<string> GetUnlocalizedChoices() => _choices;
        public int GetValue() { try { return Mathf.Clamp(_get(), 0, Math.Max(0, _choices.Count - 1)); } catch { return 0; } }
        public void SetValue(int v, ISettingHandler settingHandler, bool fromUI) { try { _set(v); } catch (Exception e) { Plugin.PLLLog.LogWarning("Settings page: " + e.Message); } }

        public string GetDisplayName() => Key;
        public string GetCategory() => "PLL";
        public override void Load(ISettingsSaveLoad loader) { }
        public override void Save(ISettingsSaveLoad saver) { }
        public override void ApplyValue() { }
        public override SettingUI GetDebugUI(ISettingHandler settingHandler) => null;
        public override GameObject GetSettingUICell() => SingletonAsset<InputCellMapper>.Instance.EnumSettingCell;
    }

    internal sealed class RangeRow : FloatSetting, IExposedSetting, IKeyed
    {
        private readonly Action<float> _set;
        private readonly float _default;
        public string Key { get; set; }

        public RangeRow(float min, float max, float value, float def, Action<float> set)
        {
            MinValue = min; MaxValue = max; Value = Mathf.Clamp(value, min, max); _default = def; _set = set;
        }

        public override void ApplyValue() { try { _set(Value); } catch (Exception e) { Plugin.PLLLog.LogWarning("Settings page: " + e.Message); } }
        protected override float GetDefaultValue() => _default;
        protected override Unity.Mathematics.float2 GetMinMaxValue() => new Unity.Mathematics.float2(MinValue, MaxValue);
        public override void Load(ISettingsSaveLoad loader) { }
        public override void Save(ISettingsSaveLoad saver) { }
        public string GetDisplayName() => Key;
        public string GetCategory() => "PLL";
    }

    internal sealed class TextRow : Setting, IExposedSetting, IKeyed
    {
        public readonly Func<string> Get;
        public readonly Action<string> Set;
        public string Key { get; set; }

        public TextRow(Func<string> get, Action<string> set) { Get = get; Set = set; }

        public string GetDisplayName() => Key;
        public string GetCategory() => "PLL";
        public override void Load(ISettingsSaveLoad loader) { }
        public override void Save(ISettingsSaveLoad saver) { }
        public override void ApplyValue() { }
        public override SettingUI GetDebugUI(ISettingHandler settingHandler) => null;
        public override GameObject GetSettingUICell()
        {
            InputCellMapper m = SingletonAsset<InputCellMapper>.Instance;
            if (m.StringSettingCell != null) return m.StringSettingCell;
            if (m.IntSettingCell != null) return m.IntSettingCell;
            return m.FloatSettingCell;
        }
    }

    internal sealed class ButtonRow : ButtonSetting, IExposedSetting, IKeyed
    {
        private readonly string _text;
        private readonly Action _click;
        public string Key { get; set; }

        public ButtonRow(string text, Action click) { _text = text; _click = click; }

        public override void OnClicked(ISettingHandler settingHandler) { try { _click(); } catch (Exception e) { Plugin.PLLLog.LogWarning("Settings page: " + e.Message); } }
        public override string GetButtonText() => _text;
        public void Click() => OnClicked(null);

        public override GameObject GetSettingUICell()
        {
            InputCellMapper m = SingletonAsset<InputCellMapper>.Instance;
            return m.ButtonSettingCell != null ? m.ButtonSettingCell : m.EnumSettingCell;
        }
        public string GetDisplayName() => Key;
        public string GetCategory() => "PLL";
    }

    internal sealed class PllMenuFill : MonoBehaviour
    {
        private int _tab;
        private bool _built, _failed, _dirty, _advanced;
        private Transform _list;
        private ScrollRect _scroll;
        private GameObject _cellPrefab;
        private TMP_FontAsset _font;
        private Material _fontMat;
        private Color _textColor = Color.white;
        private float _fontSize = 28f, _rowW = 1000f;
        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly List<SettingsUICell> _cells = new List<SettingsUICell>();
        private Coroutine _fade;
        private Button _button;
        private static int _keyCounter;

        private static readonly Color Muted = new Color(0.72f, 0.7f, 0.66f, 1f);
        private static readonly Color Accent = new Color(1f, 0.62f, 0.8f, 1f);   // PLL's pink
        private static readonly Color Good = new Color(0.62f, 0.9f, 0.6f, 1f);
        private static readonly Color Bad = new Color(1f, 0.55f, 0.45f, 1f);

        public void Show(int tab)
        {
            _tab = tab;
            _advanced = false;
            if (isActiveAndEnabled) Rebuild(true);
        }

        private void OnEnable() => Rebuild(true);

        private void LateUpdate()
        {
            if (!_dirty) return;
            _dirty = false;
            Rebuild(false);
        }

        private void Refresh() => _dirty = true;


        private void Rebuild(bool tabChange)
        {
            if (_failed) return;
            try
            {
                if (!EnsureUi()) { _failed = true; return; }
                float keep = _scroll != null ? _scroll.verticalNormalizedPosition : 1f;
                Clear();
                ConfigFile cfg = PllSettingsData.Config;
                try
                {
                    if (cfg == null)
                        Note("PLL Library's settings aren't loaded yet. Open this page again in a moment.", Bad);
                    else if (_tab == 0) BuildMap(cfg);
                    else if (_tab == 1) BuildConfig(cfg);
                    else BuildFaq(cfg);
                }
                finally
                {
                    if (_fade != null) StopCoroutine(_fade);
                    _fade = StartCoroutine(FadeIn(tabChange, tabChange ? 1f : keep));
                }
            }
            catch (Exception e) { PllMenu.Warn("building the tab failed: " + e); }
        }

        private IEnumerator FadeIn(bool sounds, float scrollTo)
        {
            yield return null;
            if (_scroll != null) _scroll.verticalNormalizedPosition = scrollTo;
            for (int i = 0; i < _cells.Count; i++)
            {
                SettingsUICell c = _cells[i];
                if (c == null) continue;
                if (sounds && i < 10)
                {
                    c.FadeIn();
                    yield return new WaitForSecondsRealtime(0.04f);
                }
                else
                {
                    var g = c.GetComponent<CanvasGroup>();
                    if (g != null) g.alpha = 1f;
                }
            }
            _fade = null;
        }

        private void Clear()
        {
            foreach (GameObject g in _rows)
                if (g != null) { g.SetActive(false); Destroy(g); }
            _rows.Clear();
            _cells.Clear();
        }

        private bool EnsureUi()
        {
            if (_built) return _list != null;
            _built = true;

            SharedSettingsMenu native = FindNative();
            if (native == null || native.m_settingsCellPrefab == null || native.m_settingsContentParent == null)
            {
                PllMenu.Warn("PEAK's settings page wasn't found next to PLL's page; the tabs stay empty.");
                return false;
            }
            Transform nContent = native.transform.Find("Content");
            Transform top = native.m_settingsContentParent;
            while (top != null && top.parent != nContent) top = top.parent;
            Transform tabs = FindDeep(transform, "TABS");
            if (top == null || tabs == null)
            {
                PllMenu.Warn("PEAK's settings page layout has changed; the tabs stay empty.");
                return false;
            }

            string rel = PathFrom(top, native.m_settingsContentParent);
            GameObject clone = Instantiate(top.gameObject, tabs.parent, false);
            clone.name = "PLL Settings Content";
            clone.SetActive(true);
            clone.transform.SetSiblingIndex(Mathf.Clamp(top.GetSiblingIndex(), 0, tabs.parent.childCount - 1));
            _list = rel.Length == 0 ? clone.transform : clone.transform.Find(rel);
            if (_list == null) { Destroy(clone); PllMenu.Warn("couldn't find the rows' parent in the copied page."); return false; }
            for (int i = _list.childCount - 1; i >= 0; i--) DestroyImmediate(_list.GetChild(i).gameObject);
            _scroll = clone.GetComponentInChildren<ScrollRect>(true);
            if (_scroll == null) MakeScrollable((RectTransform)_list);
            _cellPrefab = native.m_settingsCellPrefab;

            SettingsUICell pc = _cellPrefab.GetComponent<SettingsUICell>();
            if (pc != null && pc.m_text != null)
            {
                _font = pc.m_text.font;
                _fontMat = pc.m_text.fontSharedMaterial;
                _textColor = pc.m_text.color;
                _fontSize = pc.m_text.fontSize > 1f ? pc.m_text.fontSize : _fontSize;
            }

            Canvas.ForceUpdateCanvases();
            float prefabW = ((RectTransform)_cellPrefab.transform).sizeDelta.x;
            float listW = ((RectTransform)_list).rect.width;
            _rowW = listW > 100f ? listW : prefabW > 100f ? prefabW : 1000f;
            Plugin.PLLLog.LogInfo($"Settings page: filled (row width {_rowW:0}, rows under '{rel}').");
            return true;
        }

        private void MakeScrollable(RectTransform view)
        {
            var old = view.GetComponent<VerticalLayoutGroup>();
            var rows = new GameObject("Rows", typeof(RectTransform));
            rows.layer = view.gameObject.layer;
            var rr = (RectTransform)rows.transform;
            rr.SetParent(view, false);
            rr.anchorMin = new Vector2(0f, 1f); rr.anchorMax = new Vector2(1f, 1f); rr.pivot = new Vector2(0.5f, 1f);
            rr.offsetMin = new Vector2(0f, 0f); rr.offsetMax = new Vector2(-22f, 0f);
            rr.anchoredPosition = Vector2.zero;
            var vlg = rows.AddComponent<VerticalLayoutGroup>();
            if (old != null)
            {
                vlg.padding = old.padding; vlg.spacing = old.spacing; vlg.childAlignment = old.childAlignment;
                vlg.childControlWidth = old.childControlWidth; vlg.childControlHeight = old.childControlHeight;
                vlg.childForceExpandWidth = old.childForceExpandWidth; vlg.childForceExpandHeight = false;
                vlg.childScaleWidth = old.childScaleWidth; vlg.childScaleHeight = old.childScaleHeight;
                DestroyImmediate(old);
            }
            else { vlg.spacing = 8f; vlg.childControlHeight = true; vlg.childControlWidth = true; vlg.childForceExpandHeight = false; }
            var fit = rows.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            view.gameObject.AddComponent<RectMask2D>();
            var hit = view.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
            _scroll = view.gameObject.AddComponent<ScrollRect>();
            _scroll.content = rr;
            _scroll.viewport = view;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 70f;
            _scroll.inertia = true;
            _scroll.decelerationRate = 0.12f;

            var bar = new GameObject("Scrollbar", typeof(RectTransform));
            bar.layer = view.gameObject.layer;
            var br = (RectTransform)bar.transform;
            br.SetParent(view, false);
            br.anchorMin = new Vector2(1f, 0f); br.anchorMax = new Vector2(1f, 1f); br.pivot = new Vector2(1f, 0.5f);
            br.sizeDelta = new Vector2(8f, -12f); br.anchoredPosition = Vector2.zero;
            var bg = bar.AddComponent<Image>(); bg.color = new Color(1f, 1f, 1f, 0.10f);
            var area = new GameObject("Sliding Area", typeof(RectTransform));
            area.layer = bar.layer;
            var ar = (RectTransform)area.transform;
            ar.SetParent(br, false); ar.anchorMin = Vector2.zero; ar.anchorMax = Vector2.one; ar.offsetMin = ar.offsetMax = Vector2.zero;
            var handle = new GameObject("Handle", typeof(RectTransform));
            handle.layer = bar.layer;
            var hr = (RectTransform)handle.transform;
            hr.SetParent(ar, false); hr.offsetMin = hr.offsetMax = Vector2.zero;
            var hi = handle.AddComponent<Image>(); hi.color = new Color(Accent.r, Accent.g, Accent.b, 0.85f);
            var sb = bar.AddComponent<Scrollbar>();
            sb.handleRect = hr; sb.targetGraphic = hi; sb.direction = Scrollbar.Direction.BottomToTop;
            var nav = sb.navigation; nav.mode = Navigation.Mode.None; sb.navigation = nav;
            _scroll.verticalScrollbar = sb;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            _list = rr;
        }

        private GameObject _lastSelected;

        private void Update()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            GameObject sel = es != null ? es.currentSelectedGameObject : null;
            if (sel == _lastSelected || _scroll == null || _list == null) return;
            _lastSelected = sel;
            if (sel == null || !sel.transform.IsChildOf(_list)) return;
            Transform row = sel.transform;
            while (row.parent != null && row.parent != _list) row = row.parent;
            var view = _scroll.viewport;
            var corners = new Vector3[4];
            ((RectTransform)row).GetWorldCorners(corners);
            float bottom = view.InverseTransformPoint(corners[0]).y, top = view.InverseTransformPoint(corners[1]).y;
            Rect vr = view.rect;
            Vector2 pos = _scroll.content.anchoredPosition;
            if (bottom < vr.yMin + 20f) pos.y += vr.yMin + 20f - bottom;
            else if (top > vr.yMax - 20f) pos.y -= top - (vr.yMax - 20f);
            _scroll.content.anchoredPosition = pos;
        }

        private SharedSettingsMenu FindNative()
        {
            Transform parent = transform.parent;
            if (parent == null) return null;
            int idx = transform.GetSiblingIndex();
            if (idx > 0)
            {
                var m = parent.GetChild(idx - 1).GetComponentInChildren<SharedSettingsMenu>(true);
                if (m != null) return m;
            }
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform c = parent.GetChild(i);
                if (c == transform) continue;
                var m = c.GetComponentInChildren<SharedSettingsMenu>(true);
                if (m != null) return m;
            }
            return null;
        }


        private void BuildMap(ConfigFile cfg)
        {
            Note("Changes save to PLL's config straight away and are used from the next expedition. In a lobby, the host's settings build the map.", Muted);

            List<PllSettingsData.Custom> customs = PllSettingsData.CustomBiomes();
            var tokens = new List<string>(PllSettingsData.NativeStops);
            var labels = new List<string>(PllSettingsData.NativeStops);
            foreach (PllSettingsData.Custom c in customs) { tokens.Add(c.Id); labels.Add(c.Name + "  <size=70%><color=#FF9ECD>custom</color></size>"); }

            Header("Route");
            ConfigEntryBase order = PllSettingsData.Entry(cfg, "Map", "LayoutOrder");
            ConfigEntryBase doOrder = PllSettingsData.Entry(cfg, "Map", "DoOrder");
            if (doOrder != null) Row(doOrder, "Use this route", "Off lets PLL pick the order (Generative Map) or use PEAK's own map.", true);

            if (order != null)
            {
                List<string> stops = PllSettingsData.Stops(order.BoxedValue as string);
                for (int i = 0; i < stops.Count; i++)
                {
                    int at = i;
                    string tok = stops[i];
                    var stopTokens = new List<string>(tokens);
                    var stopLabels = new List<string>(labels);
                    int cur = stopTokens.FindIndex(t => string.Equals(t, tok, StringComparison.OrdinalIgnoreCase));
                    if (cur < 0)
                    {
                        int ci = customs.FindIndex(c => c.Matches(tok));
                        if (ci >= 0) { cur = PllSettingsData.NativeStops.Length + ci; stopTokens[cur] = tok; } // keep the id the route already uses
                    }
                    if (cur < 0)
                    {
                        stopTokens.Add(tok);
                        stopLabels.Add(tok + "  <size=70%><color=#FF8C73>not loaded</color></size>");
                        cur = stopTokens.Count - 1;
                    }
                    stopTokens.Add(null);
                    stopLabels.Add("<color=#FF8C73>Remove this stop</color>");
                    Add(new ChoiceRow(stopLabels, () => cur, v =>
                    {
                        List<string> now = PllSettingsData.Stops(order.BoxedValue as string);
                        if (at >= now.Count) return;
                        if (stopTokens[v] == null) now.RemoveAt(at); else now[at] = stopTokens[v];
                        order.BoxedValue = string.Join(" > ", now);
                        Refresh();
                    }), $"Stop {i + 1}");
                }
                if (stops.Count == 0) Note("The route is empty, so PLL will use PEAK's default order.", Bad);

                Add(new ButtonRow("Add", () =>
                {
                    List<string> now = PllSettingsData.Stops(order.BoxedValue as string);
                    string pick = tokens.FirstOrDefault(t => !now.Contains(t, StringComparer.OrdinalIgnoreCase) && !customs.Any(c => c.Id == t && now.Any(c.Matches))) ?? "Beach";
                    int at = now.Count > 0 && string.Equals(now[now.Count - 1], "Nadir", StringComparison.OrdinalIgnoreCase) ? now.Count - 1 : now.Count;
                    now.Insert(at, pick);
                    order.BoxedValue = string.Join(" > ", now);
                    Refresh();
                }), "Add a stop");
                Add(new ButtonRow("Reset", () => { order.BoxedValue = order.DefaultValue as string ?? PllSettingsData.DefaultLayout; Refresh(); }), "Reset to PEAK's route");

                var shown = stops.Select(s =>
                {
                    PllSettingsData.Custom c = customs.FirstOrDefault(x => x.Matches(s));
                    return c != null ? c.Name : s;
                });
                Note("Route:  " + string.Join("  >  ", shown) +
                     "\nVolcano brings Caldera below it and Citadel brings Gloom. The Peak comes with Volcano or Citadel, or with \"Peak at end\" (PLL Configuration tab).", Muted);
            }

            Header("Biomes available");
            var sb = new StringBuilder();
            sb.Append("<color=#FFE6BF>PEAK's own:</color>  ").Append(string.Join(",  ", PllSettingsData.NativeStops));
            sb.Append("\n<color=#FFE6BF>Custom (from biome bundles):</color>  ");
            if (customs.Count == 0) sb.Append("<color=#C8C3B8>none loaded</color>");
            else sb.Append(string.Join(",  ", customs.Select(c => c.Name + " <color=#C8C3B8><size=80%>(" + c.Id + ")</size></color>")));
            Note(sb.ToString(), _textColor);

            Header("Map");
            var placed = new HashSet<string> { "LayoutOrder", "DoOrder", "InfiniteMap" };
            foreach (ConfigEntryBase e in PllSettingsData.Section(cfg, "Map"))
                if (!placed.Contains(e.Definition.Key)) Row(e, null, null, true);

            Header("Infinite map");
            ConfigEntryBase inf = PllSettingsData.Entry(cfg, "Map", "InfiniteMap");
            if (inf != null) Row(inf, "Infinite map", null, true, refresh: true);
            if (inf == null || inf.BoxedValue is bool on && on)
                foreach (ConfigEntryBase e in PllSettingsData.Section(cfg, "Fields"))
                    Row(e, e.Definition.Key.Replace("InfiniteMap: ", ""), null, true);

            List<ConfigEntryBase> vars = PllSettingsData.Section(cfg, "Variations");
            if (vars.Count > 0)
            {
                Header("Biome variations");
                foreach (ConfigEntryBase e in vars)
                {
                    string key = e.Definition.Key;
                    if (key.EndsWith(" Pattern")) continue;
                    if ((ListValues(e.Description?.AcceptableValues) ?? FileChoices(e)) is string[] l && l.Length <= 2)
                        continue; // only Random / Pattern - nothing to choose
                    Row(e, key, null, false, refresh: true);
                    if (e.BoxedValue as string == "Pattern")
                    {
                        ConfigEntryBase pat = PllSettingsData.Entry(cfg, "Variations", key + " Pattern");
                        if (pat != null) Row(pat, key + " pattern", null, true);
                    }
                }
                Note("Random lets PEAK pick each time. Biomes with only one look aren't listed.", Muted);
            }

            foreach (PllSettingsData.Custom c in PllSettingsData.CustomConfigs())
            {
                List<ConfigEntryBase> gen = PllSettingsData.Section(c.File, "Generative Map");
                if (gen.Count == 0) continue;
                Header(c.Name + "  <size=70%><color=#FF9ECD>custom biome</color></size>");
                foreach (ConfigEntryBase e in gen) Row(e, null, null, true);
            }
        }


        private static readonly (string section, string title)[] ConfigSections =
        {
            ("Behaviors", "Run rules"),
            ("Prerequisites", "Start of the run"),
            ("Optimizations", "Performance"),
            ("PLL", "PLL Library"),
        };

        private static readonly HashSet<string> ElsewhereSections = new HashSet<string>
            { "Version", "Map", "Fields", "Variations", "Fog", "Boundaries", "Behaviors", "Prerequisites", "Optimizations", "PLL" };

        private void BuildConfig(ConfigFile cfg)
        {
            Note("Changes save to PLL's config straight away and are used from the next expedition.", Muted);
            foreach (var (section, title) in ConfigSections)
            {
                List<ConfigEntryBase> list = PllSettingsData.Section(cfg, section);
                if (list.Count == 0) continue;
                Header(title);
                foreach (ConfigEntryBase e in list) Row(e, null, null, true);
            }
            foreach (string s in PllSettingsData.Sections(cfg))
            {
                if (ElsewhereSections.Contains(s)) continue;
                Header(s);
                foreach (ConfigEntryBase e in PllSettingsData.Section(cfg, s)) Row(e, null, null, true);
            }

            Header("Advanced");
            Add(new ChoiceRow(new List<string> { "Hidden", "Shown" }, () => _advanced ? 1 : 0, v => { _advanced = v == 1; Refresh(); }),
                "Fog cutouts and wall boxes");
            if (_advanced)
            {
                Note("Numbers for biome makers: leave a box empty to keep the biome as it was built.", Muted);
                foreach (string s in new[] { "Fog", "Boundaries" })
                    foreach (ConfigEntryBase e in PllSettingsData.Section(cfg, s)) Row(e, null, null, false);
                foreach (PllSettingsData.Custom c in PllSettingsData.CustomConfigs())
                    foreach (ConfigEntryBase e in PllSettingsData.Section(c.File, "Spatial")) Row(e, c.Name + ": " + e.Definition.Key, null, false);
            }

            Header("Config file");
            Add(new ButtonRow("Reload", () =>
            {
                cfg.Reload();
                _fileChoices.Clear();
                foreach (PllSettingsData.Custom c in PllSettingsData.CustomConfigs()) try { c.File.Reload(); } catch { }
                Refresh();
            }), "Reload after editing it by hand");
            Add(new ButtonRow("Open", () => OpenFolder(Path.GetDirectoryName(cfg.ConfigFilePath))), "Open the config folder");
        }


        private void BuildFaq(ConfigFile cfg)
        {
            Header("What's loaded");
            List<PllSettingsData.Custom> customs = PllSettingsData.CustomBiomes();
            List<string> bundles = PllSettingsData.BundleFiles();
            var sb = new StringBuilder();
            string v = PllSettingsData.Version();
            sb.Append("<color=#FFE6BF>PLL Library</color> ").Append(v ?? "?");
            sb.Append("\n<color=#FFE6BF>Custom biomes:</color> ").Append(customs.Count == 0 ? "none" : string.Join(", ", customs.Select(c => c.Name)));
            sb.Append("\n<color=#FFE6BF>Biome bundles:</color> ").Append(bundles.Count == 0 ? "none" : string.Join(", ", bundles.Select(Path.GetFileName)));
            ConfigEntryBase order = PllSettingsData.Entry(cfg, "Map", "LayoutOrder");
            ConfigEntryBase doOrder = PllSettingsData.Entry(cfg, "Map", "DoOrder");
            ConfigEntryBase gen = PllSettingsData.Entry(cfg, "Map", "Generative Map");
            ConfigEntryBase inf = PllSettingsData.Entry(cfg, "Map", "InfiniteMap");
            string mode = inf?.BoxedValue is bool i && i ? "infinite map" :
                          doOrder?.BoxedValue is bool d && d ? "your route" :
                          gen?.BoxedValue is bool g && g ? "generative (PLL picks the order)" : "PEAK's own map";
            sb.Append("\n<color=#FFE6BF>Next expedition uses:</color> ").Append(mode);
            if (order != null && doOrder?.BoxedValue is bool d2 && d2)
            {
                List<string> stops = PllSettingsData.Stops(order.BoxedValue as string);
                var known = new HashSet<string>(PllSettingsData.NativeStops, StringComparer.OrdinalIgnoreCase);
                List<string> missing = stops.Where(s => !known.Contains(s) && !customs.Any(c => c.Matches(s))).ToList();
                if (missing.Count > 0)
                    sb.Append("\n<color=#FF8C73>The route names biomes that aren't loaded: ").Append(string.Join(", ", missing)).Append("</color>");
            }
            Note(sb.ToString(), _textColor);

            Header("Problems PLL reported this session");
            List<string> issues = PllSettingsData.LoggedIssues(12);
            if (issues.Count == 0) Note("None - PLL hasn't logged any warnings or errors.", Good);
            else foreach (string s in issues) Note(Trim(s, 400), s.StartsWith("Warning") ? new Color(1f, 0.82f, 0.5f) : Bad);
            Add(new ButtonRow("Refresh", Refresh), "Check again");
            Add(new ButtonRow("Open", () => OpenFolder(Paths.BepInExRootPath)), "Open the log folder (LogOutput.log)");

            Header("FAQ");
            Faq("How do I change the biomes in a run?",
                "Map tab: turn on \"Use this route\", then set each stop. Add a stop or remove one with the last choice on its drop-down. It's used from the next expedition you start.");
            Faq("My custom biome isn't on the list.",
                "Its bundle didn't load. Check that it's in BepInEx\\plugins and that it was made for this PLL version, then look under \"Problems PLL reported\" above.");
            Faq("Friends can't join (\"The host has no compatible PLL Library version\").",
                "Everyone in the lobby needs the same PLL Library version and the same biome bundles. The host's settings decide the map, so guests don't need to match the route.");
            Faq("I changed a setting and nothing happened.",
                "Map settings are read when an expedition starts, so changes made mid-run show up on the next one. If you edited the file by hand while the game was open, use Reload on the PLL Configuration tab.");
            Faq("Where are Caldera, Gloom and the Peak?",
                "Caldera is always the lower part of Volcano and Gloom the lower part of Citadel, so they can't be picked alone. The Peak is the ending: it comes with Volcano or Citadel, or add it with \"Peak at end\".");
            Faq("Loading takes long or stutters.",
                "Keep \"Lazy Generation\" and \"Remove Old\" on (Performance). \"Display Progress\" (PLL Library) shows loading progress. Lower \"Light Map Minimum\" uses less memory.");
            Faq("Where's the config file?",
                "BepInEx\\config\\AF\\PLL_Library\\config.cfg, with one file per custom biome under \"Custom Segments\". Use \"Open the config folder\" on the PLL Configuration tab.");
        }

        private void Faq(string q, string a) => Note("<color=#FFE6BF>" + q + "</color>\n<color=#C8C3B8>" + a + "</color>", _textColor);

        private void Row(ConfigEntryBase e, string label, string desc, bool describe, bool refresh = false)
        {
            if (e == null) return;
            label = label ?? e.Definition.Key;
            if (describe && desc == null) desc = e.Description?.Description;
            Type t = e.SettingType;
            Setting s;
            AcceptableValueBase acc = e.Description?.AcceptableValues;
            Action after = refresh ? (Action)Refresh : null;

            if (t == typeof(bool))
            {
                s = new ChoiceRow(new List<string> { "Off", "On" }, () => (bool)e.BoxedValue ? 1 : 0,
                    v => { e.BoxedValue = v == 1; after?.Invoke(); });
            }
            else if (t.IsEnum)
            {
                string[] names = Enum.GetNames(t);
                s = new ChoiceRow(names.ToList(), () => Array.IndexOf(names, e.BoxedValue.ToString()),
                    v => { e.BoxedValue = Enum.Parse(t, names[v]); after?.Invoke(); });
            }
            else if (t == typeof(string) && (ListValues(acc) ?? FileChoices(e)) is string[] vals)
            {
                s = new ChoiceRow(vals.Select(Pretty).ToList(), () => Array.IndexOf(vals, e.BoxedValue as string),
                    v => { e.BoxedValue = vals[v]; after?.Invoke(); });
            }
            else if (t == typeof(int) && acc is AcceptableValueRange<int> ri)
            {
                s = new RangeRow(ri.MinValue, ri.MaxValue, (int)e.BoxedValue, (int)e.DefaultValue, f =>
                {
                    int n = Mathf.RoundToInt(f);
                    if ((int)e.BoxedValue != n) e.BoxedValue = n;
                });
            }
            else if (t == typeof(float) && acc is AcceptableValueRange<float> rf && rf.MaxValue - rf.MinValue >= 10f)
            {
                s = new RangeRow(rf.MinValue, rf.MaxValue, (float)e.BoxedValue, (float)e.DefaultValue, f =>
                {
                    if (Math.Abs((float)e.BoxedValue - f) > 0.001f) e.BoxedValue = f;
                });
            }
            else
            {
                s = new TextRow(() => e.GetSerializedValue(), str =>
                {
                    if (str == e.GetSerializedValue()) return;
                    e.SetSerializedValue(str);
                    after?.Invoke();
                });
            }
            Add(s, label);
            if (!string.IsNullOrEmpty(desc)) Note(desc, Muted, small: true);
        }

        private static string[] ListValues(AcceptableValueBase acc)
        {
            if (acc == null) return null;
            try
            {
                PropertyInfo p = acc.GetType().GetProperty("AcceptableValues", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p?.GetValue(acc) is IEnumerable list && !(list is string))
                {
                    string[] vals = list.Cast<object>().Select(o => o?.ToString() ?? "").ToArray();
                    return vals.Length > 0 ? vals : null;
                }
            }
            catch { }
            return null;
        }

        private static readonly Dictionary<string, Dictionary<string, string[]>> _fileChoices = new Dictionary<string, Dictionary<string, string[]>>();

        private string[] FileChoices(ConfigEntryBase e)
        {
            try
            {
                string path = e.ConfigFile?.ConfigFilePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                if (!_fileChoices.TryGetValue(path, out var map))
                {
                    map = new Dictionary<string, string[]>();
                    string section = "", pending = null;
                    foreach (string raw in File.ReadAllLines(path))
                    {
                        string line = raw.Trim();
                        if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); pending = null; }
                        else if (line.StartsWith("# Acceptable values:")) pending = line.Substring("# Acceptable values:".Length).Trim();
                        else if (!line.StartsWith("#") && line.Contains("="))
                        {
                            if (pending != null) map[section + "\n" + line.Substring(0, line.IndexOf('=')).Trim()] = pending.Split(new[] { ", " }, StringSplitOptions.None);
                            pending = null;
                        }
                    }
                    _fileChoices[path] = map;
                }
                if (!map.TryGetValue(e.Definition.Section + "\n" + e.Definition.Key, out string[] vals)) return null;
                string cur = e.BoxedValue as string;
                if (cur != null && !vals.Contains(cur)) return null; // the file is out of date
                return vals;
            }
            catch { return null; }
        }

        private static string Pretty(string v)
        {
            int c = v.IndexOf(": ", StringComparison.Ordinal);
            if (c > 0 && c < 10) v = v.Substring(c + 2);
            v = v.TrimStart('-', ' ');
            return v;
        }

        private void Add(Setting s, string label)
        {
            if (!(s is IKeyed keyed)) return;
            keyed.Key = Label(label);
            GameObject go = Instantiate(_cellPrefab, _list, false);
            go.name = "PLL row: " + label;
            _rows.Add(go);
            var cell = go.GetComponent<SettingsUICell>();
            if (cell == null) return;
            _cells.Add(cell);
            try
            {
                cell.Setup(s);
                if (s is TextRow text) WireText(cell, text);
                if (s is ButtonRow button) WireButton(cell, button);
            }
            catch (Exception e) { PllMenu.Warn("row '" + label + "': " + e.Message); }
        }

        private void WireButton(SettingsUICell cell, ButtonRow row)
        {
            Transform content = cell.m_settingsContentParent != null ? cell.m_settingsContentParent : cell.transform;
            if (content.GetComponentInChildren<Zorro.Settings.UI.ButtonSettingUI>(true) != null) return;
            if (_button == null) _button = FindDeep(transform, "UI_MainMenuButton_LeaveGame (2)")?.GetComponent<Button>();
            var dd = content.GetComponentInChildren<TMP_Dropdown>(true);
            if (_button == null) { if (dd != null) dd.interactable = false; return; }
            var slot = dd != null ? (RectTransform)dd.transform : null;
            GameObject b = Instantiate(_button.gameObject, slot != null ? slot : content, false);
            b.name = "PLL button";
            foreach (LocalizedText lt in b.GetComponentsInChildren<LocalizedText>(true)) DestroyImmediate(lt);
            var rt = (RectTransform)b.transform;
            if (slot != null)
            {
                // The drop-down keeps its place in the row but shows nothing; the button fills it.
                for (int i = 0; i < slot.childCount; i++) if (slot.GetChild(i) != rt) slot.GetChild(i).gameObject.SetActive(false);
                foreach (Graphic g in slot.GetComponents<Graphic>()) g.enabled = false;
                dd.enabled = false;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = new Vector2(0f, -4f); rt.offsetMax = new Vector2(0f, 4f);
                rt.localScale = Vector3.one; rt.localRotation = Quaternion.identity;
            }
            TMP_Text label = b.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = row.GetButtonText();
            var btn = b.GetComponent<Button>();
            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(row.Click);
            b.SetActive(true);
        }

        private static void WireText(SettingsUICell cell, TextRow row)
        {
            TMP_InputField field = cell.m_settingsContentParent != null
                ? cell.m_settingsContentParent.GetComponentInChildren<TMP_InputField>(true)
                : cell.GetComponentInChildren<TMP_InputField>(true);
            if (field == null) return;
            Slider slider = field.transform.parent != null ? field.transform.parent.GetComponentInChildren<Slider>(true) : null;
            if (slider != null && slider.transform.parent == field.transform.parent)
            {
                var fr = (RectTransform)field.transform; var sr = (RectTransform)slider.transform;
                var parent = (RectTransform)fr.parent;
                if (parent.GetComponent<LayoutGroup>() == null)
                {
                    Rect a = LocalRect(fr, parent), b = LocalRect(sr, parent);
                    float xMin = Mathf.Min(a.xMin, b.xMin), xMax = Mathf.Max(a.xMax, b.xMax);
                    fr.anchorMin = fr.anchorMax = new Vector2(0f, 0f);
                    fr.pivot = new Vector2(0f, 0f);
                    fr.anchoredPosition = new Vector2(xMin - parent.rect.xMin, a.yMin - parent.rect.yMin);
                    fr.sizeDelta = new Vector2(xMax - xMin, a.height);
                }
                else
                {
                    var le = field.GetComponent<LayoutElement>() ?? field.gameObject.AddComponent<LayoutElement>();
                    le.flexibleWidth = 1f;
                }
                slider.gameObject.SetActive(false);
            }
            if (field.textComponent != null) field.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
            field.contentType = TMP_InputField.ContentType.Standard;
            field.characterValidation = TMP_InputField.CharacterValidation.None;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = 0;
            field.SetTextWithoutNotify(row.Get() ?? "");
            if (field.placeholder is TMP_Text ph)
            {
                ph.text = "empty";
                ph.alignment = TextAlignmentOptions.MidlineLeft;
                ph.color = new Color(0.45f, 0.45f, 0.45f, 0.8f);
            }
            field.onEndEdit.AddListener(v => row.Set(v ?? ""));
        }

        private static Rect LocalRect(RectTransform r, RectTransform space)
        {
            var c = new Vector3[4];
            r.GetWorldCorners(c);
            Vector3 a = space.InverseTransformPoint(c[0]), b = space.InverseTransformPoint(c[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static string Label(string text)
        {
            string key = "DEPLL_ROW_" + (++_keyCounter);
            try
            {
                Dictionary<string, List<string>> table = LocalizedText.mainTable;
                if (table != null)
                {
                    int count = Math.Max(LocalizedText.LANGUAGE_COUNT, 1) + 2;
                    var row = new List<string>(count);
                    for (int i = 0; i < count; i++) row.Add(text);
                    table[key.ToUpperInvariant()] = row;
                }
            }
            catch { }
            return key;
        }

        private void Header(string text)
        {
            TextMeshProUGUI t = Text(text.ToUpperInvariant(), _fontSize * 0.95f, Accent, 22f, 6f);
            t.characterSpacing = 4f;
        }

        private void Note(string text, Color color, bool small = false) =>
            Text(text, _fontSize * (small ? 0.6f : 0.72f), color, small ? 0f : 4f, small ? 10f : 6f);

        private TextMeshProUGUI Text(string text, float size, Color color, float padTop, float padBottom)
        {
            var go = new GameObject("PLL text", typeof(RectTransform));
            go.layer = _list.gameObject.layer;
            go.transform.SetParent(_list, false);
            _rows.Add(go);

            var holder = (RectTransform)go.transform;
            var child = new GameObject("Text", typeof(RectTransform));
            child.layer = go.layer;
            child.transform.SetParent(holder, false);
            var t = child.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            if (_fontMat != null) t.fontSharedMaterial = _fontMat;
            t.fontSize = size;
            t.color = color;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.raycastTarget = false;
            t.text = text;

            float pad = 18f;
            float h = t.GetPreferredValues(text, _rowW - pad * 2f, 0f).y + padTop + padBottom;
            var rt = (RectTransform)child.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, padBottom); rt.offsetMax = new Vector2(-pad, -padTop);

            holder.sizeDelta = new Vector2(_rowW, h);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = h; le.preferredHeight = h;
            le.minWidth = _rowW; le.preferredWidth = _rowW;
            return t;
        }


        private static void OpenFolder(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            Application.OpenURL("file:///" + dir.Replace('\\', '/'));
        }

        private static string Trim(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "...";

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static string PathFrom(Transform top, Transform target)
        {
            var parts = new List<string>();
            for (Transform t = target; t != null && t != top; t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }
    }
}
