using System;
using System.Management;
using Forens.Core.Collection;

namespace Forens.Core.Collectors
{
    public sealed class ServicesSource : IArtifactSource
    {
        public const string SourceId = "services";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Windows Services",
            description: "All Windows services with state, start mode, image path, and service account.",
            category: Category.Persistence,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: true,
            processFilterMode: ProcessFilterMode.LivePid,
            contendedResources: new[] { ContendedResource.WmiCimV2 },
            estimatedMemoryMB: 16,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            using (var jl = writer.OpenJsonlFile("services.jsonl"))
            {
                ManagementObjectCollection collection;
                try
                {
                    var searcher = new ManagementObjectSearcher(
                        "SELECT Name, DisplayName, Description, State, StartMode, PathName, StartName, ProcessId, AcceptStop, AcceptPause FROM Win32_Service");
                    collection = searcher.Get();
                }
                catch (Exception ex)
                {
                    ctx.Logger.Warning(ex, "WMI Win32_Service query failed");
                    writer.RecordPartial("WMI Win32_Service query failed: " + ex.Message);
                    return;
                }

                try
                {
                    foreach (var mo in collection)
                    {
                        ctx.CancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            int pid = ToInt(mo["ProcessId"]);
                            if (pid > 0 && !ctx.ProcessFilter.Includes(pid)) continue;

                            var record = new ServiceRecord
                            {
                                Name = mo["Name"] as string,
                                DisplayName = mo["DisplayName"] as string,
                                Description = mo["Description"] as string,
                                State = mo["State"] as string,
                                StartMode = mo["StartMode"] as string,
                                PathName = mo["PathName"] as string,
                                ServiceAccount = mo["StartName"] as string,
                                ProcessId = pid > 0 ? (int?)pid : null,
                                AcceptStop = mo["AcceptStop"] as bool?,
                                AcceptPause = mo["AcceptPause"] as bool?
                            };
                            jl.Write(record);
                            writer.RecordItem();
                        }
                        finally { mo.Dispose(); }
                    }
                }
                finally { collection.Dispose(); }
            }
        }

        private static int ToInt(object o)
        {
            if (o == null) return 0;
            try { return Convert.ToInt32(o); } catch { return 0; }
        }

        private sealed class ServiceRecord
        {
            public string Name { get; set; }
            public string DisplayName { get; set; }
            public string Description { get; set; }
            public string State { get; set; }
            public string StartMode { get; set; }
            public string PathName { get; set; }
            public string ServiceAccount { get; set; }
            public int? ProcessId { get; set; }
            public bool? AcceptStop { get; set; }
            public bool? AcceptPause { get; set; }
        }
    }
}
