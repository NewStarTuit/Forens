using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Forens.Common.Host;
using Forens.Common.Logging;
using Forens.Common.Time;
using Forens.Core.Collection;
using Forens.Core.Collection.Run;
using Forens.Core.Profiles;
using Forens.Reporting;
using McMaster.Extensions.CommandLineUtils;

namespace Forens.Cli.Commands
{
    [Command("collect", Description = "Run a forensic collection.")]
    public sealed class CollectCommand
    {
        [Option("--sources", "Comma-separated source ids to run.", CommandOptionType.SingleValue)]
        public string SourcesCsv { get; set; }

        [Option("--profile", "Profile name. Mutually exclusive with --sources.", CommandOptionType.SingleValue)]
        public string Profile { get; set; }

        [Option("--from", "Inclusive UTC lower bound (ISO 8601 with offset/Z).", CommandOptionType.SingleValue)]
        public string FromIso { get; set; }

        [Option("--to", "Inclusive UTC upper bound (ISO 8601 with offset/Z).", CommandOptionType.SingleValue)]
        public string ToIso { get; set; }

        [Option("--no-time-filter", "Comma-separated source ids that may silently drop the time filter.", CommandOptionType.SingleValue)]
        public string NoTimeFilterCsv { get; set; }

        [Option("--pid", "Comma-separated PIDs to filter on (positive integers).", CommandOptionType.SingleValue)]
        public string PidsCsv { get; set; }

        [Option("--process-name", "Comma-separated process names to filter on (case-insensitive).", CommandOptionType.SingleValue)]
        public string ProcessNamesCsv { get; set; }

        [Option("--output", "Output directory root.", CommandOptionType.SingleValue)]
        public string OutputRoot { get; set; } = "./forens-out";

        [Option("--case-id", "Operator-supplied case id.", CommandOptionType.SingleValue)]
        public string CaseId { get; set; }

        [Option("--parallelism", "Maximum concurrent sources (>= 1).", CommandOptionType.SingleValue)]
        public int? Parallelism { get; set; }

        [Option("--memory-ceiling-mb", "Soft memory ceiling in MB (>= 64).", CommandOptionType.SingleValue)]
        public int? MemoryCeilingMB { get; set; }

        [Option("--disk-floor-mb", "Disk-free floor in MB (>= 0).", CommandOptionType.SingleValue)]
        public int? DiskFloorMB { get; set; }

        [Option("--dry-run", "Print pre-run summary and exit 0 without collecting.", CommandOptionType.NoValue)]
        public bool DryRun { get; set; }

        [Option("-v|--verbose", "Lower log threshold to Verbose.", CommandOptionType.NoValue)]
        public bool Verbose { get; set; }

        [Option("--quiet", "Raise log threshold to Warning.", CommandOptionType.NoValue)]
        public bool Quiet { get; set; }

        [Option("--keyword", "Run a post-collection keyword aggregation across every source's output. Repeat the flag for OR-logic. Emits search.jsonl + search.summary.json under the run directory.", CommandOptionType.MultipleValue)]
        public string[] Keywords { get; set; }

        [Option("--keyword-case-sensitive", "Match --keyword exactly as typed. Default is case-insensitive.", CommandOptionType.NoValue)]
        public bool KeywordCaseSensitive { get; set; }

        public int OnExecute(IConsole console, CommandLineApplication app)
        {
            // ---- Validation rules per contracts/cli.md §5 ----
            if (!string.IsNullOrEmpty(Profile) && !string.IsNullOrEmpty(SourcesCsv))
            {
                console.Error.WriteLine("--profile and --sources are mutually exclusive.");
                return 2;
            }
            if (string.IsNullOrEmpty(Profile) && string.IsNullOrEmpty(SourcesCsv))
            {
                // Default to live-triage so `forens collect --output ./out` works
                // out-of-the-box and pulls every applicable source on the host.
                Profile = "live-triage";
                console.WriteLine("(no --sources / --profile given; defaulting to --profile live-triage)");
            }
            if (!string.IsNullOrEmpty(Profile) && !CollectionProfiles.TryGet(Profile, out _))
            {
                console.Error.WriteLine("Unknown profile: " + Profile);
                console.Error.WriteLine("Valid profiles:");
                foreach (var p in CollectionProfiles.All)
                    console.Error.WriteLine("  " + p.Name + " — " + p.Description);
                return 2;
            }
            if (Verbose && Quiet)
            {
                console.Error.WriteLine("--verbose and --quiet are mutually exclusive.");
                return 2;
            }

            DateTimeOffset? timeFrom = null, timeTo = null;
            try
            {
                if (!string.IsNullOrEmpty(FromIso))
                    timeFrom = TimeRange.ParseStrictUtc(FromIso, "--from");
                if (!string.IsNullOrEmpty(ToIso))
                    timeTo = TimeRange.ParseStrictUtc(ToIso, "--to");
            }
            catch (FormatException ex)
            {
                console.Error.WriteLine(ex.Message);
                return 2;
            }
            if (timeFrom.HasValue && timeTo.HasValue && timeFrom.Value > timeTo.Value)
            {
                console.Error.WriteLine("--from must be <= --to.");
                return 2;
            }

            CollectionProfile selectedProfile = null;
            if (!string.IsNullOrEmpty(Profile))
                CollectionProfiles.TryGet(Profile, out selectedProfile);

            int parallelism = Parallelism ?? selectedProfile?.Parallelism ?? Math.Min(Environment.ProcessorCount, 8);
            if (parallelism < 1)
            {
                console.Error.WriteLine("--parallelism must be >= 1.");
                return 2;
            }

            int memoryCeilingMB = MemoryCeilingMB ?? selectedProfile?.MemoryCeilingMB ?? 512;
            if (memoryCeilingMB < 64)
            {
                console.Error.WriteLine("--memory-ceiling-mb must be >= 64.");
                return 2;
            }

            long diskFloorMB = DiskFloorMB ?? (selectedProfile != null
                ? selectedProfile.DiskFloorBytes / (1024L * 1024L)
                : 1024);
            if (diskFloorMB < 0)
            {
                console.Error.WriteLine("--disk-floor-mb must be >= 0.");
                return 2;
            }

            var sources = ParseCsv(SourcesCsv);
            var noTimeFilter = new HashSet<string>(ParseCsv(NoTimeFilterCsv), StringComparer.Ordinal);

            List<int> pids = new List<int>();
            foreach (var p in ParseCsv(PidsCsv))
            {
                int pid;
                if (!int.TryParse(p, out pid) || pid <= 0)
                {
                    console.Error.WriteLine("--pid values must be positive integers (got '" + p + "').");
                    return 2;
                }
                pids.Add(pid);
            }
            var processNames = ParseCsv(ProcessNamesCsv);

            // ---- Catalog discovery + unknown-id check (FR-013) ----
            SourceCatalog catalog;
            try { catalog = SourceCatalog.Discover(); }
            catch (Exception ex)
            {
                console.Error.WriteLine("Failed to load source catalog: " + ex.Message);
                return 5;
            }

            // ---- Profile resolution (after catalog discovery) ----
            if (selectedProfile != null)
            {
                sources = selectedProfile.ResolveSourceIds(catalog);
                if (sources.Count == 0)
                {
                    console.Error.WriteLine("Profile '" + selectedProfile.Name +
                        "' resolved to zero sources on this catalog.");
                    return 2;
                }
            }

            var unknown = sources.Where(id => !catalog.Contains(id)).ToList();
            if (unknown.Count > 0)
            {
                console.Error.WriteLine("Unknown source id(s): " + string.Join(", ", unknown));
                console.Error.WriteLine("Valid ids:");
                foreach (var s in catalog.Sources)
                    console.Error.WriteLine("  " + s.Metadata.Id);
                return 2;
            }

            var resolvedSources = sources.Select(catalog.Get).ToList();

            // ---- For profile runs, automatically suppress time-filter rejection
            //      for sources that don't support it (FR-006 / Constitution III's
            //      escape hatch is implicit when a profile is selected). ----
            if (selectedProfile != null && (timeFrom.HasValue || timeTo.HasValue))
            {
                foreach (var s in resolvedSources)
                {
                    if (!s.Metadata.SupportsTimeRange)
                        noTimeFilter.Add(s.Metadata.Id);
                }
            }

            // ---- Time-filter on non-time-aware source rule (Constitution III) ----
            if (timeFrom.HasValue || timeTo.HasValue)
            {
                foreach (var s in resolvedSources)
                {
                    if (!s.Metadata.SupportsTimeRange && !noTimeFilter.Contains(s.Metadata.Id))
                    {
                        console.Error.WriteLine(
                            "Source '" + s.Metadata.Id + "' does not support time-range scoping; " +
                            "add it to --no-time-filter to acknowledge.");
                        return 2;
                    }
                }
            }

            // ---- Build process filter and pre-run rows ----
            var processFilter = new ProcessFilterCriteria(pids, processNames, ResolveImagePaths(processNames));
            var elevation = HostInfo.Elevation;
            var hostName = HostInfo.MachineName;
            var hostOs = HostInfo.OsVersionString;

            var preRunRows = new List<PreRunSummary.SourceRow>();
            using (var probeLogger = LoggerFactory.CreateRunLogger(
                Path.Combine(Path.GetTempPath(), "forens-precheck-" + Guid.NewGuid().ToString("N") + ".log"),
                ResolveVerbosity()))
            using (var dummyCts = new System.Threading.CancellationTokenSource())
            {
                var ctxForCheck = new CollectionContext(
                    runId: Guid.Empty,
                    outputDir: OutputRoot,
                    timeFrom: timeFrom,
                    timeTo: timeTo,
                    processFilter: new ProcessFilter(processFilter),
                    elevation: elevation,
                    hostOsVersion: HostInfo.OsVersion,
                    cancellationToken: dummyCts.Token,
                    logger: probeLogger);

                foreach (var s in resolvedSources)
                {
                    var pre = s.CheckPrecondition(ctxForCheck);
                    preRunRows.Add(new PreRunSummary.SourceRow
                    {
                        Id = s.Metadata.Id,
                        DisplayName = s.Metadata.DisplayName,
                        RequiresElevation = s.Metadata.RequiresElevation,
                        Precondition = pre.Result,
                        PreconditionReason = pre.Reason
                    });
                }
            }

            var runId = Guid.NewGuid();
            string runDirHint = "forens-" + hostName + "-" + DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHH-mm-ssZ");
            PreRunSummary.Print(console, Profile, runId, runDirHint, elevation,
                timeFrom, timeTo, pids, processNames, preRunRows);

            if (DryRun)
            {
                console.WriteLine();
                console.WriteLine("--dry-run: not collecting.");
                return 0;
            }

            // ---- Run collection ----
            var asm = typeof(CollectCommand).Assembly;
            string toolVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            string gitCommit = VersionCommand.ReadMetadata(asm, "GitCommit") ?? "unknown";

            var cfg = new CollectionConfiguration
            {
                OutputRoot = OutputRoot,
                SelectedSources = sources,
                ProfileName = Profile,
                CaseId = CaseId,
                TimeFrom = timeFrom,
                TimeTo = timeTo,
                ProcessFilter = processFilter,
                NoTimeFilterFor = noTimeFilter.ToArray(),
                Parallelism = parallelism,
                MemoryCeilingMB = memoryCeilingMB,
                DiskFloorBytes = diskFloorMB * 1024L * 1024L,
                Verbosity = ResolveVerbosity(),
                ToolVersion = toolVersion,
                GitCommit = gitCommit,
                Cli = ReconstructCli()
            };

            CollectionRun run;
            try
            {
                var runner = new CollectionRunner(catalog);
                run = runner.Run(cfg);
            }
            catch (Exception ex)
            {
                console.Error.WriteLine("Internal error: " + ex.Message);
                return 5;
            }

            WriteReports(run, catalog);

            console.WriteLine();
            console.WriteLine(string.Format("Run {0} {1}: {2} succeeded, {3} partial, {4} skipped, {5} failed",
                run.RunId, run.Status,
                run.Results.Count(r => r.Status == SourceStatus.Succeeded),
                run.Results.Count(r => r.Status == SourceStatus.Partial),
                run.Results.Count(r => r.Status == SourceStatus.Skipped),
                run.Results.Count(r => r.Status == SourceStatus.Failed)));
            console.WriteLine("Output: " + run.OutputRoot);

            // ---- Post-collection keyword aggregation ----
            if (Keywords != null && Keywords.Length > 0)
            {
                MaybeRunKeywordSearch(console, run.OutputRoot);
            }

            return MapExitCode(run);
        }

        private void MaybeRunKeywordSearch(IConsole console, string runDir)
        {
            try
            {
                var result = Forens.Cli.Search.KeywordSearchEngine.Run(
                    runDir: runDir,
                    keywords: Keywords,
                    caseSensitive: KeywordCaseSensitive);
                console.WriteLine();
                console.WriteLine(string.Format(
                    "forens search ({0}): {1} match{2} across {3} source{4} in {5} file{6} ({7} line{8} scanned)",
                    string.Join(",", Keywords),
                    result.TotalMatches, result.TotalMatches == 1 ? "" : "es",
                    result.MatchesPerSource.Count, result.MatchesPerSource.Count == 1 ? "" : "s",
                    result.FilesScanned, result.FilesScanned == 1 ? "" : "s",
                    result.TotalLinesScanned, result.TotalLinesScanned == 1 ? "" : "s"));
                console.WriteLine("  output: " + result.OutputPath);
                console.WriteLine("  summary: " + result.SummaryPath);
            }
            catch (Exception ex)
            {
                console.Error.WriteLine("Post-collection keyword search failed (collection itself succeeded): " + ex.Message);
            }
        }

        private LogVerbosity ResolveVerbosity()
        {
            if (Verbose) return LogVerbosity.Verbose;
            if (Quiet) return LogVerbosity.Quiet;
            return LogVerbosity.Normal;
        }

        private static int MapExitCode(CollectionRun run)
        {
            switch (run.Status)
            {
                case RunStatus.Completed: return 0;
                case RunStatus.CompletedWithErrors: return 3;
                case RunStatus.StoppedDiskSpace: return 4;
                case RunStatus.Aborted: return 4;
                default: return 0;
            }
        }

        private static IReadOnlyList<string> ParseCsv(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<string>();
            return csv.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        }

        private IReadOnlyList<string> ResolveImagePaths(IReadOnlyList<string> processNames)
        {
            if (processNames == null || processNames.Count == 0) return Array.Empty<string>();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                {
                    using (p)
                    {
                        try
                        {
                            string name = p.ProcessName;
                            foreach (var nameQuery in processNames)
                            {
                                if (string.Equals(name, nameQuery, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(name + ".exe", nameQuery, StringComparison.OrdinalIgnoreCase))
                                {
                                    try { if (p.MainModule != null) set.Add(p.MainModule.FileName); } catch { }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return set.ToArray();
        }

        private IReadOnlyList<string> ReconstructCli()
        {
            var args = new List<string> { "forens", "collect" };
            if (!string.IsNullOrEmpty(SourcesCsv)) { args.Add("--sources"); args.Add(SourcesCsv); }
            if (!string.IsNullOrEmpty(Profile)) { args.Add("--profile"); args.Add(Profile); }
            if (!string.IsNullOrEmpty(FromIso)) { args.Add("--from"); args.Add(FromIso); }
            if (!string.IsNullOrEmpty(ToIso)) { args.Add("--to"); args.Add(ToIso); }
            if (!string.IsNullOrEmpty(PidsCsv)) { args.Add("--pid"); args.Add(PidsCsv); }
            if (!string.IsNullOrEmpty(ProcessNamesCsv)) { args.Add("--process-name"); args.Add(ProcessNamesCsv); }
            if (!string.IsNullOrEmpty(OutputRoot)) { args.Add("--output"); args.Add(OutputRoot); }
            if (!string.IsNullOrEmpty(CaseId)) { args.Add("--case-id"); args.Add(CaseId); }
            if (Parallelism.HasValue) { args.Add("--parallelism"); args.Add(Parallelism.Value.ToString()); }
            if (MemoryCeilingMB.HasValue) { args.Add("--memory-ceiling-mb"); args.Add(MemoryCeilingMB.Value.ToString()); }
            if (DiskFloorMB.HasValue) { args.Add("--disk-floor-mb"); args.Add(DiskFloorMB.Value.ToString()); }
            if (DryRun) args.Add("--dry-run");
            if (Verbose) args.Add("--verbose");
            if (Quiet) args.Add("--quiet");
            return args;
        }

        private void WriteReports(CollectionRun run, SourceCatalog catalog)
        {
            try
            {
                var displayNames = new Dictionary<string, string>(StringComparer.Ordinal);
                var categoryNames = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var s in catalog.Sources)
                {
                    displayNames[s.Metadata.Id] = s.Metadata.DisplayName;
                    categoryNames[s.Metadata.Id] = s.Metadata.Category.ToString();
                }
                var model = ReportModelFactory.FromCollectionRun(run, displayNames, categoryNames);
                JsonReportWriter.WriteToFile(model, Path.Combine(run.OutputRoot, "report.json"));
                HtmlReportWriter.WriteToFile(model, Path.Combine(run.OutputRoot, "report.html"));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Warning: failed to write report: " + ex.Message);
            }
        }
    }
}
