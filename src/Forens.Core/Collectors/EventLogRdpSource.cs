using System;
using Forens.Core.Collection;
using Forens.Core.Collectors.EventLog;

namespace Forens.Core.Collectors
{
    public sealed class EventLogRdpSource : IArtifactSource
    {
        public const string SourceId = "eventlog-rdp";
        private const string ChannelName = "Microsoft-Windows-TerminalServices-LocalSessionManager/Operational";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Windows Event Log: RDP / Terminal Services",
            description: "Local Session Manager operational events (RDP logon/logoff/reconnect).",
            category: Category.Network,
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
