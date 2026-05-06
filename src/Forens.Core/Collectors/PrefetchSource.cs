using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Forens.Core.Collection;
using Forens.Core.Collectors.Prefetch;

namespace Forens.Core.Collectors
{
    /// <summary>
    /// Windows Prefetch (.pf) collector. Emits one parsed record per file:
    /// format version, executable name + full path, path hash, run count,
    /// up to 8 last-run timestamps, volume count, referenced file count,
    /// and the first 100 referenced file paths. Falls back to filename-derived
    /// metadata + an error reason when binary parsing fails.
    /// </summary>
    public sealed class PrefetchSource : IArtifactSource
    {
        public const string SourceId = "prefetch";

        private static readonly Regex FilenamePattern = new Regex(
            @"^(?<exe>.+?)-(?<hash>[0-9A-Fa-f]{8})\.pf$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Prefetch (parsed)",
            description: "Windows Prefetch parser: executable, path hash, run count, last-8 run times, referenced files, volume count.",
            category: Category.Persistence,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: true,
            processFilterMode: ProcessFilterMode.HistoricalImagePath,
            contendedResources: Array.Empty<ContendedResource>(),
            estimatedMemoryMB: 16,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            string dir = PrefetchDir();
            if (!Directory.Exists(dir))
                return SourcePrecondition.Skip(PreconditionResult.SkipNotAvailableOnHost,
                    "Prefetch directory not present: " + dir);
            try
            {
                Directory.GetFiles(dir, "*.pf");
                return SourcePrecondition.Ok();
            }
            catch (UnauthorizedAccessException)
            {
                return SourcePrecondition.Skip(PreconditionResult.SkipRequiresElevation,
                    "Cannot read Prefetch directory");
            }
            catch (Exception ex)
            {
                return SourcePrecondition.Skip(PreconditionResult.SkipNotAvailableOnHost,
                    "Cannot list Prefetch files: " + ex.Message);
            }
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            string dir = PrefetchDir();
            string[] files;
            try { files = Directory.GetFiles(dir, "*.pf"); }
            catch (UnauthorizedAccessException)
            {
                writer.RecordPartial("Prefetch directory not readable");
                return;
            }

            using (var jl = writer.OpenJsonlFile("prefetch.jsonl"))
            {
                int parseErrors = 0;
                foreach (var path in files)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    var record = BuildRecord(path);
                    if (record == null) continue;

                    string filterPath = record.ExecutableFullPath ?? record.ExecutableName;
                    if (!string.IsNullOrEmpty(filterPath) &&
                        !ctx.ProcessFilter.IncludesImagePath(filterPath))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(record.ParseError)) parseErrors++;

                    jl.Write(record);
                    writer.RecordItem();
                }
                if (parseErrors > 0)
                {
                    ctx.Logger.Information(
                        "Prefetch: {Count} files had a parse error and were emitted with metadata only",
                        parseErrors);
                    writer.RecordPartial(parseErrors + " prefetch file(s) failed to parse");
                }
            }
        }

        internal static PrefetchRecord BuildRecord(string path)
        {
            FileInfo info;
            try { info = new FileInfo(path); }
            catch { return null; }

            var match = FilenamePattern.Match(info.Name);
            string filenameExe = match.Success ? match.Groups["exe"].Value : null;
            string filenameHash = match.Success ? match.Groups["hash"].Value.ToUpperInvariant() : null;

            ParsedPrefetch parsed;
            try { parsed = PrefetchParser.ParseFile(path); }
            catch (Exception ex)
            {
                parsed = new ParsedPrefetch { ParseError = "Unhandled parser exception: " + ex.Message };
            }

            return new PrefetchRecord
            {
                FileName = info.Name,
                FullPath = info.FullName,
                FormatVersion = parsed != null && parsed.FormatVersion != 0 ? (uint?)parsed.FormatVersion : null,
                WasCompressed = parsed?.WasCompressed ?? false,
                ExecutableName = !string.IsNullOrEmpty(parsed?.ExecutableName) ? parsed.ExecutableName : filenameExe,
                ExecutableFullPath = parsed?.ExecutableFullPath,
                PathHash = parsed != null && parsed.PathHash != 0
                    ? parsed.PathHash.ToString("X8")
                    : filenameHash,
                RunCount = parsed?.RunCount,
                LastRunTimesUtc = parsed != null && parsed.LastRunTimesUtc.Count > 0 ? parsed.LastRunTimesUtc : null,
                VolumeCount = parsed?.VolumeCount,
                ReferencedFileCount = parsed?.ReferencedFileCount,
                ReferencedFiles = parsed != null && parsed.ReferencedFiles.Count > 0 ? parsed.ReferencedFiles : null,
                SizeBytes = info.Length,
                FileCreatedUtc = info.CreationTimeUtc,
                FileLastModifiedUtc = info.LastWriteTimeUtc,
                ParseError = parsed?.ParseError
            };
        }

        private static string PrefetchDir()
        {
            string sysroot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            return Path.Combine(sysroot, "Prefetch");
        }

        internal sealed class PrefetchRecord
        {
            public string FileName { get; set; }
            public string FullPath { get; set; }
            public uint? FormatVersion { get; set; }
            public bool WasCompressed { get; set; }
            public string ExecutableName { get; set; }
            public string ExecutableFullPath { get; set; }
            public string PathHash { get; set; }
            public uint? RunCount { get; set; }
            public List<DateTimeOffset> LastRunTimesUtc { get; set; }
            public uint? VolumeCount { get; set; }
            public int? ReferencedFileCount { get; set; }
            public List<string> ReferencedFiles { get; set; }
            public long SizeBytes { get; set; }
            public DateTime FileCreatedUtc { get; set; }
            public DateTime FileLastModifiedUtc { get; set; }
            public string ParseError { get; set; }
        }
    }
}
