using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Forens.Core.Collectors.Ntfs
{
    /// <summary>
    /// Opens a Windows volume (e.g. \\.\C:) for raw read access via CreateFile,
    /// then exposes USN journal queries (FSCTL_QUERY_USN_JOURNAL) and MFT
    /// enumeration (FSCTL_ENUM_USN_DATA). Disposable — releases the SafeFileHandle.
    /// </summary>
    public sealed class NtfsVolumeReader : IDisposable
    {
        private SafeFileHandle _handle;
        public string VolumePath { get; }

        private NtfsVolumeReader(SafeFileHandle handle, string volumePath)
        {
            _handle = handle;
            VolumePath = volumePath;
        }

        public static NtfsVolumeReader Open(string volumePath)
        {
            if (string.IsNullOrEmpty(volumePath)) throw new ArgumentException("volumePath required", nameof(volumePath));

            var handle = NtfsApi.CreateFile(
                volumePath,
                NtfsApi.GENERIC_READ,
                NtfsApi.FILE_SHARE_READ | NtfsApi.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NtfsApi.OPEN_EXISTING,
                NtfsApi.FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                throw new Win32Exception(err, "CreateFile " + volumePath + " failed (Win32 error " + err + ")");
            }
            return new NtfsVolumeReader(handle, volumePath);
        }

        public UsnJournalData QueryUsnJournal()
        {
            const int outSize = 80; // V2 size; we parse only the V0 prefix.
            IntPtr outBuf = Marshal.AllocHGlobal(outSize);
            try
            {
                uint bytes;
                bool ok = NtfsApi.DeviceIoControl(
                    _handle, NtfsApi.FSCTL_QUERY_USN_JOURNAL,
                    IntPtr.Zero, 0, outBuf, (uint)outSize, out bytes, IntPtr.Zero);
                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    throw new Win32Exception(err, "FSCTL_QUERY_USN_JOURNAL failed (Win32 error " + err + ")");
                }
                if (bytes < 56) throw new InvalidOperationException("USN_JOURNAL_DATA returned " + bytes + " bytes (need >= 56)");
                byte[] managed = new byte[bytes];
                Marshal.Copy(outBuf, managed, 0, (int)bytes);
                return UsnJournalData.FromBytes(managed);
            }
            finally { Marshal.FreeHGlobal(outBuf); }
        }

        /// <summary>
        /// Enumerates every current MFT entry via FSCTL_ENUM_USN_DATA. Yields one
        /// <see cref="UsnRecordV2"/> per file currently present in the MFT (each
        /// record carries the file's most-recent USN and timestamp).
        /// </summary>
        public IEnumerable<UsnRecordV2> EnumUsnData(long lowUsn, long highUsn, int outBufferSize = 1024 * 1024)
        {
            ulong nextStart = 0;
            IntPtr inBuf = Marshal.AllocHGlobal(24);
            IntPtr outBuf = Marshal.AllocHGlobal(outBufferSize);
            try
            {
                while (true)
                {
                    // MFT_ENUM_DATA_V0 input (24 bytes): StartFRN(8) + LowUsn(8) + HighUsn(8)
                    Marshal.WriteInt64(inBuf, 0, (long)nextStart);
                    Marshal.WriteInt64(inBuf, 8, lowUsn);
                    Marshal.WriteInt64(inBuf, 16, highUsn);

                    uint bytes;
                    bool ok = NtfsApi.DeviceIoControl(
                        _handle, NtfsApi.FSCTL_ENUM_USN_DATA,
                        inBuf, 24, outBuf, (uint)outBufferSize, out bytes, IntPtr.Zero);
                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err == NtfsApi.ERROR_HANDLE_EOF || err == NtfsApi.ERROR_NO_MORE_ITEMS) yield break;
                        throw new Win32Exception(err, "FSCTL_ENUM_USN_DATA failed (Win32 error " + err + ")");
                    }
                    if (bytes <= 8) yield break;

                    byte[] managed = new byte[bytes];
                    Marshal.Copy(outBuf, managed, 0, (int)bytes);

                    // First 8 bytes are the next StartFileReferenceNumber.
                    nextStart = BitConverter.ToUInt64(managed, 0);

                    int pos = 8;
                    while (pos + 60 <= managed.Length)
                    {
                        uint recordLen = BitConverter.ToUInt32(managed, pos);
                        if (recordLen == 0 || pos + recordLen > managed.Length) break;
                        UsnRecordV2 rec;
                        try { rec = UsnRecordParser.Parse(managed, pos); }
                        catch { rec = null; }
                        pos += (int)recordLen;
                        if (rec != null) yield return rec;
                    }

                    if (nextStart == 0) yield break;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(inBuf);
                Marshal.FreeHGlobal(outBuf);
            }
        }

        public void Dispose()
        {
            var h = System.Threading.Interlocked.Exchange(ref _handle, null);
            if (h != null) h.Dispose();
        }
    }
}
