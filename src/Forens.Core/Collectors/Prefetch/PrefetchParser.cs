using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Forens.Core.Collectors.Prefetch
{
    /// <summary>
    /// Parses Windows Prefetch (.pf) files. Handles MAM-compressed Win 8+ files
    /// transparently; understands inner SCCA structure for format versions
    /// 17 (WinXP), 23 (Win 7), 26 (Win 8.0/8.1), and 30 (Win 10/11).
    ///
    /// For v26/v30 the parser extracts: executable name, path hash, run count,
    /// up to 8 last-run timestamps (most recent first), executable full path
    /// (when present in the filename strings section), volume count, total
    /// referenced file count, and the first N referenced file paths.
    ///
    /// For v17/v23 only the header fields (version, exe name, path hash) are
    /// extracted — the file-info section layout differs and is out of scope
    /// for this slice.
    /// </summary>
    public sealed class ParsedPrefetch
    {
        public uint FormatVersion { get; set; }
        public string ExecutableName { get; set; }
        public uint PathHash { get; set; }
        public uint? RunCount { get; set; }
        public List<DateTimeOffset> LastRunTimesUtc { get; set; } = new List<DateTimeOffset>();
        public uint? VolumeCount { get; set; }
        public uint? ReferencedFilesByteCount { get; set; }
        public int? ReferencedFileCount { get; set; }
        public string ExecutableFullPath { get; set; }
        public List<string> ReferencedFiles { get; set; } = new List<string>();
        public string ParseError { get; set; }
        public bool WasCompressed { get; set; }
    }

    public static class PrefetchParser
    {
        private const uint SCCA_MAGIC = 0x41434353u; // "SCCA" little-endian
        private const int MaxReferencedFilesEmitted = 100;

        /// <summary>Parse a .pf file from disk.</summary>
        public static ParsedPrefetch ParseFile(string path)
        {
            byte[] raw;
            try { raw = File.ReadAllBytes(path); }
            catch (Exception ex)
            {
                return new ParsedPrefetch { ParseError = "ReadAllBytes failed: " + ex.Message };
            }
            return Parse(raw);
        }

        public static ParsedPrefetch Parse(byte[] raw)
        {
            var info = new ParsedPrefetch();
            if (raw == null || raw.Length < 8)
            {
                info.ParseError = "Buffer too short";
                return info;
            }

            byte[] payload = raw;
            if (PrefetchDecompressor.IsMamCompressed(raw))
            {
                info.WasCompressed = true;
                try { payload = PrefetchDecompressor.Decompress(raw); }
                catch (Exception ex)
                {
                    info.ParseError = "MAM decompression failed: " + ex.Message;
                    return info;
                }
            }

            if (payload.Length < 84)
            {
                info.ParseError = "Decompressed payload shorter than header (84 bytes)";
                return info;
            }

            // ---- Header (84 bytes) ----
            info.FormatVersion = BitConverter.ToUInt32(payload, 0x00);
            uint signature = BitConverter.ToUInt32(payload, 0x04);
            if (signature != SCCA_MAGIC)
            {
                info.ParseError = "Bad SCCA signature: 0x" + signature.ToString("X8");
                return info;
            }
            info.ExecutableName = ReadUnicodeFixed(payload, 0x10, 60);
            info.PathHash = BitConverter.ToUInt32(payload, 0x4C);

            // ---- File-info section (v26/v30/v31 layout) ----
            // v31 is Win 11 24H2; layout heuristically matches v30 in the fields we care
            // about. If parsed values look insane (huge run count, FILETIMEs out of plausible
            // range), the v31 caller can re-check after seeing the result.
            if (info.FormatVersion == 26 || info.FormatVersion == 30 || info.FormatVersion == 31)
            {
                ParseV26V30FileInfo(payload, info);
            }
            // For v17/v23, the file-info layout differs — header fields are still useful.

            return info;
        }

        private static void ParseV26V30FileInfo(byte[] p, ParsedPrefetch info)
        {
            // Need at least up through the run-count field (0xD4).
            if (p.Length < 0xD4)
            {
                info.ParseError = "Truncated file-info section";
                return;
            }

            uint filenameStringsOffset = BitConverter.ToUInt32(p, 0x64);
            uint filenameStringsSize = BitConverter.ToUInt32(p, 0x68);
            uint volumeCount = BitConverter.ToUInt32(p, 0x70);

            // 8 last-run times starting at 0x80 (same in v26/v30/v31)
            for (int i = 0; i < 8; i++)
            {
                if (0x80 + (i + 1) * 8 > p.Length) break;
                long ft = BitConverter.ToInt64(p, 0x80 + i * 8);
                if (ft <= 0) continue;
                try
                {
                    var dto = DateTimeOffset.FromFileTime(ft).ToUniversalTime();
                    info.LastRunTimesUtc.Add(dto);
                }
                catch { /* invalid FILETIME — skip */ }
            }

            // Run count at 0xD0 in v26/v30. v31 (Win 11 24H2) keeps run count at the same
            // offset most of the time but the file-information section was extended; some
            // v31 records read 0 here even when last-run times confirm execution. Treat
            // run count as best-effort for v31 and rely on LastRunTimesUtc.Count as a
            // lower-bound execution signal. (Future: probe extended v31 offsets.)
            info.RunCount = BitConverter.ToUInt32(p, 0xD0);
            info.VolumeCount = volumeCount;
            info.ReferencedFilesByteCount = filenameStringsSize;

            // Extract referenced file strings (UTF-16 LE, null-terminated, packed back-to-back)
            if (filenameStringsOffset > 0 && filenameStringsSize > 0
                && filenameStringsOffset + filenameStringsSize <= (uint)p.Length)
            {
                var files = ExtractStrings(p, (int)filenameStringsOffset, (int)filenameStringsSize, MaxReferencedFilesEmitted);
                info.ReferencedFileCount = files.Total;
                foreach (var f in files.Sample) info.ReferencedFiles.Add(f);

                // Pick a path that ends with the executable name as the "executable full path".
                if (!string.IsNullOrEmpty(info.ExecutableName))
                {
                    string exeNameUpper = info.ExecutableName.ToUpperInvariant();
                    foreach (var f in files.Sample)
                    {
                        if (string.IsNullOrEmpty(f)) continue;
                        if (f.ToUpperInvariant().EndsWith("\\" + exeNameUpper, StringComparison.Ordinal))
                        {
                            info.ExecutableFullPath = f;
                            break;
                        }
                    }
                }
            }
        }

        internal struct StringExtraction
        {
            public int Total;
            public List<string> Sample;
        }

        internal static StringExtraction ExtractStrings(byte[] p, int offset, int size, int sampleCap)
        {
            var sample = new List<string>(Math.Min(sampleCap, 64));
            int total = 0;
            int pos = offset;
            int end = offset + size;
            while (pos + 1 < end)
            {
                int strStart = pos;
                // Find UTF-16 null terminator: pair of zero bytes at even alignment.
                while (pos + 1 < end && !(p[pos] == 0 && p[pos + 1] == 0))
                    pos += 2;
                int strLen = pos - strStart;
                pos += 2; // skip the null pair
                if (strLen <= 0) continue;
                total++;
                if (sample.Count < sampleCap)
                {
                    try
                    {
                        string s = Encoding.Unicode.GetString(p, strStart, strLen);
                        if (s.Length > 0) sample.Add(s);
                    }
                    catch { /* skip undecodable string */ }
                }
            }
            return new StringExtraction { Total = total, Sample = sample };
        }

        internal static string ReadUnicodeFixed(byte[] data, int offset, int byteLength)
        {
            if (offset < 0 || offset + byteLength > data.Length) return null;
            try
            {
                string s = Encoding.Unicode.GetString(data, offset, byteLength);
                int nul = s.IndexOf('\0');
                return nul >= 0 ? s.Substring(0, nul) : s;
            }
            catch { return null; }
        }
    }
}
