using System;
using System.Text;
using Forens.Core.Collection;
using Forens.Core.Collectors.Registry;
using Microsoft.Win32;

namespace Forens.Core.Collectors
{
    public sealed class RecentDocsSource : IArtifactSource
    {
        public const string SourceId = "recentdocs";
        private const string RootPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Recent Documents",
            description: "Recently-opened documents per file extension (HKCU\\Software\\...\\Explorer\\RecentDocs).",
            category: Category.User,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: new[] { ContendedResource.RegistryHiveUser },
            estimatedMemoryMB: 8,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            using (var jl = writer.OpenJsonlFile("recentdocs.jsonl"))
            {
                EmitFromExtKey(ctx, writer, jl, RootPath, "(all)");

                foreach (var sub in RegistryReader.EnumerateSubkeys(
                    RegistryHive.CurrentUser, RegistryView.Registry64, RootPath))
                {
                    EmitFromExtKey(ctx, writer, jl, RootPath + "\\" + sub.SubkeyName, sub.SubkeyName);
                }
            }
        }

        private static void EmitFromExtKey(CollectionContext ctx, ISourceWriter writer, IRecordWriter jl,
            string keyPath, string extension)
        {
            try
            {
                foreach (var v in RegistryReader.EnumerateValues(
                    RegistryHive.CurrentUser, RegistryView.Registry64, keyPath))
                {
                    if (string.Equals(v.ValueName, "MRUListEx", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!(v.Value is byte[] data) || data.Length < 2)
                        continue;

                    string filename = ExtractUnicodeFilenamePrefix(data);
                    if (string.IsNullOrEmpty(filename)) continue;

                    jl.Write(new RecentDocRecord
                    {
                        Extension = extension,
                        Slot = v.ValueName,
                        Filename = filename,
                        ValueByteCount = data.Length
                    });
                    writer.RecordItem();
                }
            }
            catch (Exception ex)
            {
                ctx.Logger.Verbose(ex, "Failed reading RecentDocs sub-key {Key}", keyPath);
                writer.RecordPartial("Failed reading at least one RecentDocs sub-key");
            }
        }

        // The first field in each value is a UTF-16 LE null-terminated filename; following it
        // is a binary shell-link blob we don't decode here.
        internal static string ExtractUnicodeFilenamePrefix(byte[] data)
        {
            if (data == null) return null;
            // Walk pairs of bytes until we find a 0x0000 pair (UTF-16 null terminator).
            int end = -1;
            for (int i = 0; i + 1 < data.Length; i += 2)
            {
                if (data[i] == 0 && data[i + 1] == 0) { end = i; break; }
            }
            if (end <= 0) return null;
            try { return Encoding.Unicode.GetString(data, 0, end); }
            catch { return null; }
        }

        private sealed class RecentDocRecord
        {
            public string Extension { get; set; }
            public string Slot { get; set; }
            public string Filename { get; set; }
            public int ValueByteCount { get; set; }
        }
    }
}
