using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Forens.Core.Collection;

namespace Forens.Core.Collectors
{
    /// <summary>
    /// Enumerates LNK files (.lnk + .automaticDestinations-ms + .customDestinations-ms)
    /// in known per-user folders. Records metadata only (filename, full path, size,
    /// creation/last-modified time). Full Microsoft Shell-Link binary parsing is out of
    /// scope for this slice — analysts get the file list + metadata which already
    /// answers most of "what was launched and when" questions; deep parsing can be
    /// layered on later as a separate enrichment source.
    /// </summary>
    public sealed class LnkFilesSource : IArtifactSource
    {
        public const string SourceId = "lnk-files";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "LNK Files (per-user)",
            description: "Shortcut + jump-list file inventory (.lnk + .automaticDestinations-ms + .customDestinations-ms) under the operator's profile.",
            category: Category.User,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: Array.Empty<ContendedResource>(),
            estimatedMemoryMB: 16,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        private sealed class Root
        {
            public string Label;
            public string Dir;
            public bool Recurse;
            public string[] Patterns;
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            using (var jl = writer.OpenJsonlFile("lnk-files.jsonl"))
            {
                foreach (var r in EnumerateRoots())
                {
                    if (!Directory.Exists(r.Dir)) continue;
                    EnumerateOne(r.Label, r.Dir, r.Recurse, r.Patterns, ctx, writer, jl);
                }
            }
        }

        private static IEnumerable<Root> EnumerateRoots()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
            string commonStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
            string recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);

            yield return new Root { Label = "Recent", Dir = recent, Recurse = false, Patterns = new[] { "*.lnk" } };
            yield return new Root { Label = "Recent.Automatic", Dir = Path.Combine(appData, "Microsoft", "Windows", "Recent", "AutomaticDestinations"), Recurse = false, Patterns = new[] { "*.automaticDestinations-ms" } };
            yield return new Root { Label = "Recent.Custom", Dir = Path.Combine(appData, "Microsoft", "Windows", "Recent", "CustomDestinations"), Recurse = false, Patterns = new[] { "*.customDestinations-ms" } };
            yield return new Root { Label = "Desktop", Dir = desktop, Recurse = false, Patterns = new[] { "*.lnk" } };
            yield return new Root { Label = "StartMenu.User", Dir = startMenu, Recurse = true, Patterns = new[] { "*.lnk" } };
            yield return new Root { Label = "StartMenu.Common", Dir = commonStartMenu, Recurse = true, Patterns = new[] { "*.lnk" } };
        }

        private static void EnumerateOne(string label, string dir, bool recurse, string[] patterns,
            CollectionContext ctx, ISourceWriter writer, IRecordWriter jl)
        {
            var opt = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var pattern in patterns)
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(dir, pattern, opt); }
                catch (UnauthorizedAccessException)
                {
                    writer.RecordPartial("Some user-folder paths were not readable");
                    continue;
                }
                catch (Exception ex)
                {
                    ctx.Logger.Verbose(ex, "Failed to enumerate {Dir}/{Pattern}", dir, pattern);
                    writer.RecordPartial("Failed to enumerate at least one shortcut path");
                    continue;
                }

                foreach (var path in files)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(path);
                        jl.Write(new LnkRecord
                        {
                            Label = label,
                            FileName = info.Name,
                            FullPath = info.FullName,
                            Extension = info.Extension,
                            SizeBytes = info.Length,
                            CreatedUtc = info.CreationTimeUtc,
                            LastModifiedUtc = info.LastWriteTimeUtc,
                            LastAccessUtc = info.LastAccessTimeUtc
                        });
                        writer.RecordItem();
                    }
                    catch (Exception ex)
                    {
                        ctx.Logger.Verbose(ex, "Skipped LNK file {Path}", path);
                    }
                }
            }
        }

        private sealed class LnkRecord
        {
            public string Label { get; set; }
            public string FileName { get; set; }
            public string FullPath { get; set; }
            public string Extension { get; set; }
            public long SizeBytes { get; set; }
            public DateTime CreatedUtc { get; set; }
            public DateTime LastModifiedUtc { get; set; }
            public DateTime LastAccessUtc { get; set; }
        }
    }
}
