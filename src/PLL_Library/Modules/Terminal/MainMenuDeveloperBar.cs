using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PLL.Library;

internal static class MainMenuDeveloperBar
{
    const string LogoResource = "PLL.Library.MainMenu.AeralisFoundationLogoWhite.png";
    static readonly Vector2 LogoOffset = new(-44f, -19f);

    static MainMenuDeveloperBarControl? Control;

    static void Attach(MainMenu Menu)
    {
        if (!MainMenuBinding.TryCreate(Menu, out var Binding, out var Failure))
        {
            Plugin.PLLLog.LogError(Failure);
            return;
        }

        var ExistingControl = Binding.Bar.GetComponent<MainMenuDeveloperBarControl>();
        if (Control && Control != ExistingControl)
        {
            var StaleControl = Control;
            StaleControl.Dispose();
            UnityEngine.Object.Destroy(StaleControl);
        }
        if (ExistingControl)
        {
            Control = ExistingControl;
            return;
        }

        MainMenuOwnedContent? OwnedContent = null;
        MainMenuDeveloperBarControl? CreatedControl = null;
        var Initialized = false;
        try
        {
            OwnedContent = MainMenuOwnedContent.Create(Binding);
            CreatedControl = Binding.Bar.gameObject.AddComponent<MainMenuDeveloperBarControl>();
            OwnedContent.LogoRoot.SetActive(true);
            CreatedControl.Initialize(Binding, OwnedContent);
            Control = CreatedControl;
            OwnedContent = null;
            Initialized = true;
        }
        finally
        {
            if (!Initialized && CreatedControl)
            {
                CreatedControl.Dispose();
                UnityEngine.Object.Destroy(CreatedControl);
                OwnedContent = null;
            }
            if (!Initialized) OwnedContent?.Release();
        }
    }

    internal static void Shutdown()
    {
        var CurrentControl = Control;
        Control = null;
        List<Exception>? Failures = null;
        if (CurrentControl)
        {
            CleanupOperations.Capture(ref Failures, CurrentControl.Dispose);
            CleanupOperations.Capture(ref Failures, () => UnityEngine.Object.Destroy(CurrentControl));
        }
        CleanupOperations.Capture(ref Failures, DeveloperNoteLoader.Shutdown);
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is not null)
            throw new AggregateException("Main-menu developer content shutdown failed.", Failures);
    }

    internal static void Unregister(MainMenuDeveloperBarControl Instance)
    {
        if (Control == Instance) Control = null;
    }


    static GameObject CreateLogo(MainMenuBinding Binding, Sprite LogoSprite, out Image LogoImage)
    {
        var Root = new GameObject("Aeralis Foundation", typeof(RectTransform));
        Root.SetActive(false);
        var Created = false;
        try
        {
            Root.transform.SetParent(Binding.Bar, false);
            var Rect = (RectTransform)Root.transform;
            TerminalPresentation.CopyRect(Rect, Binding.LandfallRect);
            Rect.anchoredPosition -= Binding.AggroCrabRect.anchoredPosition
                - Binding.LandfallRect.anchoredPosition - LogoOffset;

            var ImageObject = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            ImageObject.transform.SetParent(Rect, false);
            LogoImage = ImageObject.GetComponent<Image>();
            TerminalPresentation.CopyRect(
                LogoImage.rectTransform, Binding.LandfallImage.rectTransform);
            LogoImage.sprite = LogoSprite;
            LogoImage.color = Binding.LandfallImage.color;
            LogoImage.material = Binding.LandfallImage.material;
            LogoImage.preserveAspect = true;
            LogoImage.raycastTarget = false;
            LogoImage.maskable = Binding.LandfallImage.maskable;
            Created = true;
            return Root;
        }
        finally
        {
            if (!Created) UnityEngine.Object.Destroy(Root);
        }
    }

    static GameObject CreateNote(
        MainMenuBinding Binding,
        out RectTransform NoteRect,
        out TextMeshProUGUI SizingText,
        out TextMeshProUGUI MessageText)
    {
        var Root = new GameObject("Aeralis Foundation Note", typeof(RectTransform));
        Root.SetActive(false);
        var Created = false;
        try
        {
            Root.transform.SetParent(Binding.NoteParent, false);
            NoteRect = (RectTransform)Root.transform;
            TerminalPresentation.CopyRect(NoteRect, Binding.NormalNote);
            NoteRect.localRotation = Quaternion.Euler(0f, 0f, 6f);
            NoteRect.SetAsLastSibling();

            var TextObject = UnityEngine.Object.Instantiate(
                Binding.NormalText.gameObject, NoteRect, false);
            TextObject.name = "Text (TMP)";
            var TextRect = (RectTransform)TextObject.transform;
            TextRect.anchoredPosition = new Vector2(TextRect.anchoredPosition.x, -66f);
            SizingText = TextObject.GetComponent<TextMeshProUGUI>();
            MessageText = TextRect.Find("MESSAGE")?.GetComponent<TextMeshProUGUI>()!;
            var DarkPanel = TextRect.Find("Panel")?.GetComponent<Image>();
            var LightPanel = TextRect.Find("Panel (1)")?.GetComponent<Image>();
            var Caret = TextRect.Find("Caret") as RectTransform;
            var CaretDarkPanel = Caret?.Find("Panel (3)")?.GetComponent<Image>();
            var CaretLightPanel = Caret?.Find("Panel (2)")?.GetComponent<Image>();
            if (!SizingText || !MessageText || !DarkPanel || !LightPanel
                || !Caret || !CaretDarkPanel || !CaretLightPanel)
                throw new InvalidOperationException(
                    "PEAK's creator-message text template is incomplete.");

            SizingText.richText = false;
            MessageText.richText = false;
            SizingText.text = string.Empty;
            MessageText.text = string.Empty;
            var SizingColor = SizingText.color;
            SizingColor.a = 0f;
            SizingText.color = SizingColor;
            MessageText.color = Color.white;
            DarkPanel.color = TerminalPalette.NoteDark;
            LightPanel.color = TerminalPalette.NoteLight;
            Caret.anchorMin = new Vector2(0.784f, 0f);
            Caret.anchorMax = Caret.anchorMin;
            Caret.anchoredPosition = new Vector2(0f, 88f);
            Caret.localRotation = Quaternion.Euler(0f, 0f, -4.3f);
            CaretDarkPanel.color = TerminalPalette.NoteDark;
            CaretLightPanel.color = TerminalPalette.NoteLight;
            foreach (var Graphic in TextObject.GetComponentsInChildren<Graphic>(true))
                Graphic.raycastTarget = false;
            Created = true;
            return Root;
        }
        finally
        {
            if (!Created) UnityEngine.Object.Destroy(Root);
        }
    }

    internal readonly struct MainMenuBinding
    {
        internal readonly RectTransform Bar;
        internal readonly RectTransform LandfallRect;
        internal readonly RectTransform AggroCrabRect;
        internal readonly Image LandfallImage;
        internal readonly Image AggroCrabImage;
        internal readonly RectTransform NoteParent;
        internal readonly RectTransform NormalNote;
        internal readonly RectTransform NormalText;
        internal readonly RectTransform ShillGroup;
        internal readonly RectTransform ShillNormalNote;
        internal readonly RectTransform ShillNormalText;
        internal readonly RectTransform ShillPromoNote;
        internal readonly TextMeshProUGUI[] OfficialTexts;

        internal static bool TryCreate(MainMenu Menu, out MainMenuBinding Binding, out string Failure)
        {
            Binding = default;
            if (!Menu || !Menu.landfallButton || !Menu.aggrocrabButton
                || Menu.landfallButton.transform is not RectTransform LandfallRect
                || Menu.aggrocrabButton.transform is not RectTransform AggroCrabRect
                || LandfallRect.parent is not RectTransform Bar
                || AggroCrabRect.parent != Bar
                || Menu.landfallButton.targetGraphic is not Image LandfallImage
                || Menu.aggrocrabButton.targetGraphic is not Image AggroCrabImage
                || Bar.parent is not RectTransform MainPage)
            {
                Failure = "PEAK's developer bar is missing. The Aeralis Foundation menu mark could not be added.";
                return false;
            }

            var DevMessage = MainPage.GetComponent<DevMessageUI>();
            var NormalNote = DevMessage && DevMessage.parent
                ? DevMessage.parent.transform as RectTransform
                : null;
            var ShillGroup = DevMessage && DevMessage.shillParent
                ? DevMessage.shillParent.transform as RectTransform
                : null;
            var NoteParent = NormalNote ? NormalNote.parent as RectTransform : null;
            var NormalText = NormalNote ? NormalNote.Find("Text (TMP)") as RectTransform : null;
            var ShillNormalNote = ShillGroup ? ShillGroup.Find("DevMessage_Normal") as RectTransform : null;
            var ShillNormalText = ShillNormalNote ? ShillNormalNote.Find("Text (TMP)") as RectTransform : null;
            var ShillPromoNote = ShillGroup ? ShillGroup.Find("DevMessage_Shill") as RectTransform : null;
            if (!DevMessage || !NormalNote || !ShillGroup || !NoteParent || ShillGroup.parent != NoteParent
                || !NormalText || !ShillNormalNote || !ShillNormalText || !ShillPromoNote
                || DevMessage.texts is not { Length: > 0 } OfficialTexts)
            {
                Failure = "PEAK's creator-message panel is missing. The Aeralis Foundation note could not be added.";
                return false;
            }
            for (var Index = 0; Index < OfficialTexts.Length; Index++)
            {
                if (OfficialTexts[Index]) continue;
                Failure = "PEAK's creator-message panel is missing. The Aeralis Foundation note could not be added.";
                return false;
            }

            Binding = new MainMenuBinding(
                Bar, LandfallRect, AggroCrabRect, LandfallImage, AggroCrabImage,
                NoteParent, NormalNote, NormalText, ShillGroup, ShillNormalNote, ShillNormalText, ShillPromoNote,
                OfficialTexts);
            Failure = string.Empty;
            return true;
        }

        MainMenuBinding(
            RectTransform Bar,
            RectTransform LandfallRect,
            RectTransform AggroCrabRect,
            Image LandfallImage,
            Image AggroCrabImage,
            RectTransform NoteParent,
            RectTransform NormalNote,
            RectTransform NormalText,
            RectTransform ShillGroup,
            RectTransform ShillNormalNote,
            RectTransform ShillNormalText,
            RectTransform ShillPromoNote,
            TextMeshProUGUI[] OfficialTexts)
        {
            this.Bar = Bar;
            this.LandfallRect = LandfallRect;
            this.AggroCrabRect = AggroCrabRect;
            this.LandfallImage = LandfallImage;
            this.AggroCrabImage = AggroCrabImage;
            this.NoteParent = NoteParent;
            this.NormalNote = NormalNote;
            this.NormalText = NormalText;
            this.ShillGroup = ShillGroup;
            this.ShillNormalNote = ShillNormalNote;
            this.ShillNormalText = ShillNormalText;
            this.ShillPromoNote = ShillPromoNote;
            this.OfficialTexts = OfficialTexts;
        }
    }

    internal sealed class MainMenuOwnedContent
    {
        bool Released;

        internal GameObject LogoRoot { get; }
        internal GameObject NoteRoot { get; }
        internal OwnedTerminalImage LogoImage { get; }
        internal MainMenuDeveloperLogoBob LandfallLogo { get; }
        internal MainMenuDeveloperLogoBob AggroCrabLogo { get; }
        internal MainMenuDeveloperLogoBob FoundationLogo { get; }
        internal RectTransform NoteRect { get; }
        internal TextMeshProUGUI NoteSizingText { get; }
        internal TextMeshProUGUI NoteMessageText { get; }

        MainMenuOwnedContent(
            GameObject LogoRoot,
            GameObject NoteRoot,
            OwnedTerminalImage LogoImage,
            MainMenuDeveloperLogoBob LandfallLogo,
            MainMenuDeveloperLogoBob AggroCrabLogo,
            MainMenuDeveloperLogoBob FoundationLogo,
            RectTransform NoteRect,
            TextMeshProUGUI NoteSizingText,
            TextMeshProUGUI NoteMessageText)
        {
            this.LogoRoot = LogoRoot;
            this.NoteRoot = NoteRoot;
            this.LogoImage = LogoImage;
            this.LandfallLogo = LandfallLogo;
            this.AggroCrabLogo = AggroCrabLogo;
            this.FoundationLogo = FoundationLogo;
            this.NoteRect = NoteRect;
            this.NoteSizingText = NoteSizingText;
            this.NoteMessageText = NoteMessageText;
        }

        internal static MainMenuOwnedContent Create(MainMenuBinding Binding)
        {
            var LoadedLogoImage = TerminalPresentation.LoadEmbeddedImage(
                typeof(MainMenuDeveloperBar), LogoResource, "Aeralis Foundation Logo");
            GameObject? CreatedLogoRoot = null;
            GameObject? CreatedNoteRoot = null;
            MainMenuDeveloperLogoBob? CreatedLandfallLogo = null;
            MainMenuDeveloperLogoBob? CreatedAggroCrabLogo = null;
            MainMenuDeveloperLogoBob? CreatedFoundationLogo = null;
            var Created = false;
            try
            {
                CreatedLogoRoot = CreateLogo(Binding, LoadedLogoImage.Sprite, out var LogoGraphic);
                CreatedNoteRoot = CreateNote(Binding, out var CreatedNoteRect, out var CreatedSizingText, out var CreatedMessageText);
                CreatedLandfallLogo = Binding.LandfallImage.gameObject.AddComponent<MainMenuDeveloperLogoBob>();
                CreatedAggroCrabLogo = Binding.AggroCrabImage.gameObject.AddComponent<MainMenuDeveloperLogoBob>();
                CreatedFoundationLogo = LogoGraphic.gameObject.AddComponent<MainMenuDeveloperLogoBob>();
                var Content = new MainMenuOwnedContent(
                    CreatedLogoRoot, CreatedNoteRoot, LoadedLogoImage,
                    CreatedLandfallLogo, CreatedAggroCrabLogo, CreatedFoundationLogo,
                    CreatedNoteRect, CreatedSizingText, CreatedMessageText);
                Created = true;
                return Content;
            }
            finally
            {
                if (!Created)
                {
                    if (CreatedLogoRoot) UnityEngine.Object.Destroy(CreatedLogoRoot);
                    if (CreatedNoteRoot) UnityEngine.Object.Destroy(CreatedNoteRoot);
                    if (CreatedLandfallLogo) UnityEngine.Object.Destroy(CreatedLandfallLogo);
                    if (CreatedAggroCrabLogo) UnityEngine.Object.Destroy(CreatedAggroCrabLogo);
                    if (CreatedFoundationLogo) UnityEngine.Object.Destroy(CreatedFoundationLogo);
                    LoadedLogoImage.Release();
                }
            }
        }

        internal void Release()
        {
            if (Released) return;
            Released = true;
            if (LogoRoot) UnityEngine.Object.Destroy(LogoRoot);
            if (NoteRoot) UnityEngine.Object.Destroy(NoteRoot);
            if (LandfallLogo) UnityEngine.Object.Destroy(LandfallLogo);
            if (AggroCrabLogo) UnityEngine.Object.Destroy(AggroCrabLogo);
            LogoImage.Release();
        }
    }

    [HarmonyPatch(typeof(MainMenu), "Start")]
    static class MainMenuStartPatch
    {
        [HarmonyPostfix]
        static void Postfix(MainMenu __instance) => Attach(__instance);
    }
}

internal sealed class MainMenuDeveloperBarControl : MonoBehaviour
{
    const float BobPeriodSeconds = 2f;
    const float BobTravel = 13.05f;
    const float NoteGap = 125f;
    const float NoteHorizontalOffset = 72f;
    const float NoteLogoGap = 8f;

    readonly Vector3[] Corners = new Vector3[4];
    readonly List<Graphic> Graphics = new();
    MainMenuDeveloperBar.MainMenuBinding Binding;
    MainMenuDeveloperBar.MainMenuOwnedContent? Content;
    float AnimationStartedAt;
    bool Initialized;

    internal void Dispose()
    {
        MainMenuDeveloperBar.Unregister(this);
        if (!Initialized) return;
        DeveloperNoteLoader.ResultAvailable -= SetDeveloperNote;
        OnDisable();
        Initialized = false;
        var CurrentContent = Content!;
        Content = null;
        foreach (var Text in Binding.OfficialTexts)
        {
            if (Text) Text.UnregisterDirtyLayoutCallback(Reflow);
        }
        Binding = default;
        CurrentContent.Release();
    }

    internal void Initialize(
        MainMenuDeveloperBar.MainMenuBinding Binding,
        MainMenuDeveloperBar.MainMenuOwnedContent OwnedContent)
    {
        this.Binding = Binding;
        Content = OwnedContent;
        Initialized = true;
        AnimationStartedAt = Time.unscaledTime;
        foreach (var Text in Binding.OfficialTexts)
            Text.RegisterDirtyLayoutCallback(Reflow);
        DeveloperNoteLoader.ResultAvailable += SetDeveloperNote;
        if (DeveloperNoteLoader.Result is { } CachedResult)
            SetDeveloperNote(CachedResult);
        DeveloperNoteLoader.Start(FileManager.GetNote.Value);
    }

    void SetDeveloperNote(string Text)
    {
        if (!Initialized) return;
        var OwnedContent = Content!;
        OwnedContent.NoteSizingText.text = Text;
        OwnedContent.NoteMessageText.text = Text;
        if (!OwnedContent.NoteRoot.activeSelf) OwnedContent.NoteRoot.SetActive(true);
        Reflow();
    }

    void LateUpdate()
    {
        if (!Initialized) return;
        var OwnedContent = Content!;
        var Elapsed = Time.unscaledTime - AnimationStartedAt;
        var Offset = BobTravel * 0.5f
            * (1f - Mathf.Cos(Elapsed * Mathf.PI * 2f / BobPeriodSeconds));
        OwnedContent.LandfallLogo.SetOffset(Offset);
        OwnedContent.AggroCrabLogo.SetOffset(Offset);
        OwnedContent.FoundationLogo.SetOffset(Offset);

        var TextRect = OwnedContent.NoteSizingText.rectTransform;
        var Position = TextRect.anchoredPosition;
        var PositionY = (Binding.ShillGroup.gameObject.activeInHierarchy
            ? Binding.ShillNormalText
            : Binding.NormalText).anchoredPosition.y;
        if (Mathf.Approximately(Position.y, PositionY)) return;
        Position.y = PositionY;
        TextRect.anchoredPosition = Position;
    }

    void Reflow()
    {
        if (!Initialized || Content is not { } OwnedContent) return;
        if (!OwnedContent.LogoRoot || !OwnedContent.FoundationLogo || !Binding.NoteParent
            || !OwnedContent.NoteRect || !OwnedContent.NoteSizingText
            || !OwnedContent.NoteMessageText || !Binding.NormalNote
            || !Binding.ShillNormalNote || !Binding.ShillPromoNote || !Binding.ShillGroup) return;
        var MessageWidth = OwnedContent.NoteMessageText.rectTransform.rect.width;
        var PreferredHeight = OwnedContent.NoteMessageText
            .GetPreferredValues(MessageWidth, Mathf.Infinity).y;
        var TextRect = OwnedContent.NoteSizingText.rectTransform;
        if (!Mathf.Approximately(TextRect.rect.height, PreferredHeight))
            TextRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical, PreferredHeight);
        var OfficialBounds = Binding.ShillGroup.gameObject.activeInHierarchy
            ? Union(
                GetVisibleBounds(Binding.ShillNormalNote),
                GetVisibleBounds(Binding.ShillPromoNote))
            : GetVisibleBounds(Binding.NormalNote);
        var NoteBounds = GetVisibleBounds(OwnedContent.NoteRect);
        var LogoBounds = GetVisibleBounds((RectTransform)OwnedContent.LogoRoot.transform);
        var BobTravelInNoteSpace = Binding.NoteParent.InverseTransformVector(
            OwnedContent.FoundationLogo.transform.TransformVector(Vector3.up * BobTravel)).y;
        var Offset = new Vector2(
            OfficialBounds.xMin - NoteGap - NoteBounds.xMax + NoteHorizontalOffset,
            LogoBounds.yMax + Math.Max(0f, BobTravelInNoteSpace)
                + NoteLogoGap - NoteBounds.yMin);
        if (Offset.sqrMagnitude > 0.0001f)
            OwnedContent.NoteRect.anchoredPosition += Offset;
    }

    void OnRectTransformDimensionsChange()
    {
        if (Initialized) Reflow();
    }

    Rect GetVisibleBounds(RectTransform Root)
    {
        Graphics.Clear();
        Root.GetComponentsInChildren(false, Graphics);
        var Bounds = default(Rect);
        var Found = false;
        foreach (var Graphic in Graphics)
        {
            if (!Graphic.enabled || Graphic.color.a <= 0.001f) continue;
            var GraphicBounds = GetLocalBounds(Graphic.rectTransform);
            Bounds = Found ? Union(Bounds, GraphicBounds) : GraphicBounds;
            Found = true;
        }
        return Found ? Bounds : GetLocalBounds(Root);
    }

    Rect GetLocalBounds(RectTransform TargetRect)
    {
        TargetRect.GetWorldCorners(Corners);
        var Minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var Maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var Index = 0; Index < Corners.Length; Index++)
        {
            var Point = Binding.NoteParent.InverseTransformPoint(Corners[Index]);
            Minimum = Vector2.Min(Minimum, Point);
            Maximum = Vector2.Max(Maximum, Point);
        }
        return Rect.MinMaxRect(Minimum.x, Minimum.y, Maximum.x, Maximum.y);
    }

    static Rect Union(Rect First, Rect Second) => Rect.MinMaxRect(
        Math.Min(First.xMin, Second.xMin), Math.Min(First.yMin, Second.yMin),
        Math.Max(First.xMax, Second.xMax), Math.Max(First.yMax, Second.yMax));

    void OnDisable()
    {
        if (!Initialized || Content is not { } OwnedContent) return;
        OwnedContent.LandfallLogo.SetOffset(0f);
        OwnedContent.AggroCrabLogo.SetOffset(0f);
        OwnedContent.FoundationLogo.SetOffset(0f);
    }

    void OnDestroy() => Dispose();
}

internal sealed class MainMenuDeveloperLogoBob : BaseMeshEffect
{
    float Offset;

    internal void SetOffset(float Value)
    {
        if (Mathf.Approximately(Offset, Value)) return;
        Offset = Value;
        graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper VertexHelper)
    {
        if (!IsActive() || Mathf.Approximately(Offset, 0f)) return;
        var Vertex = default(UIVertex);
        for (var Index = 0; Index < VertexHelper.currentVertCount; Index++)
        {
            VertexHelper.PopulateUIVertex(ref Vertex, Index);
            Vertex.position.y += Offset;
            VertexHelper.SetUIVertex(Vertex, Index);
        }
    }
}
