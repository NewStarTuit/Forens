using System;
using System.Collections.Generic;
using Forens.Common.Logging;
using Forens.Core.Collection.Run;

namespace Forens.Core.Collection
{
    public sealed class CollectionConfiguration
    {
        public string OutputRoot { get; set; }
        public IReadOnlyList<string> SelectedSources { get; set; } = Array.Empty<string>();
        public string ProfileName { get; set; }
        public string CaseId { get; set; }
        public DateTimeOffset? TimeFrom { get; set; }
        public DateTimeOffset? TimeTo { get; set; }
        public ProcessFilterCriteria ProcessFilter { get; set; }
        public IReadOnlyList<string> NoTimeFilterFor { get; set; } = Array.Empty<string>();
        public int Parallelism { get; set; } = Math.Min(Environment.ProcessorCount, 8);
        public long MemoryCeilingMB { get; set; } = 512;
        public long DiskFloorBytes { get; set; } = 1024L * 1024 * 1024;
        public LogVerbosity Verbosity { get; set; } = LogVerbosity.Normal;
        public string ToolVersion { get; set; } = "0.0.0";
        public string GitCommit { get; set; } = "unknown";
        public IReadOnlyList<string> Cli { get; set; } = Array.Empty<string>();
    }
}
