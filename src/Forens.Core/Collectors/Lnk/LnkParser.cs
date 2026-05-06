using System;
using System.IO;
using System.Text;

namespace Forens.Core.Collectors.Lnk
{
    /// <summary>
    /// Parses Microsoft Shell-Link (.lnk) files per [MS-SHLLINK]. Extracts:
    /// header (LinkFlags, FileAttributes, target's creation/access/write
    /// FILETIME, target file size), LinkInfo's LocalBasePath (the "real"
    /// target absolute path), VolumeID drive metadata, and StringData
    /// entries for Name/RelativePath/WorkingDir/Arguments/IconLocation
    /// when their LinkFlag bits are set.
    /// </summary>
    public sealed class ParsedLnk
    {
        public bool HeaderValid { get; set; }
        public uint LinkFlags { get; set; }
        public uint FileAttributes { get; set; }
        public DateTimeOffset? TargetCreationUtc { get; set; }
        public DateTimeOffset? TargetAccessUtc { get; set; }
        public DateTimeOffset? TargetWriteUtc { get; set; }
        public uint TargetFileSize { get; set; }
        public int IconIndex { get; set; }
        public int ShowCommand { get; set; }

        public string LocalBasePath { get; set; }
        public string CommonPathSuffix { get; set; }
        public uint? DriveType { get; set; }
        public string DriveTypeName { get; set; }
        public uint? DriveSerialNumber { get; set; }
        public string VolumeLabel { get; set; }

        public string Name { get; set; }
        public string RelativePath { get; set; }
        public string WorkingDir { get; set; }
        public string Arguments { get; set; }
        public string IconLocation { get; set; }

        public string ParseError { get; set; }
    }

    public static class LnkParser
    {
        // ShellLinkHeader.LinkCLSID = 00021401-0000-0000-C000-000000000046 in mixed endian.
        private static readonly byte[] LinkCLSID =
        {
            0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46
        };

        private const uint Flag_HasLinkTargetIDList = 0x00000001;
        private const uint Flag_HasLinkInfo         = 0x00000002;
        private const uint Flag_HasName             = 0x00000004;
        private const uint Flag_HasRelativePath     = 0x00000008;
        private const uint Flag_HasWorkingDir       = 0x00000010;
        private const uint Flag_HasArguments        = 0x00000020;
        private const uint Flag_HasIconLocation     = 0x00000040;
        private const uint Flag_IsUnicode           = 0x00000080;

        public static ParsedLnk ParseFile(string path)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception ex) { return new ParsedLnk { ParseError = "ReadAllBytes failed: " + ex.Message }; }
            return Parse(bytes);
        }

        public static ParsedLnk Parse(byte[] data)
        {
            var info = new ParsedLnk();
            if (data == null || data.Length < 0x4C)
            {
                info.ParseError = "Buffer shorter than 76-byte ShellLinkHeader";
                return info;
            }

            uint headerSize = BitConverter.ToUInt32(data, 0x00);
            if (headerSize != 0x4C)
            {
                info.ParseError = "Unexpected HeaderSize: 0x" + headerSize.ToString("X");
                return info;
            }

            // Validate CLSID (16 bytes at offset 0x04)
            for (int i = 0; i < 16; i++)
            {
                if (data[0x04 + i] != LinkCLSID[i])
                {
                    info.ParseError = "ShellLink CLSID mismatch at byte " + i;
                    return info;
                }
            }
            info.HeaderValid = true;

            info.LinkFlags = BitConverter.ToUInt32(data, 0x14);
            info.FileAttributes = BitConverter.ToUInt32(data, 0x18);
            info.TargetCreationUtc = SafeFt(BitConverter.ToInt64(data, 0x1C));
            info.TargetAccessUtc = SafeFt(BitConverter.ToInt64(data, 0x24));
            info.TargetWriteUtc = SafeFt(BitConverter.ToInt64(data, 0x2C));
            info.TargetFileSize = BitConverter.ToUInt32(data, 0x34);
            info.IconIndex = BitConverter.ToInt32(data, 0x38);
            info.ShowCommand = BitConverter.ToInt32(data, 0x3C);

            int pos = 0x4C;
            bool isUnicode = (info.LinkFlags & Flag_IsUnicode) != 0;

            // LinkTargetIDList (variable, prefixed by 2-byte size; we skip its content)
            if ((info.LinkFlags & Flag_HasLinkTargetIDList) != 0)
            {
                if (pos + 2 > data.Length) { info.ParseError = "Truncated LinkTargetIDList"; return info; }
                int idListSize = BitConverter.ToUInt16(data, pos);
                pos += 2 + idListSize;
                if (pos > data.Length) { info.ParseError = "LinkTargetIDList overruns file"; return info; }
            }

            // LinkInfo (variable, prefixed by 4-byte LinkInfoSize)
            if ((info.LinkFlags & Flag_HasLinkInfo) != 0)
            {
                if (pos + 4 > data.Length) { info.ParseError = "Truncated LinkInfo"; return info; }
                int linkInfoSize = (int)BitConverter.ToUInt32(data, pos);
                if (linkInfoSize < 0x1C || pos + linkInfoSize > data.Length)
                {
                    info.ParseError = "Invalid LinkInfoSize: " + linkInfoSize;
                    return info;
                }
                ParseLinkInfo(data, pos, linkInfoSize, info);
                pos += linkInfoSize;
            }

            // StringData entries (each prefixed by 2-byte char-count; UTF-16 LE if IsUnicode bit set)
            if ((info.LinkFlags & Flag_HasName) != 0)
                info.Name = ReadStringData(data, ref pos, isUnicode, out var ok1);
            if ((info.LinkFlags & Flag_HasRelativePath) != 0)
                info.RelativePath = ReadStringData(data, ref pos, isUnicode, out var ok2);
            if ((info.LinkFlags & Flag_HasWorkingDir) != 0)
                info.WorkingDir = ReadStringData(data, ref pos, isUnicode, out var ok3);
            if ((info.LinkFlags & Flag_HasArguments) != 0)
                info.Arguments = ReadStringData(data, ref pos, isUnicode, out var ok4);
            if ((info.LinkFlags & Flag_HasIconLocation) != 0)
                info.IconLocation = ReadStringData(data, ref pos, isUnicode, out var ok5);

            return info;
        }

        private static void ParseLinkInfo(byte[] data, int linkInfoStart, int linkInfoSize, ParsedLnk info)
        {
            int p = linkInfoStart;
            int linkInfoHeaderSize = (int)BitConverter.ToUInt32(data, p + 4);
            uint linkInfoFlags = BitConverter.ToUInt32(data, p + 8);
            int volumeIdOffset = (int)BitConverter.ToUInt32(data, p + 12);
            int localBasePathOffset = (int)BitConverter.ToUInt32(data, p + 16);
            int commonPathSuffixOffset = (int)BitConverter.ToUInt32(data, p + 24);

            int localBasePathOffsetUnicode = 0;
            int commonPathSuffixOffsetUnicode = 0;
            if (linkInfoHeaderSize >= 0x24)
            {
                localBasePathOffsetUnicode = (int)BitConverter.ToUInt32(data, p + 28);
                commonPathSuffixOffsetUnicode = (int)BitConverter.ToUInt32(data, p + 32);
            }

            // VolumeIDAndLocalBasePath bit
            if ((linkInfoFlags & 0x1) != 0)
            {
                if (volumeIdOffset > 0 && p + volumeIdOffset + 16 <= linkInfoStart + linkInfoSize)
                {
                    ParseVolumeID(data, p + volumeIdOffset, linkInfoStart + linkInfoSize, info);
                }
                if (localBasePathOffset > 0 && p + localBasePathOffset < linkInfoStart + linkInfoSize)
                {
                    info.LocalBasePath = ReadCStringAnsi(data, p + localBasePathOffset, linkInfoStart + linkInfoSize);
                }
                // Prefer unicode local-base-path when present.
                if (localBasePathOffsetUnicode > 0 && p + localBasePathOffsetUnicode < linkInfoStart + linkInfoSize)
                {
                    string u = ReadCStringUnicode(data, p + localBasePathOffsetUnicode, linkInfoStart + linkInfoSize);
                    if (!string.IsNullOrEmpty(u)) info.LocalBasePath = u;
                }
            }

            if (commonPathSuffixOffset > 0 && p + commonPathSuffixOffset < linkInfoStart + linkInfoSize)
            {
                info.CommonPathSuffix = ReadCStringAnsi(data, p + commonPathSuffixOffset, linkInfoStart + linkInfoSize);
            }
            if (commonPathSuffixOffsetUnicode > 0 && p + commonPathSuffixOffsetUnicode < linkInfoStart + linkInfoSize)
            {
                string u = ReadCStringUnicode(data, p + commonPathSuffixOffsetUnicode, linkInfoStart + linkInfoSize);
                if (!string.IsNullOrEmpty(u)) info.CommonPathSuffix = u;
            }
        }

        private static void ParseVolumeID(byte[] data, int volIdStart, int linkInfoEnd, ParsedLnk info)
        {
            int volIdSize = (int)BitConverter.ToUInt32(data, volIdStart);
            int volIdEnd = Math.Min(volIdStart + volIdSize, linkInfoEnd);
            if (volIdEnd <= volIdStart + 16) return;

            uint driveType = BitConverter.ToUInt32(data, volIdStart + 4);
            uint driveSerial = BitConverter.ToUInt32(data, volIdStart + 8);
            int volumeLabelOffset = (int)BitConverter.ToUInt32(data, volIdStart + 12);

            info.DriveType = driveType;
            info.DriveTypeName = DriveTypeName(driveType);
            info.DriveSerialNumber = driveSerial;

            if (volumeLabelOffset == 0x14 && volIdStart + 16 < volIdEnd)
            {
                // VolumeLabelOffsetUnicode follows at +16
                int volumeLabelOffsetUnicode = (int)BitConverter.ToUInt32(data, volIdStart + 16);
                if (volumeLabelOffsetUnicode > 0 && volIdStart + volumeLabelOffsetUnicode < volIdEnd)
                {
                    info.VolumeLabel = ReadCStringUnicode(data, volIdStart + volumeLabelOffsetUnicode, volIdEnd);
                }
            }
            else if (volumeLabelOffset > 0 && volIdStart + volumeLabelOffset < volIdEnd)
            {
                info.VolumeLabel = ReadCStringAnsi(data, volIdStart + volumeLabelOffset, volIdEnd);
            }
        }

        internal static string DriveTypeName(uint driveType)
        {
            switch (driveType)
            {
                case 0: return "DRIVE_UNKNOWN";
                case 1: return "DRIVE_NO_ROOT_DIR";
                case 2: return "DRIVE_REMOVABLE";
                case 3: return "DRIVE_FIXED";
                case 4: return "DRIVE_REMOTE";
                case 5: return "DRIVE_CDROM";
                case 6: return "DRIVE_RAMDISK";
                default: return "Unknown(" + driveType + ")";
            }
        }

        internal static string ReadStringData(byte[] data, ref int pos, bool isUnicode, out bool ok)
        {
            ok = false;
            if (pos + 2 > data.Length) return null;
            int charCount = BitConverter.ToUInt16(data, pos);
            pos += 2;
            int byteCount = isUnicode ? charCount * 2 : charCount;
            if (pos + byteCount > data.Length) return null;
            string s = null;
            try
            {
                s = isUnicode
                    ? Encoding.Unicode.GetString(data, pos, byteCount)
                    : Encoding.Default.GetString(data, pos, byteCount);
            }
            catch { }
            pos += byteCount;
            ok = s != null;
            return s;
        }

        internal static string ReadCStringAnsi(byte[] data, int start, int end)
        {
            if (start >= end || start < 0) return null;
            int p = start;
            while (p < end && data[p] != 0) p++;
            int len = p - start;
            if (len <= 0) return null;
            try { return Encoding.Default.GetString(data, start, len); }
            catch { return null; }
        }

        internal static string ReadCStringUnicode(byte[] data, int start, int end)
        {
            if (start >= end || start < 0) return null;
            int p = start;
            while (p + 1 < end && !(data[p] == 0 && data[p + 1] == 0)) p += 2;
            int len = p - start;
            if (len <= 0) return null;
            try { return Encoding.Unicode.GetString(data, start, len); }
            catch { return null; }
        }

        private static DateTimeOffset? SafeFt(long ft)
        {
            if (ft <= 0) return null;
            try { return DateTimeOffset.FromFileTime(ft).ToUniversalTime(); }
            catch { return null; }
        }
    }
}
