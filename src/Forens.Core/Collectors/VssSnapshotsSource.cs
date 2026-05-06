using System;
using System.Management;
using Forens.Core.Collection;

namespace Forens.Core.Collectors
{
    public sealed class VssSnapshotsSource : IArtifactSource
    {
        public const string SourceId = "vss-snapshots";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Volume Shadow Copies",
            description: "Enumeration of Volume Shadow Copy snapshots via WMI Win32_ShadowCopy. Lists snapshots only; does not mount or copy contents.",
            category: Category.Filesystem,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: new[] { ContendedResource.WmiCimV2 },
            estimatedMemoryMB: 8,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            using (var jl = writer.OpenJsonlFile("vss-snapshots.jsonl"))
            {
                ManagementObjectCollection collection;
                try
                {
                    var searcher = new ManagementObjectSearcher(
                        "SELECT ID, InstallDate, OriginatingMachine, ServiceMachine, VolumeName, DeviceObject, ExposedName, ExposedPath, State, Persistent, ProviderID, ClientAccessible, NotSurfaced FROM Win32_ShadowCopy");
                    collection = searcher.Get();
                }
                catch (ManagementException ex) when (
                    ex.ErrorCode == ManagementStatus.AccessDenied ||
                    ex.ErrorCode == ManagementStatus.AccessDenied)
                {
                    writer.RecordPartial("WMI access denied for Win32_ShadowCopy");
                    return;
                }
                catch (Exception ex)
                {
                    ctx.Logger.Warning(ex, "WMI Win32_ShadowCopy query failed");
                    writer.RecordPartial("WMI Win32_ShadowCopy query failed: " + ex.Message);
                    return;
                }

                try
                {
                    System.Collections.IEnumerator e;
                    try { e = collection.GetEnumerator(); }
                    catch (ManagementException ex)
                    {
                        ctx.Logger.Warning(ex, "Win32_ShadowCopy enumerator init failed");
                        writer.RecordPartial("Win32_ShadowCopy enumeration not permitted: " + ex.Message);
                        return;
                    }

                    while (true)
                    {
                        ctx.CancellationToken.ThrowIfCancellationRequested();
                        bool moved;
                        try { moved = e.MoveNext(); }
                        catch (ManagementException ex)
                        {
                            ctx.Logger.Warning(ex, "Win32_ShadowCopy enumeration failed (likely requires elevation)");
                            writer.RecordPartial("Win32_ShadowCopy enumeration failed (likely requires elevation): " + ex.Message);
                            return;
                        }
                        if (!moved) break;
                        var mo = (ManagementBaseObject)e.Current;
                        try
                        {
                            jl.Write(new VssRecord
                            {
                                Id = mo["ID"] as string,
                                InstallDate = mo["InstallDate"] as string,
                                OriginatingMachine = mo["OriginatingMachine"] as string,
                                ServiceMachine = mo["ServiceMachine"] as string,
                                VolumeName = mo["VolumeName"] as string,
                                DeviceObject = mo["DeviceObject"] as string,
                                ExposedName = mo["ExposedName"] as string,
                                ExposedPath = mo["ExposedPath"] as string,
                                State = mo["State"]?.ToString(),
                                Persistent = mo["Persistent"] as bool?,
                                ProviderId = mo["ProviderID"] as string
                            });
                            writer.RecordItem();
                        }
                        finally { mo.Dispose(); }
                    }
                }
                finally { collection.Dispose(); }
            }
        }

        private sealed class VssRecord
        {
            public string Id { get; set; }
            public string InstallDate { get; set; }
            public string OriginatingMachine { get; set; }
            public string ServiceMachine { get; set; }
            public string VolumeName { get; set; }
            public string DeviceObject { get; set; }
            public string ExposedName { get; set; }
            public string ExposedPath { get; set; }
            public string State { get; set; }
            public bool? Persistent { get; set; }
            public string ProviderId { get; set; }
        }
    }
}
