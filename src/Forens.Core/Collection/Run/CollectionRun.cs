using System;
using System.Collections.Generic;
using Forens.Common.Host;

namespace Forens.Core.Collection.Run
{
    public enum RunStatus
    {
        Running,
        Completed,
        CompletedWithErrors,
        StoppedDiskSpace,
        Aborted
    }

    public sealed class CollectionRun
    {
        public CollectionRun(
            Guid runId,
            string toolVersion,
            string gitCommit,
            string hostName,
            string hostOsVersion,
            string operatorAccount,
            ElevationState elevation,
            string caseId,
            string profileName,
            IReadOnlyList<string> requestedSources,
            DateTimeOffset? timeFrom,
            DateTimeOffset? timeTo,
            ProcessFilterCriteria processFilter,
            IReadOnlyList<string> cli,
            string outputRoot,
            DateTimeOffset startedUtc,
            DateTimeOffset? completedUtc,
            RunStatus status,
            string statusReason,
            IReadOnlyList<SourceResult> results)
        {
            RunId = runId;
            ToolVersion = toolVersion ?? "0.0.0";
            GitCommit = gitCommit ?? "unknown";
            HostName = hostName ?? "";
            HostOsVersion = hostOsVersion ?? "";
            OperatorAccount = operatorAccount ?? "";
            Elevation = elevation;
            CaseId = caseId;
            ProfileName = profileName;
            RequestedSources = requestedSources ?? Array.Empty<string>();
            TimeFrom = timeFrom;
            TimeTo = timeTo;
            ProcessFilter = processFilter;
            Cli = cli ?? Array.Empty<string>();
            OutputRoot = outputRoot ?? "";
            StartedUtc = startedUtc;
            CompletedUtc = completedUtc;
            Status = status;
            StatusReason = statusReason;
            Results = results ?? Array.Empty<SourceResult>();
        }

        public Guid RunId { get; }
        public string ToolVersion { get; }
        public string GitCommit { get; }
        public string HostName { get; }
        public string HostOsVersion { get; }
        public string OperatorAccount { get; }
        public ElevationState Elevation { get; }
        public string CaseId { get; }
        public string ProfileName { get; }
        public IReadOnlyList<string> RequestedSources { get; }
        public DateTimeOffset? TimeFrom { get; }
        public DateTimeOffset? TimeTo { get; }
        public ProcessFilterCriteria ProcessFilter { get; }
        public IReadOnlyList<string> Cli { get; }
        public string OutputRoot { get; }
        public DateTimeOffset StartedUtc { get; }
        public DateTimeOffset? CompletedUtc { get; }
        public RunStatus Status { get; }
        public string StatusReason { get; }
        public IReadOnlyList<SourceResult> Results { get; }
    }
}
