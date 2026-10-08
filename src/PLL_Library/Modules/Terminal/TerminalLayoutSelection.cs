using System;
using Peak.Network;

namespace PLL.Library;

internal readonly struct CapturedLayoutSelection
{
    internal CapturedLayoutSelection(LayoutId id, string rawOrder, bool doOrder, bool isLibraryBase = false)
    {
        if (isLibraryBase && (!id.IsDefault || !doOrder
            || !string.Equals(rawOrder, TerminalLayoutSelection.LibraryBaseOrder, StringComparison.Ordinal)))
            throw new ArgumentException(
                "Library Base requires the default layout ID, compiled default order, and ordering enabled.",
                nameof(isLibraryBase));
        Id = id;
        RawOrder = rawOrder;
        DoOrder = doOrder;
        IsLibraryBase = isLibraryBase;
    }

    internal LayoutId Id { get; }
    internal string RawOrder { get; }
    internal bool DoOrder { get; }
    internal bool IsLibraryBase { get; }
}

internal static class TerminalLayoutSelection
{
    static LayoutId selectedId = LayoutId.Default;
    static bool libraryBaseSelected;
    static bool? orderEnabledOverride;

    internal static event Action? Changed;

    internal static LayoutId SelectedId => selectedId;
    internal static bool CanSelect => NetCode.Session.IsOffline || NetCode.Session.IsHost;
    internal static string LibraryBaseOrder => (string)FileManager.LayoutOrder.DefaultValue;
    internal static bool UsesConfiguredSettings =>
        selectedId.IsDefault && !libraryBaseSelected && !orderEnabledOverride.HasValue;
    internal static bool IsLibraryBaseSelected => libraryBaseSelected;
    internal static bool IsOrderEnabled(bool configuredValue) => orderEnabledOverride ?? configuredValue;

    internal static CapturedLayoutSelection Capture(string configuredOrder, bool configuredDoOrder)
    {
        if (!CanSelect)
            throw new InvalidOperationException("PLL Library cannot capture a terminal layout selection without host authority.");
        return libraryBaseSelected
            ? Resolve(LayoutId.Default, configuredOrder, configuredDoOrder, true)
            : Resolve(selectedId, configuredOrder, IsOrderEnabled(configuredDoOrder));
    }

    internal static CapturedLayoutSelection Resolve(
        LayoutId id,
        string configuredOrder,
        bool configuredDoOrder,
        bool isLibraryBase = false)
    {
        if (!id.IsValid) throw new ArgumentException("A valid layout ID is required.", nameof(id));
        if (isLibraryBase)
        {
            if (!id.IsDefault)
                throw new ArgumentException("Library Base requires the default layout ID.", nameof(id));
            return new CapturedLayoutSelection(LayoutId.Default, LibraryBaseOrder, true, true);
        }
        if (id.IsDefault) return new CapturedLayoutSelection(id, configuredOrder, configuredDoOrder);
        if (!LayoutRegistry.TryGet(id, out var definition))
            throw new InvalidOperationException($"Named layout '{id}' is unavailable.");
        return new CapturedLayoutSelection(id, RawOrder(definition), true);
    }

    internal static bool CanSelectLibraryBase(string configuredOrder, bool configuredDoOrder) =>
        !string.Equals(configuredOrder, LibraryBaseOrder, StringComparison.Ordinal)
        || !configuredDoOrder || LayoutRegistry.HasDefaultModification();

    internal static bool TrySelect(LayoutId id) =>
        CanSelect && TrySet(id, false, id.IsDefault ? null : true);
    internal static bool TrySelectLibraryBase(string configuredOrder, bool configuredDoOrder) =>
        CanSelect && CanSelectLibraryBase(configuredOrder, configuredDoOrder)
            && TrySet(LayoutId.Default, true, true);

    internal static bool TrySetOrderEnabled(bool enabled, bool configuredValue)
    {
        if (!CanSelect || (!selectedId.IsDefault && !enabled)) return false;
        if (libraryBaseSelected)
            return enabled || TrySet(LayoutId.Default, false, enabled == configuredValue ? null : enabled);
        return selectedId.IsDefault
            ? TrySet(LayoutId.Default, false, enabled == configuredValue ? null : enabled)
            : true;
    }

    internal static void RefreshAuthority() => Changed?.Invoke();
    internal static void CompleteFlightCapture()
    {
        if (!libraryBaseSelected && !orderEnabledOverride.HasValue) return;
        libraryBaseSelected = false;
        orderEnabledOverride = null;
        Changed?.Invoke();
    }

    internal static void Shutdown()
    {
        Reset();
        Changed = null;
    }

    static bool TrySet(LayoutId id, bool libraryBase, bool? orderOverride)
    {
        if (!Valid(id)) return false;
        if (!id.IsDefault)
        {
            libraryBase = false;
            orderOverride = true;
        }
        else if (libraryBase) orderOverride = true;
        if (selectedId == id && libraryBaseSelected == libraryBase && orderEnabledOverride == orderOverride) return true;
        selectedId = id;
        libraryBaseSelected = libraryBase;
        orderEnabledOverride = orderOverride;
        Changed?.Invoke();
        return true;
    }

    static bool Valid(LayoutId id) =>
        id.IsValid && (id.IsDefault || LayoutRegistry.TryGet(id, out _));

    internal static void Reset()
    {
        selectedId = LayoutId.Default;
        libraryBaseSelected = false;
        orderEnabledOverride = null;
        Changed?.Invoke();
    }

    internal static string RawOrder(LayoutDefinition Definition) =>
        string.Join(" > ", Definition.SegmentTokens);
}
