using Forens.Common.Host;
using Forens.Core.Collection;
using Forens.Core.Collectors.EventLog;

namespace Forens.Core.Collectors
{
    public sealed class EventLogSystemSource : IArtifactSource
    {
        public const string SourceId = "eventlog-system";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Windows Event Log: System",
            description: "Events from the Windows System channel, optionally scoped by --from/--to.",
            category: Category.System,
            requiresElevation: false,
            supportsTimeRange: true,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: new[] { ContendedResource.EventLogSystem },
            estimatedMemoryMB: 64,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx) { return SourcePrecondition.Ok(); }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            EventLogCollectorHelper.Collect("System", "events.jsonl", ctx, writer);
        }
    }
}
