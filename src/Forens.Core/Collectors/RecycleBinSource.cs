using System;
using System.IO;
using System.Text;
using Forens.Core.Collection;

namespace Forens.Core.Collectors
{
    public sealed class RecycleBinSource : IArtifactSource
    {
        public const string SourceId = "recycle-bin";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Recycle Bin",
            description: "Recycle-bin metadata: per-user $Recycle.Bin\\<SID>\\$I* index files (original path, size, deletion time).",
            category: Category.Filesystem,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: Array.Empty<ContendedResource>(),
            estimatedMemoryMB: 8,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            using (var jl = writer.OpenJsonlFile("recycle-bin.jsonl"))
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)
                        continue;
                    string root;
                    try { root = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin"); }
                    catch { continue; }
                    if (!Directory.Exists(root)) continue;

                    EnumerateBin(root, ctx, writer, jl);
                }
            }
        }

        private static void EnumerateBin(string binRoot, CollectionContext ctx, ISourceWriter writer, IRecordWriter jl)
        {
            string[] sidDirs;
            try { sidDirs = Directory.GetDirectories(binRoot); }
            catch (UnauthorizedAccessException)
            {
                writer.RecordPartial("Cannot enumerate at least one $Recycle.Bin SID directory");
                return;
            }
            catch (Exception ex)
            {
                ctx.Logger.Verbose(ex, "Failed to enumerate recycle bin root {Root}", binRoot);
                writer.RecordPartial("Failed to enumerate at least one recycle bin root");
                return;
            }

            foreach (var sidDir in sidDirs)
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                string sid = Path.GetFileName(sidDir);
                string[] indexFiles;
                try { indexFiles = Directory.GetFiles(sidDir, "$I*"); }
                catch { continue; }

                foreach (var indexPath in indexFiles)
                {
                    var record = ParseIndex(indexPath, sid);
                    if (record == null) continue;
                    jl.Write(record);
                    writer.RecordItem();
                }
            }
        }

        // $I file format:
        //   v1 (Vista/7/8/8.1): 0x0000_0000_0000_0001 header, then size (8), filetime (8), then 520 bytes UTF-16LE filename (260 chars max).
        //   v2 (Win 10+):       0x0000_0000_0000_0002 header, then size (8), filetime (8), then 4-byte filename length (chars), then UTF-16LE filename.
        internal static RecycleRecord ParseIndex(string path, string sid)
        {
            byte[] data;
            try { data = File.ReadAllBytes(path); }
            catch { return null; }
            if (data.Length < 24) return null;

            long version = BitConverter.ToInt64(data, 0);
            long size = BitConverter.ToInt64(data, 8);
            long filetime = BitConverter.ToInt64(data, 16);
            DateTimeOffset? deletedUtc = SafeFileTime(filetime);

            string originalPath = null;
            if (version == 1 && data.Length >= 24 + 520)
            {
                originalPath = ReadUnicodeFixed(data, 24, 520);
            }
            else if (version == 2 && data.Length >= 28)
            {
                int chars = BitConverter.ToInt32(data, 24);
                if (chars > 0 && 28 + chars * 2 <= data.Length)
                {
                    originalPath = Encoding.Unicode.GetString(data, 28, chars * 2).TrimEnd('\0');
                }
            }

            return new RecycleRecord
            {
                IndexPath = path,
                Sid = sid,
                Version = version,
                SizeBytes = size,
                DeletedUtc = deletedUtc,
                OriginalPath = originalPath
            };
        }

        private static string ReadUnicodeFixed(byte[] data, int offset, int byteLength)
        {
            try
            {
                string s = Encoding.Unicode.GetString(data, offset, byteLength);
                int nul = s.IndexOf('\0');
                return nul >= 0 ? s.Substring(0, nul) : s;
            }
            catch { return null; }
        }

        internal static DateTimeOffset? SafeFileTime(long ft)
        {
            if (ft <= 0) return null;
            try { return DateTimeOffset.FromFileTime(ft).ToUniversalTime(); }
            catch { return null; }
        }

        internal sealed class RecycleRecord
        {
            public string IndexPath { get; set; }
            public string Sid { get; set; }
            public long Version { get; set; }
            public long SizeBytes { get; set; }
            public DateTimeOffset? DeletedUtc { get; set; }
            public string OriginalPath { get; set; }
        }
    }
}
