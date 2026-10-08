using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zorro.ControllerSupport;
using Zorro.Settings;

namespace PLL.Library;

internal static class TerminalLayoutSelector
{
    static readonly HashSet<BoardingPassLayoutSelector> Controls = new();

    internal static void Attach(BoardingPass boardingPass)
    {
        if (!boardingPass) throw new ArgumentNullException(nameof(boardingPass));
        var control = boardingPass.GetComponent<BoardingPassLayoutSelector>();
        if (!control) control = boardingPass.gameObject.AddComponent<BoardingPassLayoutSelector>();
        var initialized = false;
        try
        {
            initialized = control.TryInitialize(boardingPass, out var failure);
            if (!initialized)
            {
                Plugin.PLLLog.LogError(failure);
                return;
            }
            Controls.Add(control);
        }
        finally
        {
            if (!initialized)
            {
                Controls.Remove(control);
                control.Dispose();
                UnityEngine.Object.Destroy(control);
            }
        }
    }

    internal static void Unregister(BoardingPassLayoutSelector control) => Controls.Remove(control);

    internal static void Shutdown()
    {
        var controls = new List<BoardingPassLayoutSelector>(Controls);
        Controls.Clear();
        foreach (var control in controls)
            if (control) control.Dispose();
    }

    [HarmonyPatch(typeof(BoardingPass), "Initialize")]
    static class InitializePatch
    {
        [HarmonyPostfix]
        static void Postfix(BoardingPass __instance) => Attach(__instance);
    }

    [HarmonyPatch(typeof(BoardingPass), "UpdateAscent")]
    static class UpdateAscentPatch
    {
        [HarmonyPostfix]
        static void Postfix(BoardingPass __instance)
        {
            var control = __instance.GetComponent<BoardingPassLayoutSelector>();
            if (control) control.Relayout(__instance);
        }
    }
}

internal sealed class BoardingPassLayoutSelector : MonoBehaviour
{
    const int VisibleOptions = 6;
    const string OrderLabel = "ORDER";
    const float Spacing = 8f;
    const float TextInset = 12f;
    const float ContentPadding = 2f;
    const float DropdownBorderWidth = 2f;
    GameObject? control;
    TMP_Dropdown? dropdown;
    Image? dropdownImage;
    Toggle? orderToggle;
    LayoutId[] options = Array.Empty<LayoutId>();
    int libraryBaseIndex = -1;
    Color defaultDropdownColor;
    Color selectedDropdownColor;
    bool subscribed;
    float RowHeight;
    float MeasuredContentWidth;
    float MeasuredOrderLabelWidth;
    float ScrollbarColumnWidth;
    float ToggleWidth => RowHeight + Spacing * 2f + MeasuredOrderLabelWidth;

    internal bool TryInitialize(BoardingPass boardingPass, out string failure)
    {
        if (dropdown && orderToggle)
        {
            failure = string.Empty;
            return true;
        }

        var titleRect = boardingPass.ascentTitle
            ? boardingPass.ascentTitle.rectTransform
            : null;
        var description = boardingPass.ascentDesc;
        var descriptionRect = description
            ? description.rectTransform
            : null;
        var rewardRect = boardingPass.reward
            ? boardingPass.reward.transform as RectTransform
            : null;
        var customOptionsRect = boardingPass.customOptionsButton
            ? boardingPass.customOptionsButton.transform as RectTransform
            : null;
        var contentPanel = rewardRect ? rewardRect.parent as RectTransform : null;
        if (!titleRect || description is null || !descriptionRect || !rewardRect || !customOptionsRect || !contentPanel
            || !titleRect.IsChildOf(contentPanel) || !descriptionRect.IsChildOf(contentPanel)
            || !customOptionsRect.IsChildOf(contentPanel)
            || (contentPanel != boardingPass.transform
                && !contentPanel.IsChildOf(boardingPass.transform)))
        {
            failure = "PLL Library could not add the boarding-pass layout controls because PEAK's boarding-pass content panel is unavailable.";
            return false;
        }

        var mapper = InputCellMapper.Instance;
        var nativeDropdown = mapper && mapper.EnumSettingCell
            ? mapper.EnumSettingCell.GetComponentInChildren<TMP_Dropdown>(true)
            : null;
        var NativeTemplate = nativeDropdown ? nativeDropdown.template : null;
        var NativeScrollRect = NativeTemplate ? NativeTemplate.GetComponent<ScrollRect>() : null;
        var NativeScrollbar = NativeScrollRect ? NativeScrollRect.verticalScrollbar : null;
        var NativeScrollbarRect = NativeScrollbar ? NativeScrollbar.transform as RectTransform : null;
        var nativeMulti = boardingPass.customOptionsWindow
            ? boardingPass.customOptionsWindow.GetComponentInChildren<CustomOptionMulti>(true)
            : null;
        var nativeMultiButton = nativeMulti ? nativeMulti.button : null;
        var nativeMultiImage = nativeMulti ? nativeMulti.buttonImage : null;
        var nativeMultiText = nativeMulti && nativeMulti.buttonText
            ? nativeMulti.buttonText.tmp
            : null;
        var nativeMultiButtonRect = nativeMultiButton
            ? nativeMultiButton.transform as RectTransform
            : null;
        var toggleTemplate = boardingPass.customOptionsWindow
            ? boardingPass.customOptionsWindow.customOptionItemTogglePrefab
            : null;
        var nativeToggle = toggleTemplate ? toggleTemplate.toggle : null;
        var nativeCheck = nativeToggle ? nativeToggle.graphic as Image : null;
        var nativeBox = nativeCheck ? nativeCheck.transform.parent.GetComponent<Image>() : null;
        if (!nativeDropdown || !NativeTemplate || !NativeScrollRect || !NativeScrollbar
            || !NativeScrollbarRect || !nativeMulti || !nativeMultiButton || !nativeMultiImage
            || !nativeMultiText || !nativeMultiButtonRect || !nativeToggle || !nativeCheck
            || !nativeBox)
        {
            failure = "PLL Library could not add the boarding-pass layout controls because PEAK's native selector or toggle template is unavailable.";
            return false;
        }

        if (!TryFindContentSlot(
                contentPanel,
                titleRect,
                description,
                rewardRect,
                customOptionsRect,
                out var contentSlot))
        {
            failure = "PLL Library could not add the boarding-pass layout controls because PEAK's boarding-pass content panel has no usable control slot.";
            return false;
        }

        var layouts = LayoutRegistry.Snapshot();
        var hasLibraryBase = TerminalLayoutSelection.CanSelectLibraryBase(FileManager.LayoutOrder.Value, FileManager.DoOrder.Value);
        var firstLayoutIndex = hasLibraryBase ? 2 : 1;
        options = new LayoutId[layouts.Count + firstLayoutIndex];
        options[0] = LayoutId.Default;
        var labels = new List<string>(options.Length) { "Default" };
        libraryBaseIndex = -1;
        if (hasLibraryBase)
        {
            libraryBaseIndex = 1;
            options[libraryBaseIndex] = LayoutId.Default;
            labels.Add("Library Base");
        }
        for (var i = 0; i < layouts.Count; i++)
        {
            options[i + firstLayoutIndex] = layouts[i].Id;
            labels.Add(layouts[i].DisplayName);
        }

        RowHeight = nativeMultiButtonRect.rect.height;
        var NativeRichText = nativeMultiText.richText;
        nativeMultiText.richText = false;
        try
        {
            MeasuredContentWidth = 0f;
            foreach (var Label in labels)
                MeasuredContentWidth = Math.Max(MeasuredContentWidth, nativeMultiText.GetPreferredValues(Label).x);
            MeasuredOrderLabelWidth = nativeMultiText.GetPreferredValues(OrderLabel).x;
        }
        finally
        {
            nativeMultiText.richText = NativeRichText;
        }
        ScrollbarColumnWidth = options.Length > VisibleOptions
            ? NativeScrollbarRect.rect.width + NativeScrollRect.verticalScrollbarSpacing
            : 0f;
        if (!TryCalculateControlBounds(contentPanel, contentSlot, out var ControlBounds))
        {
            failure = "PLL Library could not add the boarding-pass layout controls because PEAK's boarding-pass content panel has no usable control slot.";
            return false;
        }

        control = new GameObject("PLL Layout Controls", typeof(RectTransform));
        control.SetActive(false);
        control.transform.SetParent(contentPanel, false);
        var controlRect = (RectTransform)control.transform;
        controlRect.anchorMin = controlRect.anchorMax = new Vector2(0f, 0.5f);
        controlRect.pivot = new Vector2(0f, 0.5f);
        controlRect.localRotation = Quaternion.identity;
        controlRect.localScale = Vector3.one;
        controlRect.SetAsLastSibling();
        var background = CreateImage("Background", control.transform, nativeMultiImage);
        Stretch(background.rectTransform);
        background.color = nativeMulti.defaultColor;
        background.raycastTarget = false;

        var dropdownObject = Instantiate(nativeDropdown.gameObject, control.transform, false);
        dropdownObject.name = "Layout";
        var dropdownRect = (RectTransform)dropdownObject.transform;
        dropdownRect.anchorMin = dropdownRect.anchorMax = new Vector2(0f, 0.5f);
        dropdownRect.pivot = new Vector2(0f, 0.5f);
        dropdownRect.localRotation = Quaternion.identity;
        dropdownRect.localScale = Vector3.one;

        var createdDropdown = dropdownObject.GetComponent<TMP_Dropdown>();
        var caption = createdDropdown ? createdDropdown.captionText : null;
        var itemText = createdDropdown ? createdDropdown.itemText : null;
        var template = createdDropdown ? createdDropdown.template : null;
        var templateImage = template ? template.GetComponent<Image>() : null;
        var item = itemText ? itemText.transform.parent as RectTransform : null;
        var itemToggle = item ? item.GetComponent<Toggle>() : null;
        var scrollRect = template ? template.GetComponent<ScrollRect>() : null;
        var scrollbar = scrollRect ? scrollRect.verticalScrollbar : null;
        var scrollbarImage = scrollbar ? scrollbar.GetComponent<Image>() : null;
        var scrollbarHandleImage = scrollbar ? scrollbar.targetGraphic as Image : null;
        var targetImage = createdDropdown ? createdDropdown.targetGraphic as Image : null;
        var itemImage = itemToggle ? itemToggle.targetGraphic as Image : null;
        var itemCheck = itemToggle ? itemToggle.graphic as Image : null;
        var arrow = dropdownObject.transform.Find("Arrow")?.GetComponent<Image>();
        if (!createdDropdown || !caption || !itemText || !template || !templateImage
            || !item || !itemToggle || !scrollRect || !scrollbar || !scrollbarImage
            || !scrollbarHandleImage || !targetImage || !itemImage || !itemCheck || !arrow
            || !template.GetComponent<ScrollRectAutoScroller>()
            || !item.GetComponent<ScrollRectAutoScrollerElement>())
        {
            failure = "PLL Library could not add the boarding-pass layout selector because PEAK's native dropdown is incomplete.";
            return false;
        }
        var itemHeight = item.rect.height;
        var availableListHeight = contentPanel.rect.yMax - ControlBounds.yMax;
        if (itemHeight <= 0f || availableListHeight < itemHeight)
        {
            failure = "PLL Library could not add the boarding-pass layout selector because the boarding pass has no usable list viewport.";
            return false;
        }
        var IndicatorColumnWidth = RowHeight;

        targetImage.enabled = false;
        AddHitTarget(dropdownObject.transform);
        AddBorder(dropdownObject.transform, nativeMultiText.color, DropdownBorderWidth);
        createdDropdown.targetGraphic = null;
        CopyText(caption, nativeMultiText);
        caption.richText = false;
        caption.gameObject.SetActive(true);
        caption.enabled = true;
        caption.color = nativeMultiText.color;
        caption.horizontalAlignment = HorizontalAlignmentOptions.Left;
        SetTextColumn(caption.rectTransform, IndicatorColumnWidth);
        createdDropdown.transition = Selectable.Transition.None;
        createdDropdown.colors = nativeMultiButton.colors;
        createdDropdown.spriteState = nativeMultiButton.spriteState;
        createdDropdown.animationTriggers = nativeMultiButton.animationTriggers;
        createdDropdown.navigation = nativeMultiButton.navigation;
        arrow.color = nativeMultiText.color;
        var ArrowRect = arrow.rectTransform;
        var ArrowSize = ArrowRect.rect.size;
        ArrowRect.anchorMin = ArrowRect.anchorMax = new Vector2(1f, 0.5f);
        ArrowRect.pivot = new Vector2(0.5f, 0.5f);
        ArrowRect.anchoredPosition = new Vector2(-Spacing - IndicatorColumnWidth * 0.5f, 0f);
        ArrowRect.sizeDelta = ArrowSize;
        templateImage.color = nativeMultiText.color;
        AddBorder(template, nativeMulti.defaultColor, DropdownBorderWidth);
        scrollbarImage.color = nativeMultiText.color;
        CopyImage(scrollbarHandleImage, nativeMultiImage);
        scrollbarHandleImage.color = nativeMulti.defaultColor;
        CopyImage(itemImage, nativeMultiImage);
        itemImage.color = nativeMulti.defaultColor;
        CopyText(itemText, nativeMultiText);
        itemText.richText = false;
        itemText.horizontalAlignment = HorizontalAlignmentOptions.Left;
        SetTextColumn(itemText.rectTransform, IndicatorColumnWidth);
        CopyImage(itemCheck, nativeCheck);
        itemCheck.color = nativeMultiText.color;
        SetCheckmarkGeometry(
            itemCheck.rectTransform, nativeCheck.rectTransform,
            new Vector2(1f, 0.5f),
            new Vector2(-Spacing - IndicatorColumnWidth * 0.5f, 0f),
            new Vector2(IndicatorColumnWidth, itemHeight));
        itemToggle.transition = nativeMultiButton.transition;
        itemToggle.colors = nativeMultiButton.colors;
        itemToggle.spriteState = nativeMultiButton.spriteState;
        itemToggle.animationTriggers = nativeMultiButton.animationTriggers;
        itemToggle.navigation = nativeMultiButton.navigation;

        template.anchorMin = template.anchorMax = new Vector2(0.5f, 1f);
        template.pivot = new Vector2(0.5f, 0f);
        template.anchoredPosition = Vector2.zero;
        caption.textWrappingMode = TextWrappingModes.NoWrap;
        caption.overflowMode = TextOverflowModes.Ellipsis;
        itemText.textWrappingMode = TextWrappingModes.NoWrap;
        itemText.overflowMode = TextOverflowModes.Ellipsis;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = itemHeight;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

        var createdToggle = CreateOrderToggle(
            control.transform,
            nativeToggle,
            nativeBox,
            nativeCheck,
            nativeMultiText,
            ToggleWidth,
            RowHeight);
        var toggleRect = (RectTransform)createdToggle.transform;
        toggleRect.anchorMin = toggleRect.anchorMax = new Vector2(0f, 0.5f);
        toggleRect.pivot = new Vector2(0f, 0.5f);
        toggleRect.localRotation = Quaternion.identity;
        toggleRect.localScale = Vector3.one;

        createdDropdown.ClearOptions();
        createdDropdown.AddOptions(labels);
        createdDropdown.SetValueWithoutNotify(IndexOf(TerminalLayoutSelection.SelectedId));
        SetLayer(control.transform, boardingPass.gameObject.layer);
        dropdown = createdDropdown;
        dropdownImage = background;
        defaultDropdownColor = nativeMulti.defaultColor;
        selectedDropdownColor = nativeMulti.adjustedColor;
        orderToggle = createdToggle;
        ApplyControlGeometry(contentPanel, ControlBounds);
        failure = string.Empty;
        if (isActiveAndEnabled) Subscribe();
        Refresh();
        return true;
    }

    internal void Relayout(BoardingPass boardingPass)
    {
        if (!control || !dropdown || !orderToggle) return;
        var contentPanel = (RectTransform)control.transform.parent;
        if (!TryFindContentSlot(
                contentPanel,
                boardingPass.ascentTitle.rectTransform,
                boardingPass.ascentDesc,
                (RectTransform)boardingPass.reward.transform,
                (RectTransform)boardingPass.customOptionsButton.transform,
                out var contentSlot)
            || !TryCalculateControlBounds(contentPanel, contentSlot, out var ControlBounds)) return;
        ApplyControlGeometry(contentPanel, ControlBounds);
    }

    internal void Dispose()
    {
        Unsubscribe();
        if (control)
        {
            control.SetActive(false);
            Destroy(control);
        }
        control = null;
        dropdown = null;
        dropdownImage = null;
        orderToggle = null;
        options = Array.Empty<LayoutId>();
        libraryBaseIndex = -1;
    }

    void OnEnable()
    {
        if (!dropdown || !orderToggle) return;
        Subscribe();
        Refresh();
    }

    void OnDisable()
    {
        if (dropdown) dropdown.Hide();
        Unsubscribe();
    }

    void OnDestroy()
    {
        Dispose();
        TerminalLayoutSelector.Unregister(this);
    }

    void Subscribe()
    {
        if (subscribed || !dropdown || !orderToggle) return;
        dropdown.onValueChanged.AddListener(Select);
        orderToggle.onValueChanged.AddListener(SetOrderEnabled);
        TerminalLayoutSelection.Changed += Refresh;
        subscribed = true;
    }

    void Unsubscribe()
    {
        if (!subscribed) return;
        if (dropdown) dropdown.onValueChanged.RemoveListener(Select);
        if (orderToggle) orderToggle.onValueChanged.RemoveListener(SetOrderEnabled);
        TerminalLayoutSelection.Changed -= Refresh;
        subscribed = false;
    }

    void Select(int index)
    {
        if (index == libraryBaseIndex
            && TerminalLayoutSelection.TrySelectLibraryBase(FileManager.LayoutOrder.Value, FileManager.DoOrder.Value)) return;
        if (index >= 0 && index < options.Length && TerminalLayoutSelection.TrySelect(options[index])) return;
        Refresh();
    }

    void SetOrderEnabled(bool enabled)
    {
        if (TerminalLayoutSelection.TrySetOrderEnabled(enabled, FileManager.DoOrder.Value)) return;
        Refresh();
    }

    void Refresh()
    {
        if (!dropdown || !dropdownImage || !orderToggle || !control) return;
        var canSelect = TerminalLayoutSelection.CanSelect;
        var orderEnabled = TerminalLayoutSelection.IsOrderEnabled(FileManager.DoOrder.Value);
        if (!canSelect && control.activeSelf) dropdown.Hide();
        dropdown.SetValueWithoutNotify(IndexOf(TerminalLayoutSelection.SelectedId));
        orderToggle.SetIsOnWithoutNotify(orderEnabled);
        dropdown.interactable = canSelect;
        orderToggle.interactable = canSelect && TerminalLayoutSelection.SelectedId.IsDefault;
        control.SetActive(canSelect);
        dropdownImage.color = TerminalLayoutSelection.UsesConfiguredSettings
            ? defaultDropdownColor
            : selectedDropdownColor;
    }

    int IndexOf(LayoutId id)
    {
        if (libraryBaseIndex >= 0 && TerminalLayoutSelection.IsLibraryBaseSelected)
            return libraryBaseIndex;
        for (var i = 0; i < options.Length; i++)
            if (i != libraryBaseIndex && options[i] == id) return i;
        return 0;
    }

    static bool TryFindContentSlot(
        RectTransform contentPanel,
        RectTransform title,
        TMP_Text description,
        RectTransform reward,
        RectTransform customOptions,
        out Rect slot)
    {
        var titleBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(contentPanel, title);
        var rewardBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(contentPanel, reward);
        var customOptionsBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(contentPanel, customOptions);
        description.ForceMeshUpdate(true, false);
        var descriptionBottom = contentPanel.InverseTransformPoint(
            description.transform.TransformPoint(description.textBounds.min)).y;
        var left = Math.Max(contentPanel.rect.xMin, titleBounds.min.x);
        var right = Math.Min(rewardBounds.min.x, customOptionsBounds.min.x) - Spacing;
        var bottom = contentPanel.rect.yMin + ContentPadding;
        var top = Math.Min(contentPanel.rect.yMax, descriptionBottom) - ContentPadding;
        slot = Rect.MinMaxRect(left, bottom, right, top);
        return slot.width > 0f && slot.height > 0f;
    }

    bool TryCalculateControlBounds(RectTransform ContentPanel, Rect ContentSlot, out Rect ControlBounds)
    {
        var PreferredDropdownWidth = MeasuredContentWidth + RowHeight + ScrollbarColumnWidth + TextInset * 2f;
        var AvailableDropdownWidth = ContentSlot.width - Spacing * 3f - ToggleWidth;
        var DropdownWidth = Math.Min(PreferredDropdownWidth, AvailableDropdownWidth);
        if (RowHeight <= 0f || DropdownWidth <= RowHeight + ScrollbarColumnWidth + TextInset * 2f)
        {
            ControlBounds = default;
            return false;
        }
        var ControlHeight = RowHeight + Spacing * 2f;
        var HalfControlHeight = ControlHeight * 0.5f;
        var BalancedPadding = ContentSlot.xMin - ContentPanel.rect.xMin;
        var ControlCenterY = Mathf.Clamp(
            ContentPanel.rect.yMin + BalancedPadding + HalfControlHeight,
            ContentSlot.yMin + HalfControlHeight,
            ContentSlot.yMax - HalfControlHeight);
        ControlBounds = new Rect(
            ContentSlot.xMin,
            ControlCenterY - ControlHeight * 0.5f,
            Spacing * 3f + DropdownWidth + ToggleWidth,
            ControlHeight);
        return true;
    }

    void ApplyControlGeometry(RectTransform ContentPanel, Rect ControlBounds)
    {
        var DropdownWidth = ControlBounds.width - Spacing * 3f - ToggleWidth;
        var ControlRect = (RectTransform)control!.transform;
        ControlRect.anchoredPosition = new Vector2(
            ControlBounds.xMin - ContentPanel.rect.xMin,
            ControlBounds.center.y - ContentPanel.rect.center.y);
        ControlRect.sizeDelta = ControlBounds.size;
        var DropdownRect = (RectTransform)dropdown!.transform;
        DropdownRect.anchoredPosition = new Vector2(Spacing, 0f);
        DropdownRect.sizeDelta = new Vector2(DropdownWidth, RowHeight);
        var ToggleRect = (RectTransform)orderToggle!.transform;
        ToggleRect.anchoredPosition = new Vector2(Spacing * 2f + DropdownWidth, 0f);
        ToggleRect.sizeDelta = new Vector2(ToggleWidth, RowHeight);
        var Item = (RectTransform)dropdown.itemText.transform.parent;
        var ListHeight = Math.Min(
            Item.rect.height * Math.Min(options.Length, VisibleOptions),
            ContentPanel.rect.yMax - ControlBounds.yMax);
        dropdown.template.sizeDelta = new Vector2(DropdownWidth, ListHeight);
    }

    static void SetTextColumn(RectTransform Value, float IndicatorColumnWidth)
    {
        var AnchorMin = Value.anchorMin;
        var AnchorMax = Value.anchorMax;
        var OffsetMin = Value.offsetMin;
        var OffsetMax = Value.offsetMax;
        Value.anchorMin = new Vector2(0f, AnchorMin.y);
        Value.anchorMax = new Vector2(1f, AnchorMax.y);
        Value.offsetMin = new Vector2(TextInset, OffsetMin.y);
        Value.offsetMax = new Vector2(-(TextInset + IndicatorColumnWidth), OffsetMax.y);
    }

    static void SetCheckmarkGeometry(
        RectTransform Value,
        RectTransform SourceCheck,
        Vector2 TargetAnchor,
        Vector2 TargetCenter,
        Vector2 TargetBoxSize)
    {
        var SourceCheckSize = SourceCheck.rect.size;
        var Scale = Math.Min(1f, Math.Min(TargetBoxSize.x / SourceCheckSize.x, TargetBoxSize.y / SourceCheckSize.y));
        Value.anchorMin = Value.anchorMax = TargetAnchor;
        Value.pivot = SourceCheck.pivot;
        Value.anchoredPosition = TargetCenter;
        Value.sizeDelta = SourceCheckSize * Scale;
        Value.localRotation = SourceCheck.localRotation;
        Value.localScale = SourceCheck.localScale;
    }

    static Toggle CreateOrderToggle(
        Transform parent,
        Toggle source,
        Image sourceBox,
        Image sourceCheck,
        TMP_Text sourceText,
        float width,
        float height)
    {
        var root = new GameObject("Layout Order", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        ((RectTransform)root.transform).sizeDelta = new Vector2(width, height);
        AddHitTarget(root.transform);
        var box = CreateImage("Toggle", root.transform, sourceBox);
        box.color = sourceText.color;
        var boxRect = box.rectTransform;
        boxRect.anchorMin = boxRect.anchorMax = new Vector2(0f, 0.5f);
        boxRect.pivot = new Vector2(0f, 0.5f);
        boxRect.anchoredPosition = Vector2.zero;
        boxRect.sizeDelta = new Vector2(height, height);
        var check = CreateImage("Checkmark", box.transform, sourceCheck);
        check.color = sourceText.color;
        SetCheckmarkGeometry(
            check.rectTransform, sourceCheck.rectTransform,
            new Vector2(0.5f, 0.5f), Vector2.zero, boxRect.rect.size);
        var labelObject = new GameObject("Label", typeof(RectTransform));
        labelObject.transform.SetParent(root.transform, false);
        var label = labelObject.AddComponent<TextMeshProUGUI>();
        CopyText(label, sourceText);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        label.rectTransform.offsetMin = new Vector2(height + Spacing, 0f);
        label.rectTransform.offsetMax = new Vector2(-Spacing, 0f);
        label.text = OrderLabel;

        var toggle = root.AddComponent<Toggle>();
        toggle.graphic = check;
        toggle.transition = Selectable.Transition.None;
        toggle.colors = source.colors;
        toggle.spriteState = source.spriteState;
        toggle.animationTriggers = source.animationTriggers;
        toggle.navigation = source.navigation;
        return toggle;
    }

    static void AddBorder(Transform parent, Color color, float width)
    {
        CreateBorderEdge(
            "Top Border", parent, color,
            new Vector2(0f, 1f), Vector2.one,
            new Vector2(0f, -width), Vector2.zero);
        CreateBorderEdge(
            "Bottom Border", parent, color,
            Vector2.zero, new Vector2(1f, 0f),
            Vector2.zero, new Vector2(0f, width));
        CreateBorderEdge(
            "Left Border", parent, color,
            Vector2.zero, new Vector2(0f, 1f),
            Vector2.zero, new Vector2(width, 0f));
        CreateBorderEdge(
            "Right Border", parent, color,
            new Vector2(1f, 0f), Vector2.one,
            new Vector2(-width, 0f), Vector2.zero);
    }

    static void CreateBorderEdge(
        string name,
        Transform parent,
        Color color,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        var edge = new GameObject(name, typeof(RectTransform), typeof(Image));
        edge.transform.SetParent(parent, false);
        var rect = (RectTransform)edge.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        var image = edge.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    static void AddHitTarget(Transform parent)
    {
        var target = new GameObject("Hit Target", typeof(RectTransform));
        target.transform.SetParent(parent, false);
        target.transform.SetAsFirstSibling();
        var image = target.AddComponent<Image>();
        image.color = Color.clear;
        image.raycastTarget = true;
        Stretch(image.rectTransform);
    }

    static Image CreateImage(string name, Transform parent, Image source)
    {
        var value = new GameObject(name, typeof(RectTransform));
        value.transform.SetParent(parent, false);
        var image = value.AddComponent<Image>();
        TerminalPresentation.CopyRect(image.rectTransform, source.rectTransform);
        CopyImage(image, source);
        return image;
    }

    static void Stretch(RectTransform value)
    {
        value.anchorMin = Vector2.zero;
        value.anchorMax = Vector2.one;
        value.pivot = new Vector2(0.5f, 0.5f);
        value.anchoredPosition = Vector2.zero;
        value.sizeDelta = Vector2.zero;
        value.localRotation = Quaternion.identity;
        value.localScale = Vector3.one;
    }

    static void SetLayer(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (var i = 0; i < root.childCount; i++) SetLayer(root.GetChild(i), layer);
    }

    static void CopyImage(Image target, Image source)
    {
        target.sprite = source.sprite;
        target.type = source.type;
        target.color = source.color;
        target.material = source.material;
        target.preserveAspect = source.preserveAspect;
        target.fillCenter = source.fillCenter;
        target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
        target.raycastTarget = source.raycastTarget;
        target.maskable = source.maskable;
    }

    static void CopyText(TMP_Text target, TMP_Text source)
    {
        target.font = source.font;
        target.fontSharedMaterial = source.fontSharedMaterial;
        target.fontSize = source.fontSize;
        target.fontStyle = source.fontStyle;
        target.color = source.color;
        target.alignment = source.alignment;
        target.enableAutoSizing = source.enableAutoSizing;
        target.fontSizeMin = source.fontSizeMin;
        target.fontSizeMax = source.fontSizeMax;
        target.characterWidthAdjustment = source.characterWidthAdjustment;
        target.textWrappingMode = TextWrappingModes.NoWrap;
        target.overflowMode = TextOverflowModes.Ellipsis;
        target.raycastTarget = false;
    }
}
