using System;
using Forens.Core.Collection;
using Forens.Core.Collectors.EventLog;

namespace Forens.Core.Collectors
{
    public sealed class EventLogPowerShellSource : IArtifactSource
    {
        public const string SourceId = "eventlog-powershell";
        private const string ChannelName = "Microsoft-Windows-PowerShell/Operational";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Windows Event Log: PowerShell Operational",
            description: "PowerShell engine and pipeline operational events (incl. ScriptBlock logging when enabled).",
            category: Category.Persistence,
            requiresElevation: false,
            supportsTimeRange: true,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: Array.Empty<ContendedResource>(),
            estimatedMemoryMB: 64,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return EventLogCollectorHelper.Preflight(ChannelName);
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            EventLogCollectorHelper.Collect(ChannelName, "events.jsonl", ctx, writer);
        }
    }
}
