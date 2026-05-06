using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Forens.Core.Collectors.Ntfs
{
    /// <summary>
    /// P/Invoke surface for raw-volume NTFS operations: CreateFile on \\.\&lt;volume&gt;,
    /// DeviceIoControl with FSCTL_QUERY_USN_JOURNAL / FSCTL_ENUM_USN_DATA.
    /// </summary>
    internal static class NtfsApi
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

        // From winioctl.h
        // FSCTL_QUERY_USN_JOURNAL  = CTL_CODE(FILE_DEVICE_FILE_SYSTEM, 61, METHOD_BUFFERED, FILE_ANY_ACCESS) = 0x000900F4
        // FSCTL_ENUM_USN_DATA      = CTL_CODE(FILE_DEVICE_FILE_SYSTEM, 44, METHOD_NEITHER,  FILE_ANY_ACCESS) = 0x000900B3
        public const uint FSCTL_QUERY_USN_JOURNAL = 0x000900F4;
        public const uint FSCTL_ENUM_USN_DATA = 0x000900B3;

        public const int ERROR_HANDLE_EOF = 38;
        public const int ERROR_NO_MORE_ITEMS = 259;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);
    }

    /// <summary>USN journal metadata returned by FSCTL_QUERY_USN_JOURNAL (V0 prefix only).</summary>
    public sealed class UsnJournalData
    {
        public ulong UsnJournalId { get; set; }
        public long FirstUsn { get; set; }
        public long NextUsn { get; set; }
        public long LowestValidUsn { get; set; }
        public long MaxUsn { get; set; }
        public ulong MaximumSize { get; set; }
        public ulong AllocationDelta { get; set; }

        public static UsnJournalData FromBytes(byte[] data)
        {
            // USN_JOURNAL_DATA_V0 (56 bytes):
            //   ULONGLONG UsnJournalID; LONGLONG FirstUsn; LONGLONG NextUsn;
            //   LONGLONG LowestValidUsn; LONGLONG MaxUsn;
            //   DWORDLONG MaximumSize; DWORDLONG AllocationDelta;
            if (data == null || data.Length < 56)
                throw new ArgumentException("Buffer too short for USN_JOURNAL_DATA_V0", nameof(data));
            return new UsnJournalData
            {
                UsnJournalId = BitConverter.ToUInt64(data, 0),
                FirstUsn = BitConverter.ToInt64(data, 8),
                NextUsn = BitConverter.ToInt64(data, 16),
                LowestValidUsn = BitConverter.ToInt64(data, 24),
                MaxUsn = BitConverter.ToInt64(data, 32),
                MaximumSize = BitConverter.ToUInt64(data, 40),
                AllocationDelta = BitConverter.ToUInt64(data, 48)
            };
        }
    }

    /// <summary>One parsed USN_RECORD_V2 entry.</summary>
    public sealed class UsnRecordV2
    {
        public uint RecordLength { get; set; }
        public ushort MajorVersion { get; set; }
        public ushort MinorVersion { get; set; }
        public ulong FileReferenceNumber { get; set; }
        public ulong ParentFileReferenceNumber { get; set; }
        public long Usn { get; set; }
        public DateTimeOffset? TimeStampUtc { get; set; }
        public uint Reason { get; set; }
        public string ReasonDecoded { get; set; }
        public uint SourceInfo { get; set; }
        public uint SecurityId { get; set; }
        public uint FileAttributes { get; set; }
        public string FileAttributesDecoded { get; set; }
        public string FileName { get; set; }
    }

    internal struct FlagDef
    {
        public uint Bit;
        public string Name;
        public FlagDef(uint bit, string name) { Bit = bit; Name = name; }
    }

    public static class UsnRecordParser
    {
        // Reason flag bit names per winioctl.h
        private static readonly FlagDef[] ReasonFlags =
        {
            new FlagDef(0x00000001u, "DataOverwrite"),
            new FlagDef(0x00000002u, "DataExtend"),
            new FlagDef(0x00000004u, "DataTruncation"),
            new FlagDef(0x00000010u, "NamedDataOverwrite"),
            new FlagDef(0x00000020u, "NamedDataExtend"),
            new FlagDef(0x00000040u, "NamedDataTruncation"),
            new FlagDef(0x00000100u, "FileCreate"),
            new FlagDef(0x00000200u, "FileDelete"),
            new FlagDef(0x00000400u, "EaChange"),
            new FlagDef(0x00000800u, "SecurityChange"),
            new FlagDef(0x00001000u, "RenameOldName"),
            new FlagDef(0x00002000u, "RenameNewName"),
            new FlagDef(0x00004000u, "IndexableChange"),
            new FlagDef(0x00008000u, "BasicInfoChange"),
            new FlagDef(0x00010000u, "HardLinkChange"),
            new FlagDef(0x00020000u, "CompressionChange"),
            new FlagDef(0x00040000u, "EncryptionChange"),
            new FlagDef(0x00080000u, "ObjectIdChange"),
            new FlagDef(0x00100000u, "ReparsePointChange"),
            new FlagDef(0x00200000u, "StreamChange"),
            new FlagDef(0x00400000u, "TransactedChange"),
            new FlagDef(0x00800000u, "IntegrityChange"),
            new FlagDef(0x80000000u, "Close")
        };

        // FILE_ATTRIBUTE_* bits (subset most commonly seen)
        private static readonly FlagDef[] FileAttributeFlags =
        {
            new FlagDef(0x00000001u, "ReadOnly"),
            new FlagDef(0x00000002u, "Hidden"),
            new FlagDef(0x00000004u, "System"),
            new FlagDef(0x00000010u, "Directory"),
            new FlagDef(0x00000020u, "Archive"),
            new FlagDef(0x00000040u, "Device"),
            new FlagDef(0x00000080u, "Normal"),
            new FlagDef(0x00000100u, "Temporary"),
            new FlagDef(0x00000200u, "SparseFile"),
            new FlagDef(0x00000400u, "ReparsePoint"),
            new FlagDef(0x00000800u, "Compressed"),
            new FlagDef(0x00001000u, "Offline"),
            new FlagDef(0x00002000u, "NotContentIndexed"),
            new FlagDef(0x00004000u, "Encrypted"),
            new FlagDef(0x00008000u, "IntegrityStream"),
            new FlagDef(0x00010000u, "Virtual"),
            new FlagDef(0x00020000u, "NoScrubData"),
            new FlagDef(0x00040000u, "RecallOnOpen"),
            new FlagDef(0x00400000u, "RecallOnDataAccess")
        };

        /// <summary>
        /// Parse one USN_RECORD_V2 from <paramref name="buffer"/> starting at
        /// <paramref name="offset"/>. Returns the record or throws on malformed input.
        /// </summary>
        public static UsnRecordV2 Parse(byte[] buffer, int offset)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset + 60 > buffer.Length)
                throw new ArgumentException("Offset out of range or truncated record header", nameof(offset));

            uint recordLength = BitConverter.ToUInt32(buffer, offset + 0);
            ushort majorVer = BitConverter.ToUInt16(buffer, offset + 4);
            ushort minorVer = BitConverter.ToUInt16(buffer, offset + 6);
            ulong frn = BitConverter.ToUInt64(buffer, offset + 8);
            ulong parentFrn = BitConverter.ToUInt64(buffer, offset + 16);
            long usn = BitConverter.ToInt64(buffer, offset + 24);
            long ft = BitConverter.ToInt64(buffer, offset + 32);
            uint reason = BitConverter.ToUInt32(buffer, offset + 40);
            uint sourceInfo = BitConverter.ToUInt32(buffer, offset + 44);
            uint securityId = BitConverter.ToUInt32(buffer, offset + 48);
            uint fileAttributes = BitConverter.ToUInt32(buffer, offset + 52);
            ushort fileNameLen = BitConverter.ToUInt16(buffer, offset + 56);
            ushort fileNameOffset = BitConverter.ToUInt16(buffer, offset + 58);

            string filename = null;
            int nameStart = offset + fileNameOffset;
            if (fileNameLen > 0 && nameStart >= 0 && nameStart + fileNameLen <= buffer.Length)
            {
                try { filename = System.Text.Encoding.Unicode.GetString(buffer, nameStart, fileNameLen); }
                catch { filename = null; }
            }

            return new UsnRecordV2
            {
                RecordLength = recordLength,
                MajorVersion = majorVer,
                MinorVersion = minorVer,
                FileReferenceNumber = frn,
                ParentFileReferenceNumber = parentFrn,
                Usn = usn,
                TimeStampUtc = SafeFt(ft),
                Reason = reason,
                ReasonDecoded = DecodeFlags(reason, ReasonFlags),
                SourceInfo = sourceInfo,
                SecurityId = securityId,
                FileAttributes = fileAttributes,
                FileAttributesDecoded = DecodeFlags(fileAttributes, FileAttributeFlags),
                FileName = filename
            };
        }

        public static string DecodeReason(uint reason) => DecodeFlags(reason, ReasonFlags);
        public static string DecodeFileAttributes(uint attrs) => DecodeFlags(attrs, FileAttributeFlags);

        private static string DecodeFlags(uint value, FlagDef[] flags)
        {
            if (value == 0) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var f in flags)
            {
                if ((value & f.Bit) != 0)
                {
                    if (sb.Length > 0) sb.Append('|');
                    sb.Append(f.Name);
                }
            }
            return sb.ToString();
        }

        private static DateTimeOffset? SafeFt(long ft)
        {
            if (ft <= 0) return null;
            try { return DateTimeOffset.FromFileTime(ft).ToUniversalTime(); }
            catch { return null; }
        }
    }
}
