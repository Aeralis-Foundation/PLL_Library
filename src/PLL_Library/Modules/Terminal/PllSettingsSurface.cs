using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zorro.ControllerSupport;
using Zorro.UI;

namespace PLL.Library;

internal static class PllSettingsSurface
{
    const float SettingsLauncherGap = 8f;
    static readonly List<Surface> Surfaces = new();

    static void AttachSettings(UIPage nativePage)
    {
        if (!TryBind(nativePage, out var binding))
        {
            Plugin.PLLLog.LogError("PEAK's Settings page is incomplete. PLL settings could not be added.");
            return;
        }
        GetOrCreate(binding).EnsureSettingsLauncher(binding);
    }

    static void AttachPause(PauseMenuMainPage pausePage)
    {
        PurgeStale();
        if (!pausePage) return;
        var handler = pausePage.GetComponentInParent<UIPageHandler>();
        var settingsPage = handler
            ? handler.GetComponentInChildren<PauseMenuSettingsMenuPage>(true)
            : null;
        if (!settingsPage)
        {
            Plugin.PLLLog.LogError("PEAK's Settings page is missing. The PLL pause-menu button could not be added.");
            return;
        }

        if (!TryBind(settingsPage, out var binding))
        {
            Plugin.PLLLog.LogError("PEAK's Settings page is incomplete. PLL settings could not be added.");
            return;
        }
        var surface = GetOrCreate(binding);
        surface.EnsureSettingsLauncher(binding);
        surface.EnsurePauseLauncher(pausePage);
    }

    static Surface GetOrCreate(Binding binding)
    {
        PurgeStale();
        foreach (var surface in Surfaces)
        {
            if (surface.PageHandler == binding.PageHandler) return surface;
        }

        var created = Surface.Create(binding);
        Surfaces.Add(created);
        return created;
    }

    static bool TryBind(UIPage nativePage, out Binding binding)
    {
        binding = default;
        if (!nativePage || nativePage.transform is not RectTransform pageRect
            || pageRect.parent is not RectTransform pageParent
            || nativePage.GetComponentInParent<UIPageHandler>() is not { } pageHandler
            || nativePage.GetComponentsInChildren<UIPage>(true) is not { Length: 1 } pages
            || pages[0] != nativePage
            || nativePage.GetComponentInChildren<SharedSettingsMenu>(true) is not { } sharedMenu
            || sharedMenu.transform.Find("UI_MainMenuButton_LeaveGame (2)")?.GetComponent<Button>() is not { } backButton
            || sharedMenu.transform.Find("Header")?.GetComponentInChildren<TextMeshProUGUI>(true) is not { } header
            || sharedMenu.transform.Find("Content/Parent") is not { }
            || sharedMenu.transform.Find("Content/TABS") is not { } tabs
            || !tabs.Find("General")?.GetComponent<Button>()
            || !tabs.Find("Accessibility")?.GetComponent<Button>()
            || !tabs.Find("Graphics")?.GetComponent<Button>()
            || !tabs.Find("Audio")?.GetComponent<Button>())
            return false;

        binding = new Binding(nativePage, pageHandler, pageRect, pageParent, backButton, header);
        return true;
    }

    static void PurgeStale()
    {
        for (var i = Surfaces.Count - 1; i >= 0; i--)
        {
            if (Surfaces[i].PageHandler) continue;
            Surfaces[i].Dispose();
            Surfaces.RemoveAt(i);
        }
    }

    internal static void Shutdown()
    {
        foreach (var surface in Surfaces) surface.Dispose();
        Surfaces.Clear();
    }

    sealed class Surface
    {
        readonly GameObject root;
        readonly PllSettingsPage page;
        readonly List<SettingsLauncher> settingsLaunchers = new();
        GameObject? pauseLauncher;
        PauseMenuMainPage? pauseSource;
        Button? pauseSourceButton;
        Selectable? pausePreviousUp;

        internal UIPageHandler PageHandler { get; }

        Surface(UIPageHandler pageHandler, GameObject ownedRoot, PllSettingsPage ownedPage)
        {
            PageHandler = pageHandler;
            root = ownedRoot;
            page = ownedPage;
            LocalizedText.OnLangugageChanged += RecenterSettingsLaunchers;
        }

        internal static Surface Create(Binding binding)
        {
            var root = new GameObject("PLL SettingsPage", typeof(RectTransform));
            root.SetActive(false);
            var initialized = false;
            try
            {
                root.layer = binding.PageRect.gameObject.layer;
                root.transform.SetParent(binding.PageParent, false);
                TerminalPresentation.CopyRect((RectTransform)root.transform, binding.PageRect);
                root.transform.SetSiblingIndex(binding.PageRect.GetSiblingIndex() + 1);
                for (var i = 0; i < binding.PageRect.childCount; i++)
                {
                    var source = binding.PageRect.GetChild(i);
                    var child = UnityEngine.Object.Instantiate(source.gameObject, root.transform, false);
                    child.name = source.name;
                }

                var sharedMenus = root.GetComponentsInChildren<SharedSettingsMenu>(true);
                if (sharedMenus.Length != 1)
                    throw new InvalidOperationException("PEAK's settings-page clone does not contain one shared panel.");
                var shared = sharedMenus[0].transform;
                var header = shared.Find("Header")
                    ?? throw new InvalidOperationException("PEAK's settings-page clone has no header.");
                var content = shared.Find("Content")
                    ?? throw new InvalidOperationException("PEAK's settings-page clone has no content root.");
                var tabsRoot = content.Find("TABS")
                    ?? throw new InvalidOperationException("PEAK's settings-page clone has no tabs.");
                var settingsParent = content.Find("Parent")
                    ?? throw new InvalidOperationException("PEAK's settings-page clone has no settings-content parent.");
                var backButton = shared.Find("UI_MainMenuButton_LeaveGame (2)")?.GetComponent<Button>()
                    ?? throw new InvalidOperationException("PEAK's settings-page clone has no Back button.");

                DestroyComponents<SharedSettingsMenu>(root);
                DestroyComponents<SettingsTABS>(root);
                UnityEngine.Object.DestroyImmediate(settingsParent.gameObject);
                DestroyChild(tabsRoot, "Controls");
                DestroyChild(tabsRoot, "GlyphLeft");
                DestroyChild(tabsRoot, "GlyphRight");
                DestroyChild(tabsRoot, "Audio");
                SetLiteralText(header, "PLL");
                MakePink(backButton.gameObject);

                var tabBindings = new[]
                {
                    (NativeName: "General", Label: "Map"),
                    (NativeName: "Accessibility", Label: "PLL Configuration"),
                    (NativeName: "Graphics", Label: "FAQ & Issues")
                };
                var tabs = new Button[tabBindings.Length];
                var selected = new GameObject[tabBindings.Length];
                var labels = new TextMeshProUGUI[tabBindings.Length];
                for (var i = 0; i < tabBindings.Length; i++)
                {
                    var (nativeName, label) = tabBindings[i];
                    var tab = tabsRoot.Find(nativeName)
                        ?? throw new InvalidOperationException($"PEAK's settings-page clone has no {nativeName} tab.");
                    tabs[i] = tab.GetComponent<Button>()
                        ?? throw new InvalidOperationException($"PEAK's settings-page clone {nativeName} tab is not a button.");
                    var tabState = tab.GetComponent<SettingsTABSButton>()
                        ?? throw new InvalidOperationException($"PEAK's settings-page clone {nativeName} tab has no native state binding.");
                    if (!tabState.SelectedGraphic || !tabState.text)
                        throw new InvalidOperationException($"PEAK's settings-page clone {nativeName} tab state binding is incomplete.");
                    selected[i] = tabState.SelectedGraphic;
                    labels[i] = tabState.text;
                    tabs[i].onClick = new Button.ButtonClickedEvent();
                    SetLiteralText(labels[i].transform, label);
                }

                DestroyComponents<SettingsTABSButton>(root);
                var page = root.AddComponent<PllSettingsPage>();
                page.Initialize(binding.PageHandler, backButton, tabs, selected, labels);
                initialized = true;
                return new Surface(binding.PageHandler, root, page);
            }
            finally
            {
                if (!initialized) UnityEngine.Object.Destroy(root);
            }
        }

        internal void EnsureSettingsLauncher(Binding binding)
        {
            PurgeStaleLaunchers();
            foreach (var launcher in settingsLaunchers)
            {
                if (launcher.Source != binding.NativePage) continue;
                launcher.Position();
                return;
            }

            var sourceRect = (RectTransform)binding.BackButton.transform;
            var root = CreateLauncher(
                binding.BackButton, sourceRect.parent, "PLL Settings Launcher",
                binding.NativePage, page, MenuVersionMark.GetIconSprite());
            var rect = (RectTransform)root.transform;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, sourceRect.rect.width);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, sourceRect.rect.height);
            rect.SetSiblingIndex(sourceRect.GetSiblingIndex() + 1);
            CenterComposition(root.GetComponent<Button>());
            var created = new SettingsLauncher(binding.NativePage, root, binding.Header, sourceRect, rect);
            created.Position();
            settingsLaunchers.Add(created);
            root.SetActive(true);
        }

        internal void EnsurePauseLauncher(PauseMenuMainPage source)
        {
            if (pauseLauncher && pauseSource == source) return;
            ClearPauseLauncher();
            if (source.transform.Find("MainPage/Options") is not RectTransform options
                || options.Find("UI_MainMenuButton_Resume")?.GetComponent<Button>() is not { } resumeButton
                || source.GetComponentInParent<UIPageHandler>() != PageHandler)
                throw new InvalidOperationException("PEAK's pause-page launcher binding is unavailable.");

            var sourceRect = (RectTransform)resumeButton.transform;
            pauseLauncher = CreateLauncher(
                resumeButton, options, "PLL Pause Launcher", source, page, MenuVersionMark.GetIconSprite());
            var rect = (RectTransform)pauseLauncher.transform;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, sourceRect.rect.width * 0.70f);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, sourceRect.rect.height);
            rect.SetSiblingIndex(sourceRect.GetSiblingIndex());
            var launcherButton = pauseLauncher.GetComponent<Button>();
            var resumeNavigation = resumeButton.navigation;
            var launcherNavigation = resumeNavigation;
            launcherNavigation.selectOnDown = resumeButton;
            launcherButton.navigation = launcherNavigation;
            pausePreviousUp = resumeNavigation.selectOnUp;
            resumeNavigation.selectOnUp = launcherButton;
            resumeButton.navigation = resumeNavigation;
            if (pausePreviousUp)
            {
                var previousNavigation = pausePreviousUp.navigation;
                if (previousNavigation.selectOnDown == resumeButton)
                {
                    previousNavigation.selectOnDown = launcherButton;
                    pausePreviousUp.navigation = previousNavigation;
                }
            }
            CenterComposition(launcherButton);
            pauseLauncher.SetActive(true);
            pauseSource = source;
            pauseSourceButton = resumeButton;
        }

        void ClearPauseLauncher()
        {
            if (pauseSourceButton && pauseLauncher)
            {
                var launcherButton = pauseLauncher.GetComponent<Button>();
                var navigation = pauseSourceButton.navigation;
                if (navigation.selectOnUp == launcherButton)
                {
                    navigation.selectOnUp = pausePreviousUp;
                    pauseSourceButton.navigation = navigation;
                }
                if (pausePreviousUp)
                {
                    navigation = pausePreviousUp.navigation;
                    if (navigation.selectOnDown == launcherButton)
                    {
                        navigation.selectOnDown = pauseSourceButton;
                        pausePreviousUp.navigation = navigation;
                    }
                }
            }
            if (pauseLauncher) UnityEngine.Object.Destroy(pauseLauncher);
            pauseLauncher = null;
            pauseSource = null;
            pauseSourceButton = null;
            pausePreviousUp = null;
        }

        void RecenterSettingsLaunchers()
        {
            PurgeStaleLaunchers();
            foreach (var launcher in settingsLaunchers) launcher.Position();
        }

        void PurgeStaleLaunchers()
        {
            for (var i = settingsLaunchers.Count - 1; i >= 0; i--)
            {
                if (settingsLaunchers[i].Source && settingsLaunchers[i].Root) continue;
                settingsLaunchers[i].RestoreSourcePosition();
                if (settingsLaunchers[i].Root) UnityEngine.Object.Destroy(settingsLaunchers[i].Root);
                settingsLaunchers.RemoveAt(i);
            }
        }

        internal void Dispose()
        {
            LocalizedText.OnLangugageChanged -= RecenterSettingsLaunchers;
            foreach (var launcher in settingsLaunchers)
            {
                launcher.RestoreSourcePosition();
                if (launcher.Root) UnityEngine.Object.Destroy(launcher.Root);
            }
            settingsLaunchers.Clear();
            ClearPauseLauncher();
            if (root) UnityEngine.Object.Destroy(root);
        }

        sealed class SettingsLauncher
        {
            internal readonly UIPage Source;
            internal readonly GameObject Root;
            readonly TextMeshProUGUI header;
            readonly RectTransform back;
            readonly RectTransform pll;
            readonly Vector2 originalBackPosition;
            Vector2 positionedBackPosition;

            internal SettingsLauncher(
                UIPage source, GameObject root, TextMeshProUGUI settingsHeader,
                RectTransform nativeBack, RectTransform pllLauncher)
            {
                Source = source;
                Root = root;
                header = settingsHeader;
                back = nativeBack;
                pll = pllLauncher;
                originalBackPosition = back.anchoredPosition;
                positionedBackPosition = originalBackPosition;
            }

            internal void Position()
            {
                if (!header || !back || !pll || back.anchoredPosition != positionedBackPosition) return;
                header.ForceMeshUpdate(true, false);
                var center = back.anchoredPosition + (Vector2)back.parent.InverseTransformVector(
                    header.transform.TransformPoint(header.textBounds.center) - back.position);
                center.y = originalBackPosition.y;
                var offset = Vector2.right * ((back.rect.width + SettingsLauncherGap) * 0.5f);
                pll.anchoredPosition = center + offset;
                back.anchoredPosition = center - offset;
                positionedBackPosition = back.anchoredPosition;
            }

            internal void RestoreSourcePosition()
            {
                if (back && back.anchoredPosition == positionedBackPosition)
                    back.anchoredPosition = originalBackPosition;
            }
        }
    }

    static GameObject CreateLauncher(
        Button template,
        Transform parent,
        string name,
        UIPage source,
        PllSettingsPage page,
        Sprite icon)
    {
        var launcher = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
        launcher.name = name;
        launcher.SetActive(false);
        var initialized = false;
        try
        {
            var button = launcher.GetComponent<Button>()
                ?? throw new InvalidOperationException("PEAK's cloned settings launcher is not a button.");
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => page.OpenFrom(source));
            SetLiteralText(launcher.transform, "PLL");
            MakePink(launcher);
            CreateIcon((RectTransform)launcher.transform, icon);
            initialized = true;
            return launcher;
        }
        finally
        {
            if (!initialized) UnityEngine.Object.Destroy(launcher);
        }
    }

    static void CreateIcon(RectTransform parent, Sprite sprite)
    {
        var value = new GameObject("PLL Package Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        value.layer = parent.gameObject.layer;
        value.transform.SetParent(parent, false);
        var image = value.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    static void CenterComposition(Button button)
    {
        var rect = (RectTransform)button.transform;
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        var icon = rect.Find("PLL Package Icon")?.GetComponent<Image>();
        if (!label || !icon) throw new InvalidOperationException("PLL's settings launcher composition is incomplete.");
        var scale = rect.rect.height / 67f;
        var iconSize = Math.Min(32f, rect.rect.height * 0.42f);
        var sprite = icon.sprite;
        var padding = UnityEngine.Sprites.DataUtility.GetPadding(sprite);
        var iconVerticalOffset = (padding.w - padding.y) * iconSize / (2f * sprite.rect.height);
        var gap = 6f * scale;
        var preferred = label.GetPreferredValues("PLL");
        var labelWidth = Math.Min(preferred.x, rect.rect.width - iconSize - gap - 8f * scale);
        var totalWidth = iconSize + gap + labelWidth;

        var iconRect = icon.rectTransform;
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = new Vector2(-totalWidth * 0.5f + iconSize * 0.5f, iconVerticalOffset);
        iconRect.sizeDelta = new Vector2(iconSize, iconSize);

        var labelRect = label.rectTransform;
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = new Vector2(totalWidth * 0.5f - labelWidth * 0.5f, 0f);
        labelRect.sizeDelta = new Vector2(labelWidth, Math.Min(preferred.y, rect.rect.height));
        label.alignment = TextAlignmentOptions.Midline;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
    }

    static void SetLiteralText(Transform root, string value)
    {
        foreach (var localized in root.GetComponentsInChildren<LocalizedText>(true))
            UnityEngine.Object.DestroyImmediate(localized);
        var texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        if (texts.Length == 0) throw new InvalidOperationException($"PEAK's {root.name} UI has no text.");
        foreach (var text in texts) text.text = value;
    }

    static void MakePink(GameObject root)
    {
        foreach (var image in root.GetComponentsInChildren<Image>(true))
        {
            image.color = image.name switch
            {
                "Shadow" => TerminalPalette.PinkShadow,
                "Border" or "Glow" => TerminalPalette.NoteLight,
                _ => TerminalPalette.NoteDark
            };
        }
    }

    static void DestroyComponents<T>(GameObject root) where T : Component
    {
        foreach (var component in root.GetComponentsInChildren<T>(true))
            UnityEngine.Object.DestroyImmediate(component);
    }

    static void DestroyChild(Transform parent, string name)
    {
        if (parent.Find(name) is { } child) UnityEngine.Object.DestroyImmediate(child.gameObject);
    }

    readonly struct Binding
    {
        internal readonly UIPage NativePage;
        internal readonly UIPageHandler PageHandler;
        internal readonly RectTransform PageRect;
        internal readonly RectTransform PageParent;
        internal readonly Button BackButton;
        internal readonly TextMeshProUGUI Header;

        internal Binding(
            UIPage nativePage,
            UIPageHandler pageHandler,
            RectTransform pageRect,
            RectTransform pageParent,
            Button backButton,
            TextMeshProUGUI header)
        {
            NativePage = nativePage;
            PageHandler = pageHandler;
            PageRect = pageRect;
            PageParent = pageParent;
            BackButton = backButton;
            Header = header;
        }
    }

    [HarmonyPatch(typeof(PauseMenuMainPage), "Start")]
    static class PauseMenuMainPageStartPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        static void Postfix(PauseMenuMainPage __instance) => AttachPause(__instance);
    }

    [HarmonyPatch(typeof(PauseMenuSettingsMenuPage), "Start")]
    static class PauseMenuSettingsPageStartPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        static void Postfix(PauseMenuSettingsMenuPage __instance) => AttachSettings(__instance);
    }

    [HarmonyPatch(typeof(MainMenuSettingsPage), "Start")]
    static class MainMenuSettingsPageStartPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        static void Postfix(MainMenuSettingsPage __instance) => AttachSettings(__instance);
    }
}

internal sealed class PllSettingsPage : UIPage, IHaveParentPage, INavigationPage
{
    Button backButton = null!;
    GameObject[] selected = Array.Empty<GameObject>();
    TextMeshProUGUI[] labels = Array.Empty<TextMeshProUGUI>();
    UIPage returnPage = null!;

    internal void Initialize(
        UIPageHandler owner,
        Button back,
        Button[] tabs,
        GameObject[] selectedStates,
        TextMeshProUGUI[] tabLabels)
    {
        pageHandler = owner;
        backButton = back;
        selected = selectedStates;
        labels = tabLabels;
        backButton.onClick = new Button.ButtonClickedEvent();
        backButton.onClick.AddListener(BackClicked);
        for (var i = 0; i < tabs.Length; i++)
        {
            var index = i;
            tabs[i].onClick.AddListener(() => SelectTab(index));
        }
        SelectTab(0);
    }

    internal void OpenFrom(UIPage source)
    {
        if (source.GetComponentInParent<UIPageHandler>() != pageHandler)
            throw new InvalidOperationException("PEAK's settings launcher and PLL page have different page handlers.");
        returnPage = source;
        pageHandler.TransistionToPage(this, new SetActivePageTransistion());
    }

    void BackClicked() => pageHandler.TransistionToPage(returnPage, new SetActivePageTransistion());

    void SelectTab(int index)
    {
        for (var i = 0; i < selected.Length; i++)
        {
            var isSelected = i == index;
            labels[i].color = isSelected ? Color.black : Color.white;
            selected[i].SetActive(isSelected);
        }
    }

    public (UIPage, PageTransistion) GetParentPage() => (returnPage, new SetActivePageTransistion());

    public GameObject GetFirstSelectedGameObject() => backButton.gameObject;
}
