using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PLL.Library
{
    internal sealed class NativeContentRequestFile : IDisposable
    {
        const uint ReadAndDeleteAccess = 0x80010000;
        const uint OpenExisting = 3;
        const uint NormalFileNoReparseFollow = 0x00200080;
        const int FileRenameInfo = 3, FileDispositionInfo = 4;
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        readonly SafeFileHandle Handle;
        readonly FileStream Stream;

        NativeContentRequestFile(SafeFileHandle Handle)
        {
            this.Handle = Handle; Stream = new FileStream(Handle, FileAccess.Read);
        }
        internal static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        internal static NativeContentRequestFile Open(string Path, bool Exclusive)
        {
            if (!IsSupported)
                throw new PlatformNotSupportedException("Native content handoff requires Windows.");
            var Handle = CreateFileW(Path, ReadAndDeleteAccess, Exclusive ? 0u : 1u,
                IntPtr.Zero, OpenExisting, NormalFileNoReparseFollow, IntPtr.Zero);
            if (Handle.IsInvalid)
            {
                var Failure = FileFailure(); Handle.Dispose(); throw Failure;
            }
            try { return new NativeContentRequestFile(Handle); }
            catch { Handle.Dispose(); throw; }
        }

        internal string ReadText()
        {
            if (Stream.Length > NativeContentArtifact.MaximumRequestBytes)
                throw new InvalidOperationException("The native content request exceeds 128 KiB.");
            using (var Reader = new StreamReader(Stream, StrictUtf8, false, 4096, true))
                return Reader.ReadToEnd();
        }

        internal void CopyTo(Stream Destination)
        {
            Stream.Position = 0; Stream.CopyTo(Destination);
        }

        internal void RenameTo(string Destination)
        {
            var Name = Encoding.Unicode.GetBytes(System.IO.Path.GetFullPath(Destination));
            var Offset = Marshal.OffsetOf<RenameInformation>(nameof(RenameInformation.FileName)).ToInt32();
            var Size = Math.Max(Marshal.SizeOf<RenameInformation>(), checked(Offset + Name.Length + sizeof(char)));
            var Buffer = Marshal.AllocHGlobal(Size);
            try
            {
                Marshal.StructureToPtr(new RenameInformation
                {
                    Flags = 0, RootDirectory = IntPtr.Zero,
                    FileNameLength = (uint)Name.Length, FileName = '\0'
                }, Buffer, false);
                Marshal.Copy(Name, 0, IntPtr.Add(Buffer, Offset), Name.Length);
                Marshal.WriteInt16(IntPtr.Add(Buffer, Offset + Name.Length), 0);
                if (!SetFileInformationByHandle(Handle, FileRenameInfo, Buffer, (uint)Size))
                    throw FileFailure();
            }
            finally { Marshal.FreeHGlobal(Buffer); }
        }

        internal void DeleteAfterPublication()
        {
            var Buffer = Marshal.AllocHGlobal(1);
            try
            {
                Marshal.WriteByte(Buffer, 1);
                if (!SetFileInformationByHandle(Handle, FileDispositionInfo, Buffer, 1))
                    throw FileFailure();
            }
            finally { Marshal.FreeHGlobal(Buffer); }
        }

        static IOException FileFailure()
        {
            var HResult = Marshal.GetHRForLastWin32Error();
            return new IOException(new Win32Exception(HResult & 0xffff).Message, HResult);
        }

        public void Dispose()
        {
            try { Stream.Dispose(); }
            finally { Handle.Dispose(); }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct RenameInformation
        {
            internal uint Flags;
            internal IntPtr RootDirectory;
            internal uint FileNameLength;
            internal char FileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        static extern SafeFileHandle CreateFileW(string FileName, uint Access, uint Sharing,
            IntPtr Security, uint Creation, uint Attributes, IntPtr Template);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetFileInformationByHandle(SafeFileHandle Handle, int InformationClass,
            IntPtr Information, uint Size);
    }
}
