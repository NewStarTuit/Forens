using System;
using Forens.Core.Collection;
using Forens.Core.Collectors.EventLog;

namespace Forens.Core.Collectors
{
    public sealed class EventLogAppLockerSource : IArtifactSource
    {
        public const string SourceId = "eventlog-applocker";
        private const string ChannelName = "Microsoft-Windows-AppLocker/EXE and DLL";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Windows Event Log: AppLocker EXE/DLL",
            description: "AppLocker enforcement and audit events for executables and DLLs.",
            category: Category.Persistence,
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
