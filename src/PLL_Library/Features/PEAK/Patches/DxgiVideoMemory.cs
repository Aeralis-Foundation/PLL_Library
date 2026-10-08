using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PLL.Library;

internal static class DxgiVideoMemory
{
    const int AdapterNotFound = unchecked((int)0x887A0002);
    const uint RemoteAdapter = 1;
    const uint SoftwareAdapter = 2;

    internal readonly struct Snapshot
    {
        internal Snapshot(ulong Budget, ulong CurrentUsage)
        {
            this.Budget = Budget;
            this.CurrentUsage = CurrentUsage;
        }

        internal ulong Budget { get; }
        internal ulong CurrentUsage { get; }
    }

    internal sealed class LocalBudget : IDisposable
    {
        Adapter1? Adapter;

        internal LocalBudget(Adapter1 Adapter, ulong NominalBytes)
        {
            this.Adapter = Adapter;
            this.NominalBytes = NominalBytes;
        }

        internal ulong NominalBytes { get; }

        internal bool TryQuery(out Snapshot Snapshot)
        {
            Snapshot = default;
            if (Adapter is not Adapter3 Adapter3
                || Adapter3.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local, out var Information) < 0
                || Information.Budget == 0) return false;
            Snapshot = new Snapshot(Information.Budget, Information.CurrentUsage);
            return true;
        }

        public void Dispose()
        {
            if (Adapter is null) return;
            Marshal.FinalReleaseComObject(Adapter);
            Adapter = null;
        }
    }

    sealed class Candidate
    {
        internal Candidate(Adapter1 Adapter, AdapterDescription1 Description)
        {
            this.Adapter = Adapter;
            this.Description = Description;
        }

        internal Adapter1 Adapter { get; }
        internal AdapterDescription1 Description { get; }
    }

    internal static LocalBudget? Open(uint VendorId, uint DeviceId, string Description)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            || VendorId == 0 || DeviceId == 0) return null;
        try { return OpenWindows(VendorId, DeviceId, Description); }
        catch (Exception) { return null; }
    }

    static LocalBudget? OpenWindows(uint VendorId, uint DeviceId, string Description)
    {
        Factory1? Factory = null;
        var Candidates = new List<Candidate>(2);
        Candidate? Selected = null;
        Candidate? Transferred = null;
        try
        {
            var InterfaceId = typeof(Factory1).GUID;
            if (CreateDXGIFactory1(ref InterfaceId, out Factory) < 0 || Factory is null) return null;
            for (uint AdapterIndex = 0; ; AdapterIndex++)
            {
                Adapter1? Adapter = null;
                try
                {
                    var EnumerationResult = Factory.EnumAdapters1(AdapterIndex, out Adapter);
                    if (EnumerationResult == AdapterNotFound) break;
                    if (EnumerationResult < 0 || Adapter is null
                        || Adapter.GetDesc1(out var AdapterDescription) < 0) return null;
                    if ((AdapterDescription.Flags & (RemoteAdapter | SoftwareAdapter)) != 0
                        || AdapterDescription.VendorId != VendorId
                        || AdapterDescription.DeviceId != DeviceId
                        || AdapterDescription.DedicatedVideoMemory.ToUInt64() == 0) continue;
                    Candidates.Add(new Candidate(Adapter, AdapterDescription));
                    Adapter = null;
                }
                finally
                {
                    if (Adapter is not null) Marshal.FinalReleaseComObject(Adapter);
                }
            }

            if (Candidates.Count == 1) Selected = Candidates[0];
            else
            {
                foreach (var Candidate in Candidates)
                {
                    if (!DescriptionsMatch(Description, Candidate.Description.Description)) continue;
                    if (Selected is not null) return null;
                    Selected = Candidate;
                }
            }
            if (Selected is null) return null;
            Transferred = Selected;
            return new LocalBudget(
                Selected.Adapter,
                Selected.Description.DedicatedVideoMemory.ToUInt64());
        }
        finally
        {
            foreach (var Candidate in Candidates)
                if (!ReferenceEquals(Candidate, Transferred))
                    Marshal.FinalReleaseComObject(Candidate.Adapter);
            if (Factory is not null) Marshal.FinalReleaseComObject(Factory);
        }
    }

    static bool DescriptionsMatch(string Expected, string Actual) =>
        !string.IsNullOrWhiteSpace(Expected) && !string.IsNullOrWhiteSpace(Actual)
        && string.Equals(Expected.Trim(), Actual.Trim(), StringComparison.OrdinalIgnoreCase);

    [DllImport("dxgi.dll", ExactSpelling = true)]
    static extern int CreateDXGIFactory1(
        ref Guid InterfaceId,
        [MarshalAs(UnmanagedType.Interface)] out Factory1 Factory);

    [ComImport]
    [Guid("770AAE78-F26F-4DBA-A829-253C83D1B387")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface Factory1
    {
        [PreserveSig]
        int SetPrivateData(ref Guid Name, uint DataSize, IntPtr Data);

        [PreserveSig]
        int SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object Unknown);

        [PreserveSig]
        int GetPrivateData(ref Guid Name, ref uint DataSize, IntPtr Data);

        [PreserveSig]
        int GetParent(ref Guid InterfaceId, out IntPtr Parent);

        [PreserveSig]
        int EnumAdapters(uint AdapterIndex, out IntPtr Adapter);

        [PreserveSig]
        int MakeWindowAssociation(IntPtr Window, uint Flags);

        [PreserveSig]
        int GetWindowAssociation(out IntPtr Window);

        [PreserveSig]
        int CreateSwapChain(
            [MarshalAs(UnmanagedType.IUnknown)] object Device,
            IntPtr Description,
            out IntPtr SwapChain);

        [PreserveSig]
        int CreateSoftwareAdapter(IntPtr Module, out IntPtr Adapter);

        [PreserveSig]
        int EnumAdapters1(
            uint AdapterIndex,
            [MarshalAs(UnmanagedType.Interface)] out Adapter1 Adapter);

        [PreserveSig]
        [return: MarshalAs(UnmanagedType.Bool)]
        bool IsCurrent();
    }

    [ComImport]
    [Guid("29038F61-3839-4626-91FD-086879011A05")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface Adapter1
    {
        [PreserveSig]
        int SetPrivateData(ref Guid Name, uint DataSize, IntPtr Data);

        [PreserveSig]
        int SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object Unknown);

        [PreserveSig]
        int GetPrivateData(ref Guid Name, ref uint DataSize, IntPtr Data);

        [PreserveSig]
        int GetParent(ref Guid InterfaceId, out IntPtr Parent);

        [PreserveSig]
        int EnumOutputs(uint OutputIndex, out IntPtr Output);

        [PreserveSig]
        int GetDesc(out AdapterDescription Description);

        [PreserveSig]
        int CheckInterfaceSupport(ref Guid InterfaceId, out long UserModeDriverVersion);

        [PreserveSig]
        int GetDesc1(out AdapterDescription1 Description);
    }

    [ComImport]
    [Guid("645967A4-1392-4310-A798-8053CE3E93FD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface Adapter3
    {
        [PreserveSig]
        int SetPrivateData(ref Guid Name, uint DataSize, IntPtr Data);

        [PreserveSig]
        int SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object Unknown);

        [PreserveSig]
        int GetPrivateData(ref Guid Name, ref uint DataSize, IntPtr Data);

        [PreserveSig]
        int GetParent(ref Guid InterfaceId, out IntPtr Parent);

        [PreserveSig]
        int EnumOutputs(uint OutputIndex, out IntPtr Output);

        [PreserveSig]
        int GetDesc(out AdapterDescription Description);

        [PreserveSig]
        int CheckInterfaceSupport(ref Guid InterfaceId, out long UserModeDriverVersion);

        [PreserveSig]
        int GetDesc1(out AdapterDescription1 Description);

        [PreserveSig]
        int GetDesc2(out AdapterDescription2 Description);

        [PreserveSig]
        int RegisterHardwareContentProtectionTeardownStatusEvent(IntPtr Event, out uint Cookie);

        void UnregisterHardwareContentProtectionTeardownStatus(uint Cookie);

        [PreserveSig]
        int QueryVideoMemoryInfo(
            uint NodeIndex,
            MemorySegmentGroup MemorySegmentGroup,
            out QueryVideoMemoryInformation Information);

        [PreserveSig]
        int SetVideoMemoryReservation(
            uint NodeIndex,
            MemorySegmentGroup MemorySegmentGroup,
            ulong Reservation);

        [PreserveSig]
        int RegisterVideoMemoryBudgetChangeNotificationEvent(IntPtr Event, out uint Cookie);

        void UnregisterVideoMemoryBudgetChangeNotification(uint Cookie);
    }
    enum MemorySegmentGroup
    {
        Local,
        NonLocal
    }

    [StructLayout(LayoutKind.Sequential)]
    struct QueryVideoMemoryInformation
    {
        internal ulong Budget;
        internal ulong CurrentUsage;
        internal ulong AvailableForReservation;
        internal ulong CurrentReservation;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string Description;
        internal uint VendorId;
        internal uint DeviceId;
        internal uint SubsystemId;
        internal uint Revision;
        internal UIntPtr DedicatedVideoMemory;
        internal UIntPtr DedicatedSystemMemory;
        internal UIntPtr SharedSystemMemory;
        internal uint AdapterLuidLow;
        internal int AdapterLuidHigh;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct AdapterDescription1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string Description;
        internal uint VendorId;
        internal uint DeviceId;
        internal uint SubsystemId;
        internal uint Revision;
        internal UIntPtr DedicatedVideoMemory;
        internal UIntPtr DedicatedSystemMemory;
        internal UIntPtr SharedSystemMemory;
        internal uint AdapterLuidLow;
        internal int AdapterLuidHigh;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct AdapterDescription2
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string Description;
        internal uint VendorId;
        internal uint DeviceId;
        internal uint SubsystemId;
        internal uint Revision;
        internal UIntPtr DedicatedVideoMemory;
        internal UIntPtr DedicatedSystemMemory;
        internal UIntPtr SharedSystemMemory;
        internal uint AdapterLuidLow;
        internal int AdapterLuidHigh;
        internal uint Flags;
        internal uint GraphicsPreemptionGranularity;
        internal uint ComputePreemptionGranularity;
    }
}
