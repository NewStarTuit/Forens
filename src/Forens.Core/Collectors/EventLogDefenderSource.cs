using Forens.Core.Collection;
using Forens.Core.Collectors.EventLog;

namespace Forens.Core.Collectors
{
    public sealed class EventLogDefenderSource : IArtifactSource
    {
        public const string SourceId = "eventlog-defender";
        private const string ChannelName = "Microsoft-Windows-Windows Defender/Operational";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Windows Event Log: Defender Operational",
            description: "Events from Microsoft-Windows-Windows Defender/Operational (detections, scan results).",
            category: Category.Persistence,
            requiresElevation: false,
            supportsTimeRange: true,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: System.Array.Empty<ContendedResource>(),
            estimatedMemoryMB: 32,
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
