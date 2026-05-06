using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Forens.Common.Host;
using Forens.Common.Logging;
using Forens.Core.Collection.Run;
using Serilog;

namespace Forens.Core.Collection
{
    public sealed class CollectionRunner
    {
        private readonly SourceCatalog _catalog;

        public CollectionRunner(SourceCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public CollectionRun Run(CollectionConfiguration config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrEmpty(config.OutputRoot))
                throw new ArgumentException("OutputRoot is required.", nameof(config));
            if (config.SelectedSources == null || config.SelectedSources.Count == 0)
                throw new ArgumentException("At least one source must be selected.", nameof(config));

            var startedUtc = DateTimeOffset.UtcNow;
            var runId = Guid.NewGuid();
            var hostName = HostInfo.MachineName;
            var hostOs = HostInfo.OsVersionString;
            var operatorAccount = HostInfo.OperatorAccount;
            var elevation = HostInfo.Elevation;
            var runDir = Path.Combine(config.OutputRoot, FormatRunDirName(hostName, startedUtc));
            Directory.CreateDirectory(runDir);
            string rawDir = Path.Combine(runDir, "raw");
            Directory.CreateDirectory(rawDir);
            string runLogPath = Path.Combine(runDir, "run.log");

            using (var logger = LoggerFactory.CreateRunLogger(runLogPath, config.Verbosity))
            using (var cts = new CancellationTokenSource())
            {
                var runLogger = logger.ForContext("RunId", runId.ToString("D"));
                runLogger.Information("Forens run {RunId} starting on {Host} ({Os}) as {Account}; elevation={Elevation}",
                    runId, hostName, hostOs, operatorAccount, elevation);

                using (var watchdog = DiskFloorWatchdog.ForPath(runDir, config.DiskFloorBytes, cts,
                    reason => runLogger.Warning("Disk floor watchdog triggered: {Reason}", reason)))
                {
                    var resolved = ResolveSelectedSources(config, runLogger);
                    var split = SplitByPrecondition(
                        resolved, config, hostName, hostOs, elevation,
                        rawDir, cts.Token, runLogger);

                    var results = new System.Collections.Concurrent.ConcurrentBag<SourceResult>();
                    foreach (var s in split.PreSkips) results.Add(s);

                    var scheduler = new Scheduler(Math.Max(1, config.Parallelism));
                    try
                    {
                        scheduler.RunAsync(split.ToRun, async (src, ct) =>
                        {
                            await Task.Yield();
                            results.Add(RunOne(src, config, hostName, hostOs, elevation, rawDir, ct, runLogger));
                        }, cts.Token).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        runLogger.Warning("Run cancelled.");
                    }

                    var allResults = ResultListInOriginalOrder(config.SelectedSources, results);

                    var runStatus = ComputeRunStatus(allResults, watchdog);
                    string statusReason = watchdog.Triggered ? watchdog.Reason : null;
                    var completedUtc = DateTimeOffset.UtcNow;

                    var run = new CollectionRun(
                        runId, config.ToolVersion, config.GitCommit,
                        hostName, hostOs, operatorAccount, elevation,
                        config.CaseId, config.ProfileName,
                        config.SelectedSources,
                        config.TimeFrom, config.TimeTo,
                        config.ProcessFilter,
                        config.Cli,
                        runDir, startedUtc, completedUtc, runStatus, statusReason,
                        allResults);

                    runLogger.Information("Run {RunId} completed: status={Status}, sources={SourceCount}, elapsed={ElapsedMs}ms",
                        runId, runStatus, allResults.Count, (completedUtc - startedUtc).TotalMilliseconds);

                    WriteOutputs(run, runDir);
                    return run;
                }
            }
        }

        private List<IArtifactSource> ResolveSelectedSources(CollectionConfiguration config, ILogger logger)
        {
            var list = new List<IArtifactSource>(config.SelectedSources.Count);
            foreach (var id in config.SelectedSources)
            {
                if (!_catalog.TryGet(id, out var src))
                    throw new InvalidOperationException("Unknown source id: " + id);
                list.Add(src);
            }
            return list;
        }

        private sealed class PreconditionSplit
        {
            public List<IArtifactSource> ToRun { get; set; }
            public List<SourceResult> PreSkips { get; set; }
        }

        private PreconditionSplit SplitByPrecondition(
            IEnumerable<IArtifactSource> sources,
            CollectionConfiguration config,
            string hostName, string hostOs, ElevationState elevation,
            string rawDir, CancellationToken ct, ILogger logger)
        {
            var toRun = new List<IArtifactSource>();
            var preSkips = new List<SourceResult>();

            var ctxForCheck = new CollectionContext(
                runId: Guid.Empty,
                outputDir: rawDir,
                timeFrom: config.TimeFrom,
                timeTo: config.TimeTo,
                processFilter: config.ProcessFilter == null ? ProcessFilter.Empty : new ProcessFilter(config.ProcessFilter),
                elevation: elevation,
                hostOsVersion: ParseOsVersion(hostOs),
                cancellationToken: ct,
                logger: logger);

            foreach (var src in sources)
            {
                var pre = src.CheckPrecondition(ctxForCheck);
                if (pre.Result == PreconditionResult.Ok)
                {
                    toRun.Add(src);
                }
                else
                {
                    var now = DateTimeOffset.UtcNow;
                    preSkips.Add(new SourceResult(
                        src.Metadata.Id, SourceStatus.Skipped, pre.Reason,
                        now, now, 0, 0, Array.Empty<OutputFile>(), null));
                    logger.Information("Source {SourceId} skipped: {Reason}", src.Metadata.Id, pre.Reason);
                }
            }
            return new PreconditionSplit { ToRun = toRun, PreSkips = preSkips };
        }

        private SourceResult RunOne(
            IArtifactSource src,
            CollectionConfiguration config,
            string hostName, string hostOs, ElevationState elevation,
            string rawDir, CancellationToken ct, ILogger logger)
        {
            string sourceDir = Path.Combine(rawDir, src.Metadata.Id);
            string posixRel = "raw/" + src.Metadata.Id;
            var startedUtc = DateTimeOffset.UtcNow;

            using (var writer = new StreamingOutputWriter(sourceDir, posixRel))
            {
                var sourceLogger = logger.ForContext("SourceId", src.Metadata.Id);
                var ctx = new CollectionContext(
                    runId: Guid.Empty,
                    outputDir: sourceDir,
                    timeFrom: config.TimeFrom,
                    timeTo: config.TimeTo,
                    processFilter: config.ProcessFilter == null ? ProcessFilter.Empty : new ProcessFilter(config.ProcessFilter),
                    elevation: elevation,
                    hostOsVersion: ParseOsVersion(hostOs),
                    cancellationToken: ct,
                    logger: sourceLogger);

                try
                {
                    sourceLogger.Information("Source {SourceId} starting", src.Metadata.Id);
                    src.Collect(ctx, writer);
                    var completedUtc = DateTimeOffset.UtcNow;
                    var status = writer.IsPartial ? SourceStatus.Partial : SourceStatus.Succeeded;
                    sourceLogger.Information("Source {SourceId} {Status}: {Items} items, {Bytes} bytes, {ElapsedMs}ms",
                        src.Metadata.Id, status, writer.ItemsCollected, writer.TotalBytesWritten,
                        (completedUtc - startedUtc).TotalMilliseconds);
                    return new SourceResult(
                        src.Metadata.Id, status,
                        status == SourceStatus.Partial ? writer.PartialReason : null,
                        startedUtc, completedUtc,
                        writer.ItemsCollected, writer.TotalBytesWritten,
                        writer.FinishedFiles, null);
                }
                catch (OperationCanceledException)
                {
                    var completedUtc = DateTimeOffset.UtcNow;
                    sourceLogger.Warning("Source {SourceId} cancelled mid-run", src.Metadata.Id);
                    return new SourceResult(
                        src.Metadata.Id, SourceStatus.Partial, "Cancelled",
                        startedUtc, completedUtc,
                        writer.ItemsCollected, writer.TotalBytesWritten,
                        writer.FinishedFiles, null);
                }
                catch (Exception ex)
                {
                    var completedUtc = DateTimeOffset.UtcNow;
                    sourceLogger.Error(ex, "Source {SourceId} failed", src.Metadata.Id);
                    var err = ErrorRecord.FromException(ex);
                    var status = writer.FinishedFiles.Count > 0 ? SourceStatus.Partial : SourceStatus.Failed;
                    return new SourceResult(
                        src.Metadata.Id, status, ex.Message,
                        startedUtc, completedUtc,
                        writer.ItemsCollected, writer.TotalBytesWritten,
                        writer.FinishedFiles, err);
                }
            }
        }

        private static List<SourceResult> ResultListInOriginalOrder(
            IReadOnlyList<string> selectedIds,
            System.Collections.Concurrent.ConcurrentBag<SourceResult> results)
        {
            var byId = new Dictionary<string, SourceResult>(StringComparer.Ordinal);
            foreach (var r in results) byId[r.SourceId] = r;
            var ordered = new List<SourceResult>(selectedIds.Count);
            foreach (var id in selectedIds)
            {
                if (byId.TryGetValue(id, out var r)) ordered.Add(r);
            }
            return ordered;
        }

        private static RunStatus ComputeRunStatus(IReadOnlyList<SourceResult> results, DiskFloorWatchdog watchdog)
        {
            if (watchdog.Triggered) return RunStatus.StoppedDiskSpace;
            bool anyError = false;
            foreach (var r in results)
            {
                if (r.Status == SourceStatus.Failed || r.Status == SourceStatus.Partial)
                {
                    anyError = true;
                    break;
                }
            }
            return anyError ? RunStatus.CompletedWithErrors : RunStatus.Completed;
        }

        private void WriteOutputs(CollectionRun run, string runDir)
        {
            string manifestPath = Path.Combine(runDir, "manifest.json");
            ManifestBuilder.WriteToFile(run, manifestPath);
        }

        public IReadOnlyDictionary<string, SourceMetadata> CatalogMetadataSnapshot()
        {
            var dict = new Dictionary<string, SourceMetadata>(StringComparer.Ordinal);
            foreach (var src in _catalog.Sources) dict[src.Metadata.Id] = src.Metadata;
            return dict;
        }

        private static string FormatRunDirName(string hostName, DateTimeOffset startedUtc)
        {
            string safeHost = string.IsNullOrEmpty(hostName) ? "host" : hostName.Replace(' ', '_');
            string ts = startedUtc.UtcDateTime.ToString("yyyy-MM-ddTHH-mm-ssZ");
            return string.Format("forens-{0}-{1}", safeHost, ts);
        }

        private static Version ParseOsVersion(string osVersionString)
        {
            if (string.IsNullOrEmpty(osVersionString)) return new Version(0, 0);
            var parts = osVersionString.Split(' ');
            string last = parts[parts.Length - 1];
            Version v;
            return Version.TryParse(last, out v) ? v : new Version(0, 0);
        }
    }
}
