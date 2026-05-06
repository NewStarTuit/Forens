using System;
using System.Collections.Generic;
using System.Text;
using Forens.Core.Collection;
using Microsoft.Win32;

namespace Forens.Core.Collectors
{
    /// <summary>
    /// Reads HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\AppCompatCache
    /// (the live shimcache value) and parses Win 10/11 entries. The HKLM\SYSTEM
    /// hive's AppCompatCache key has SYSTEM-level ACL — non-admin shells will
    /// fail at read time and the source returns SkipRequiresElevation cleanly.
    /// On elevated reads, parses Win 10+ format (entry signature "10ts" /
    /// 0x73743031). Falls back to preserved-raw-bytes when the format isn't
    /// recognised.
    /// </summary>
    public sealed class ShimcacheSource : IArtifactSource
    {
        public const string SourceId = "shimcache";
        private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\AppCompatCache";
        private const string ValueName = "AppCompatCache";
        private const uint Win10EntrySig = 0x73743031u; // "10ts"

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Shimcache (AppCompatCache)",
            description: "Application Compatibility cache from HKLM\\SYSTEM — historical executable paths with last-modified timestamps. Requires elevation (HKLM\\SYSTEM ACL).",
            category: Category.Persistence,
            requiresElevation: true,
            supportsTimeRange: false,
            supportsProcessFilter: true,
            processFilterMode: ProcessFilterMode.HistoricalImagePath,
            contendedResources: new[] { ContendedResource.RegistryHiveSystem },
            estimatedMemoryMB: 16,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            if (ctx.Elevation != Forens.Common.Host.ElevationState.Elevated)
            {
                return SourcePrecondition.Skip(PreconditionResult.SkipRequiresElevation,
                    "Shimcache requires administrator privileges to read HKLM\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\AppCompatCache");
            }
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            byte[] blob = ReadBlob(ctx, writer);
            if (blob == null) return;

            using (var jl = writer.OpenJsonlFile("shimcache.jsonl"))
            using (var headerWriter = writer.OpenJsonlFile("shimcache-header.jsonl"))
            {
                // Always emit the raw header so analysts can verify our parse against
                // a byte-exact copy.
                headerWriter.Write(new HeaderRecord
                {
                    BlobByteCount = blob.Length,
                    HeaderHex = ShellBagsSource.HexEncode(blob, 0, Math.Min(64, blob.Length))
                });
                writer.RecordItem();

                int parsed = 0;
                foreach (var entry in ParseEntries(blob, ctx, writer))
                {
                    if (!string.IsNullOrEmpty(entry.Path) &&
                        !ctx.ProcessFilter.IncludesImagePath(entry.Path))
                    {
                        continue;
                    }
                    jl.Write(entry);
                    writer.RecordItem();
                    parsed++;
                }
                if (parsed == 0)
                {
                    writer.RecordPartial("Shimcache blob present but no entries parsed (unsupported format)");
                }
            }
        }

        private static byte[] ReadBlob(CollectionContext ctx, ISourceWriter writer)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = baseKey.OpenSubKey(KeyPath, writable: false))
                {
                    if (key == null)
                    {
                        writer.RecordPartial("AppCompatCache key not present");
                        return null;
                    }
                    return key.GetValue(ValueName) as byte[];
                }
            }
            catch (System.Security.SecurityException ex)
            {
                ctx.Logger.Warning(ex, "Shimcache key access denied");
                writer.RecordPartial("Shimcache key access denied: " + ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                ctx.Logger.Warning(ex, "Failed to read AppCompatCache value");
                writer.RecordPartial("Failed to read AppCompatCache: " + ex.Message);
                return null;
            }
        }

        // Win 10 format header is 0x30 bytes; entries follow each starting with the
        // "10ts" 4-byte magic. Some Win 10 builds use a different header offset; we
        // scan forward for the first "10ts" occurrence to be tolerant of variants.
        internal static IEnumerable<ShimcacheEntry> ParseEntries(byte[] blob, CollectionContext ctx, ISourceWriter writer)
        {
            int start = FindFirstSignature(blob, 0, Win10EntrySig);
            if (start < 0)
            {
                yield break;
            }
            int pos = start;
            int safetyMax = 100_000;
            int produced = 0;
            while (pos + 12 <= blob.Length && produced < safetyMax)
            {
                uint sig = BitConverter.ToUInt32(blob, pos);
                if (sig != Win10EntrySig) break;

                uint entryLen = BitConverter.ToUInt32(blob, pos + 4);
                if (entryLen < 12 || pos + 8 + entryLen > blob.Length) break;

                int entryStart = pos + 8;
                int entryEnd = entryStart + (int)entryLen;
                int p = entryStart;
                if (p + 2 > entryEnd) break;

                ushort pathLen = BitConverter.ToUInt16(blob, p);
                p += 2;
                if (p + pathLen > entryEnd) break;
                string path = null;
                if (pathLen > 0)
                {
                    try { path = Encoding.Unicode.GetString(blob, p, pathLen); } catch { path = null; }
                }
                p += pathLen;

                DateTimeOffset? lastModified = null;
                if (p + 8 <= entryEnd)
                {
                    long ft = BitConverter.ToInt64(blob, p);
                    p += 8;
                    if (ft > 0)
                    {
                        try { lastModified = DateTimeOffset.FromFileTime(ft).ToUniversalTime(); }
                        catch { /* leave null */ }
                    }
                }

                int? dataLen = null;
                if (p + 4 <= entryEnd)
                {
                    dataLen = (int)BitConverter.ToUInt32(blob, p);
                }

                yield return new ShimcacheEntry
                {
                    Path = path,
                    LastModifiedUtc = lastModified,
                    EntryByteCount = (int)entryLen + 8,
                    DataByteCount = dataLen
                };

                produced++;
                pos = entryEnd;
            }
        }

        private static int FindFirstSignature(byte[] blob, int startAt, uint sig)
        {
            for (int i = startAt; i + 4 <= blob.Length; i++)
            {
                if (BitConverter.ToUInt32(blob, i) == sig) return i;
            }
            return -1;
        }

        internal sealed class ShimcacheEntry
        {
            public string Path { get; set; }
            public DateTimeOffset? LastModifiedUtc { get; set; }
            public int EntryByteCount { get; set; }
            public int? DataByteCount { get; set; }
        }

        private sealed class HeaderRecord
        {
            public int BlobByteCount { get; set; }
            public string HeaderHex { get; set; }
        }
    }
}
