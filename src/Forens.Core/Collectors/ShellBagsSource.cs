using System;
using System.Collections.Generic;
using System.Text;
using Forens.Core.Collection;
using Microsoft.Win32;

namespace Forens.Core.Collectors
{
    /// <summary>
    /// Walks HKCU\Software\Microsoft\Windows\Shell\BagMRU + Bags hierarchy and emits
    /// one record per (key, value) pair. ITEMIDLIST values are preserved as
    /// hex-encoded raw bytes plus a best-effort UTF-16 string extraction
    /// (yields displayable folder/file path fragments). Full ITEMIDLIST decoding
    /// is intentionally minimal — tooling like ShellBagsExplorer can parse the
    /// preserved raw bytes for deeper analysis.
    /// </summary>
    public sealed class ShellBagsSource : IArtifactSource
    {
        public const string SourceId = "shellbags";

        private static readonly string[] BagRoots =
        {
            @"Software\Microsoft\Windows\Shell\BagMRU",
            @"Software\Microsoft\Windows\Shell\Bags",
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\BagMRU",
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags"
        };

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "ShellBags (BagMRU + Bags)",
            description: "Operator's per-folder Explorer view history (HKCU\\...\\Shell\\BagMRU + Bags). Raw ITEMIDLIST bytes preserved + best-effort UTF-16 strings extracted.",
            category: Category.User,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: new[] { ContendedResource.RegistryHiveUser },
            estimatedMemoryMB: 16,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            using (var jl = writer.OpenJsonlFile("shellbags.jsonl"))
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            {
                foreach (var rootPath in BagRoots)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    using (var root = baseKey.OpenSubKey(rootPath, writable: false))
                    {
                        if (root == null) continue;
                        WalkRecursive(root, rootPath, "HKCU", ctx, writer, jl, depth: 0);
                    }
                }
            }
        }

        private static void WalkRecursive(RegistryKey key, string keyPath, string hive,
            CollectionContext ctx, ISourceWriter writer, IRecordWriter jl, int depth)
        {
            // Cap recursion to prevent runaway in malformed/corrupt registries.
            if (depth > 32) return;

            string[] valueNames;
            try { valueNames = key.GetValueNames(); }
            catch
            {
                writer.RecordPartial("Cannot list values under " + keyPath);
                valueNames = Array.Empty<string>();
            }

            foreach (var valueName in valueNames)
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                object value;
                RegistryValueKind kind;
                try
                {
                    value = key.GetValue(valueName);
                    kind = key.GetValueKind(valueName);
                }
                catch { continue; }

                var record = new ShellBagRecord
                {
                    Hive = hive,
                    KeyPath = keyPath,
                    ValueName = valueName,
                    Kind = kind.ToString()
                };

                if (value is byte[] bytes)
                {
                    record.ByteCount = bytes.Length;
                    record.HexFirst64 = HexEncode(bytes, 0, Math.Min(64, bytes.Length));
                    record.ExtractedStrings = ExtractUnicodeStrings(bytes, minLen: 3);
                    if (string.Equals(valueName, "MRUListEx", StringComparison.OrdinalIgnoreCase))
                    {
                        record.MruListExOrder = ParseMruListEx(bytes);
                    }
                }
                else if (value != null)
                {
                    record.StringValue = value.ToString();
                }

                jl.Write(record);
                writer.RecordItem();
            }

            string[] subNames;
            try { subNames = key.GetSubKeyNames(); }
            catch
            {
                writer.RecordPartial("Cannot list subkeys under " + keyPath);
                return;
            }

            foreach (var subName in subNames)
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                using (var sub = key.OpenSubKey(subName, writable: false))
                {
                    if (sub == null) continue;
                    WalkRecursive(sub, keyPath + "\\" + subName, hive, ctx, writer, jl, depth + 1);
                }
            }
        }

        // MRUListEx: array of uint32 slot indexes (most-recent first), terminated by 0xFFFFFFFF.
        internal static List<int> ParseMruListEx(byte[] data)
        {
            var order = new List<int>();
            for (int i = 0; i + 4 <= data.Length; i += 4)
            {
                int slot = BitConverter.ToInt32(data, i);
                if (slot == -1) break;
                order.Add(slot);
                if (order.Count > 1024) break; // safety cap
            }
            return order;
        }

        // Extract UTF-16 LE substrings of length >= minLen from arbitrary bytes.
        // Each candidate run is bounded by non-printable boundaries.
        internal static List<string> ExtractUnicodeStrings(byte[] data, int minLen)
        {
            var results = new List<string>();
            if (data == null) return results;

            int start = -1;
            int i = 0;
            while (i + 1 < data.Length)
            {
                ushort ch = (ushort)(data[i] | (data[i + 1] << 8));
                bool printable = ch >= 0x20 && ch <= 0x7E
                    || (ch >= 0xA0 && ch <= 0xFFFD && ch != 0xFFFE && ch != 0xFFFF);
                if (printable)
                {
                    if (start < 0) start = i;
                }
                else
                {
                    if (start >= 0)
                    {
                        int len = i - start;
                        int chars = len / 2;
                        if (chars >= minLen)
                        {
                            try
                            {
                                string s = Encoding.Unicode.GetString(data, start, len);
                                if (!string.IsNullOrWhiteSpace(s)) results.Add(s);
                            }
                            catch { }
                        }
                        start = -1;
                    }
                }
                i += 2;
            }
            // trailing run
            if (start >= 0)
            {
                int len = data.Length - start;
                if (len > 0 && len % 2 == 0 && len / 2 >= minLen)
                {
                    try
                    {
                        string s = Encoding.Unicode.GetString(data, start, len);
                        if (!string.IsNullOrWhiteSpace(s)) results.Add(s);
                    }
                    catch { }
                }
            }
            return results;
        }

        internal static string HexEncode(byte[] data, int offset, int count)
        {
            var sb = new StringBuilder(count * 2);
            for (int i = 0; i < count; i++) sb.Append(data[offset + i].ToString("X2"));
            return sb.ToString();
        }

        private sealed class ShellBagRecord
        {
            public string Hive { get; set; }
            public string KeyPath { get; set; }
            public string ValueName { get; set; }
            public string Kind { get; set; }
            public int? ByteCount { get; set; }
            public string HexFirst64 { get; set; }
            public List<string> ExtractedStrings { get; set; }
            public List<int> MruListExOrder { get; set; }
            public string StringValue { get; set; }
        }
    }
}
