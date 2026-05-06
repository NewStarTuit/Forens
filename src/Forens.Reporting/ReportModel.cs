using System;
using System.Collections.Generic;
using Forens.Core.Collection.Run;

namespace Forens.Reporting
{
    public sealed class ReportModel
    {
        public ReportSchemaInfo Schema { get; set; }
        public RunSummary Run { get; set; }
        public List<ReportSection> Sections { get; set; }
    }

    public sealed class ReportSchemaInfo
    {
        public string Name { get; set; }
        public string Version { get; set; }

        public static ReportSchemaInfo Default
        {
            get { return new ReportSchemaInfo { Name = "forens-report", Version = "1.0.0" }; }
        }
    }

    public sealed class RunSummary
    {
        public string RunId { get; set; }
        public string Profile { get; set; }
        public string CaseId { get; set; }
        public RunHostSummary Host { get; set; }
        public DateTimeOffset StartedUtc { get; set; }
        public DateTimeOffset CompletedUtc { get; set; }
        public string Status { get; set; }
        public RunFilters Filters { get; set; }
    }

    public sealed class RunHostSummary
    {
        public string Name { get; set; }
        public string OsVersion { get; set; }
    }

    public sealed class RunFilters
    {
        public DateTimeOffset? TimeFrom { get; set; }
        public DateTimeOffset? TimeTo { get; set; }
        public List<int> Pids { get; set; }
        public List<string> ProcessNames { get; set; }
    }

    public sealed class ReportSection
    {
        public string SourceId { get; set; }
        public string DisplayName { get; set; }
        public string Category { get; set; }
        public string Status { get; set; }
        public string StatusReason { get; set; }
        public Dictionary<string, object> Summary { get; set; }
        public List<RawOutputRef> RawOutput { get; set; }
    }

    public sealed class RawOutputRef
    {
        public string Path { get; set; }
        public string Sha256 { get; set; }
        public long ByteCount { get; set; }
    }

    public static class ReportModelFactory
    {
        public static ReportModel FromCollectionRun(
            CollectionRun run,
            IReadOnlyDictionary<string, string> displayNamesById,
            IReadOnlyDictionary<string, string> categoryNamesById)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            displayNamesById = displayNamesById ?? new Dictionary<string, string>();
            categoryNamesById = categoryNamesById ?? new Dictionary<string, string>();

            var sections = new List<ReportSection>(run.Results.Count);
            foreach (var r in run.Results)
            {
                string display;
                if (!displayNamesById.TryGetValue(r.SourceId, out display)) display = r.SourceId;
                string category;
                if (!categoryNamesById.TryGetValue(r.SourceId, out category)) category = "System";

                var rawRefs = new List<RawOutputRef>(r.OutputFiles.Count);
                foreach (var f in r.OutputFiles)
                {
                    rawRefs.Add(new RawOutputRef
                    {
                        Path = f.RelativePath,
                        Sha256 = f.Sha256,
                        ByteCount = f.ByteCount
                    });
                }

                sections.Add(new ReportSection
                {
                    SourceId = r.SourceId,
                    DisplayName = display,
                    Category = category,
                    Status = r.Status.ToString(),
                    StatusReason = r.StatusReason,
                    Summary = new Dictionary<string, object>
                    {
                        { "itemsCollected", r.ItemsCollected },
                        { "bytesWritten", r.BytesWritten },
                        { "elapsedMs", r.ElapsedMs }
                    },
                    RawOutput = rawRefs
                });
            }

            return new ReportModel
            {
                Schema = ReportSchemaInfo.Default,
                Run = new RunSummary
                {
                    RunId = run.RunId.ToString("D"),
                    Profile = run.ProfileName,
                    CaseId = string.IsNullOrEmpty(run.CaseId) ? null : run.CaseId,
                    Host = new RunHostSummary { Name = run.HostName, OsVersion = run.HostOsVersion },
                    StartedUtc = run.StartedUtc,
                    CompletedUtc = run.CompletedUtc ?? run.StartedUtc,
                    Status = run.Status.ToString(),
                    Filters = BuildFilters(run)
                },
                Sections = sections
            };
        }

        private static RunFilters BuildFilters(CollectionRun run)
        {
            var f = new RunFilters
            {
                TimeFrom = run.TimeFrom,
                TimeTo = run.TimeTo
            };
            if (run.ProcessFilter != null)
            {
                if (run.ProcessFilter.Pids.Count > 0) f.Pids = new List<int>(run.ProcessFilter.Pids);
                if (run.ProcessFilter.ProcessNames.Count > 0)
                    f.ProcessNames = new List<string>(run.ProcessFilter.ProcessNames);
            }
            return f;
        }
    }
}
