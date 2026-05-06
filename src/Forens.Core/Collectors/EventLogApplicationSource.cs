using System;
using Forens.Core.Collection;
using Forens.Core.Collectors.EventLog;

namespace Forens.Core.Collectors
{
    public sealed class EventLogApplicationSource : IArtifactSource
    {
        public const string SourceId = "eventlog-application";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Windows Event Log: Application",
            description: "Events from the Windows Application channel, optionally scoped by --from/--to.",
            category: Category.System,
            requiresElevation: false,
            supportsTimeRange: true,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: new[] { ContendedResource.EventLogApplication },
            estimatedMemoryMB: 64,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx) { return SourcePrecondition.Ok(); }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            EventLogCollectorHelper.Collect("Application", "events.jsonl", ctx, writer);
        }

        // Kept for the existing EventLogApplicationSourceTests that pinned to internals.
        internal static string BuildXPath(DateTimeOffset? from, DateTimeOffset? to)
        {
            return EventLogCollectorHelper.BuildXPath(from, to);
        }
    }
}
