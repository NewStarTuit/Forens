using System;
using System.IO;
using System.Runtime.InteropServices;
using Forens.Core.Collection;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Forens.Core.Collectors
{
    public sealed class AmcacheSource : IArtifactSource
    {
        public const string SourceId = "amcache";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Amcache (executable inventory)",
            description: "Windows Amcache hive — every executable Windows has seen, with SHA-1 hash, link date, publisher, version.",
            category: Category.Persistence,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: true,
            processFilterMode: ProcessFilterMode.HistoricalImagePath,
            contendedResources: Array.Empty<ContendedResource>(),
            estimatedMemoryMB: 32,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            string path = HivePath();
            if (!File.Exists(path))
                return SourcePrecondition.Skip(PreconditionResult.SkipNotAvailableOnHost,
                    "Amcache hive not present: " + path);
            try
            {
                using (AmcacheLoader.Open(path)) { }
                return SourcePrecondition.Ok();
            }
            catch (UnauthorizedAccessException)
            {
                return SourcePrecondition.Skip(PreconditionResult.SkipRequiresElevation,
                    "Cannot read Amcache.hve");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                return SourcePrecondition.Skip(PreconditionResult.SkipRequiresElevation,
                    "Cannot read Amcache.hve (access denied)");
            }
            catch (Exception ex)
            {
                return SourcePrecondition.Skip(PreconditionResult.SkipNotAvailableOnHost,
                    "Cannot mount Amcache.hve: " + ex.Message);
            }
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            string path = HivePath();
            using (var jl = writer.OpenJsonlFile("amcache.jsonl"))
            using (var root = AmcacheLoader.Open(path))
            {
                if (root == null)
                {
                    writer.RecordPartial("Failed to mount Amcache.hve");
                    return;
                }

                bool emitted = false;
                emitted |= EmitFromKey(root, @"Root\InventoryApplicationFile", "InventoryApplicationFile", ctx, writer, jl);
                emitted |= EmitFromKey(root, @"Root\InventoryApplication", "InventoryApplication", ctx, writer, jl);
                emitted |= EmitFromKey(root, @"Root\File", "File", ctx, writer, jl);

                if (!emitted)
                {
                    writer.RecordPartial("No known Amcache root keys present (unrecognized hive layout)");
                }
            }
        }

        private static string HivePath()
        {
            string sysroot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            return Path.Combine(sysroot, "appcompat", "Programs", "Amcache.hve");
        }

        private static bool EmitFromKey(RegistryKey root, string subKey, string sourceLabel,
            CollectionContext ctx, ISourceWriter writer, IRecordWriter jl)
        {
            using (var k = root.OpenSubKey(subKey, writable: false))
            {
                if (k == null) return false;
                string[] subNames;
                try { subNames = k.GetSubKeyNames(); }
                catch (Exception ex)
                {
                    ctx.Logger.Verbose(ex, "Failed to enumerate Amcache subkey {Sub}", subKey);
                    writer.RecordPartial("Failed to enumerate at least one Amcache subkey");
                    return false;
                }

                foreach (var name in subNames)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    using (var entry = k.OpenSubKey(name, writable: false))
                    {
                        if (entry == null) continue;
                        var record = ReadEntry(entry, sourceLabel, name);
                        if (record == null) continue;

                        if (!string.IsNullOrEmpty(record.Path) &&
                            !ctx.ProcessFilter.IncludesImagePath(record.Path))
                        {
                            continue;
                        }

                        jl.Write(record);
                        writer.RecordItem();
                    }
                }
            }
            return true;
        }

        private static AmcacheRecord ReadEntry(RegistryKey entry, string sourceLabel, string keyName)
        {
            try
            {
                return new AmcacheRecord
                {
                    Source = sourceLabel,
                    EntryKey = keyName,
                    Path = (entry.GetValue("LowerCaseLongPath") as string)
                           ?? (entry.GetValue("Path") as string)
                           ?? (entry.GetValue("FilePath") as string),
                    Sha1 = entry.GetValue("Hash") as string ?? entry.GetValue("FileId") as string,
                    Size = TryToLong(entry.GetValue("Size")),
                    BinaryType = entry.GetValue("BinaryType") as string,
                    LinkDate = entry.GetValue("LinkDate") as string,
                    IsPeFile = TryToInt(entry.GetValue("IsPeFile")),
                    IsOsComponent = TryToInt(entry.GetValue("IsOsComponent")),
                    Version = entry.GetValue("Version") as string ?? entry.GetValue("FileVersion") as string,
                    ProductName = entry.GetValue("ProductName") as string,
                    ProductVersion = entry.GetValue("ProductVersion") as string,
                    Publisher = entry.GetValue("Publisher") as string ?? entry.GetValue("CompanyName") as string,
                    OriginalFileName = entry.GetValue("OriginalFileName") as string,
                    Name = entry.GetValue("Name") as string,
                    Language = entry.GetValue("Language") as string
                };
            }
            catch
            {
                return null;
            }
        }

        private static long? TryToLong(object o)
        {
            if (o == null) return null;
            try { return Convert.ToInt64(o, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        private static int? TryToInt(object o)
        {
            if (o == null) return null;
            try { return Convert.ToInt32(o, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        private sealed class AmcacheRecord
        {
            public string Source { get; set; }
            public string EntryKey { get; set; }
            public string Path { get; set; }
            public string Sha1 { get; set; }
            public long? Size { get; set; }
            public string BinaryType { get; set; }
            public string LinkDate { get; set; }
            public int? IsPeFile { get; set; }
            public int? IsOsComponent { get; set; }
            public string Version { get; set; }
            public string ProductName { get; set; }
            public string ProductVersion { get; set; }
            public string Publisher { get; set; }
            public string OriginalFileName { get; set; }
            public string Name { get; set; }
            public string Language { get; set; }
        }
    }

    /// <summary>
    /// Back-compat shim — the loader now lives in
    /// <see cref="Forens.Core.Collectors.Hive.HiveLoader"/> and is shared with
    /// SAM/SECURITY hive sources.
    /// </summary>
    internal static class AmcacheLoader
    {
        public static RegistryKey Open(string hivePath)
        {
            return Forens.Core.Collectors.Hive.HiveLoader.Open(hivePath);
        }
    }
}
