using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PLL.Library;

internal static class MenuVersionMark
{
    const string IconResource = "PLL.Library.MainMenu.PackageIcon.png";
    const string MarkName = "PLL Version Mark";
    static readonly Vector2 MainPosition = new(38f, 34f);
    static readonly Vector2 PausePosition = new(70f, 24f);
    static GameObject? mainRoot;
    static GameObject? pauseRoot;
    static OwnedTerminalImage? IconImage;

    static void AttachMain(MainMenu menu)
    {
        if (!menu || !menu.landfallButton || !menu.aggrocrabButton
            || menu.landfallButton.transform is not RectTransform landfall
            || menu.aggrocrabButton.transform is not RectTransform aggroCrab
            || landfall.parent is not RectTransform bar || aggroCrab.parent != bar
            || bar.parent is not RectTransform mainPage
            || !IsFullStretch(mainPage)
            || mainPage.Find("Menu/Buttons/Button_Quit/Hinge/Text")?.GetComponent<TextMeshProUGUI>() is not { } style
            || !style.font || !style.fontSharedMaterial)
        {
            Plugin.PLLLog.LogError("PEAK's main-menu panel is incomplete. The PLL version mark could not be added.");
            return;
        }

        Attach(ref mainRoot, mainPage, style, MainPosition);
    }

    static void AttachPause(GUIManager manager)
    {
        if (!manager || !manager.pauseMenuMainPage
            || manager.pauseMenuMainPage.transform is not RectTransform mainPage
            || !IsFullStretch(mainPage)
            || mainPage.Find("MainPage/Options/UI_MainMenuButton_Resume/Text")?.GetComponent<TextMeshProUGUI>() is not { } style
            || !style.font || !style.fontSharedMaterial)
        {
            Plugin.PLLLog.LogError("PEAK's pause-menu panel is incomplete. The PLL version mark could not be added.");
            return;
        }

        Attach(ref pauseRoot, mainPage, style, PausePosition);
    }

    static bool IsFullStretch(RectTransform value) =>
        value.anchorMin == Vector2.zero && value.anchorMax == Vector2.one;

    static void Attach(ref GameObject? ownedRoot, RectTransform page, TextMeshProUGUI style, Vector2 position)
    {
        if (ownedRoot && ownedRoot.transform.parent == page) return;
        if (ownedRoot) UnityEngine.Object.Destroy(ownedRoot);
        var root = new GameObject(MarkName, typeof(RectTransform));
        root.SetActive(false);
        var created = false;
        try
        {
            root.layer = page.gameObject.layer;
            root.transform.SetParent(page, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(190f, 36f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();

            CreateIcon(rect, root.layer);
            CreateText(rect, root.layer, style);
            ownedRoot = root;
            root.SetActive(true);
            created = true;
        }
        finally
        {
            if (!created) UnityEngine.Object.Destroy(root);
        }
    }

    static void CreateIcon(RectTransform parent, int layer)
    {
        var value = new GameObject("PLL Package Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        value.layer = layer;
        value.transform.SetParent(parent, false);
        var image = value.GetComponent<Image>();
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(28f, 28f);
        image.sprite = GetIconSprite();
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    static void CreateText(RectTransform parent, int layer, TextMeshProUGUI style)
    {
        var value = new GameObject("Version", typeof(RectTransform));
        value.layer = layer;
        value.transform.SetParent(parent, false);
        var text = value.AddComponent<TextMeshProUGUI>();
        var rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(36f, 0f);
        rect.offsetMax = Vector2.zero;
        text.font = style.font;
        text.fontSharedMaterial = style.fontSharedMaterial;
        text.fontSize = 16f;
        text.fontStyle = style.fontStyle;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableAutoSizing = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.text = $"PLL v{MyPluginInfo.PLUGIN_VERSION}";
    }

    internal static Sprite GetIconSprite()
    {
        if (IconImage is { } Image && Image.Sprite) return Image.Sprite;
        IconImage = TerminalPresentation.LoadEmbeddedImage(
            typeof(MenuVersionMark), IconResource, "PLL Package Icon");
        return IconImage.Sprite;
    }

    internal static void Shutdown()
    {
        if (mainRoot) UnityEngine.Object.Destroy(mainRoot);
        if (pauseRoot) UnityEngine.Object.Destroy(pauseRoot);
        IconImage?.Release();
        mainRoot = null;
        pauseRoot = null;
        IconImage = null;
    }

    [HarmonyPatch(typeof(MainMenu), "Start")]
    static class MainMenuStartPatch
    {
        [HarmonyPostfix]
        static void Postfix(MainMenu __instance) => AttachMain(__instance);
    }

    [HarmonyPatch(typeof(GUIManager), "Start")]
    static class GUIManagerStartPatch
    {
        [HarmonyPostfix]
        static void Postfix(GUIManager __instance) => AttachPause(__instance);
    }
}
