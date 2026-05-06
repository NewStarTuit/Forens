using Forens.Common.Host;
using Forens.Core.Collection;
using Forens.Core.Collectors.EventLog;

namespace Forens.Core.Collectors
{
    public sealed class EventLogSecuritySource : IArtifactSource
    {
        public const string SourceId = "eventlog-security";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Windows Event Log: Security",
            description: "Events from the Windows Security channel (requires elevation), optionally scoped by --from/--to.",
            category: Category.Persistence,
            requiresElevation: true,
            supportsTimeRange: true,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: new[] { ContendedResource.EventLogSecurity },
            estimatedMemoryMB: 128,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            if (ctx.Elevation != ElevationState.Elevated)
            {
                return SourcePrecondition.Skip(
                    PreconditionResult.SkipRequiresElevation,
                    "Reading the Security channel requires administrator privileges.");
            }
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            EventLogCollectorHelper.Collect("Security", "events.jsonl", ctx, writer);
        }
    }
}
