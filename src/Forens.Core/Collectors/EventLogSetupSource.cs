using System;
using Forens.Core.Collection;
using Forens.Core.Collectors.EventLog;

namespace Forens.Core.Collectors
{
    public sealed class EventLogSetupSource : IArtifactSource
    {
        public const string SourceId = "eventlog-setup";
        private const string ChannelName = "Setup";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Windows Event Log: Setup",
            description: "Events from the Windows Setup channel (servicing, updates).",
            category: Category.System,
            requiresElevation: false,
            supportsTimeRange: true,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: Array.Empty<ContendedResource>(),
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
