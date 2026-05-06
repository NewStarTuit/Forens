using System;
using System.Management;
using Forens.Core.Collection;

namespace Forens.Core.Collectors
{
    /// <summary>
    /// Win32_QuickFixEngineering WMI: KB updates installed on the host.
    /// </summary>
    public sealed class WindowsUpdatesSource : IArtifactSource
    {
        public const string SourceId = "windows-updates";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Windows Updates (KB inventory)",
            description: "Installed Windows quick-fix updates (KBnnnnnnn) with description, install date, and installer account.",
            category: Category.System,
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
            using (var jl = writer.OpenJsonlFile("windows-updates.jsonl"))
            {
                ManagementObjectCollection collection;
                try
                {
                    var searcher = new ManagementObjectSearcher(
                        "SELECT HotFixID, Description, FixComments, Caption, InstalledOn, InstalledBy, ServicePackInEffect FROM Win32_QuickFixEngineering");
                    collection = searcher.Get();
                }
                catch (Exception ex)
                {
                    ctx.Logger.Warning(ex, "WMI Win32_QuickFixEngineering query failed");
                    writer.RecordPartial("WMI Win32_QuickFixEngineering query failed: " + ex.Message);
                    return;
                }

                try
                {
                    foreach (ManagementBaseObject mo in collection)
                    {
                        ctx.CancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            jl.Write(new HotfixRecord
                            {
                                HotFixId = mo["HotFixID"] as string,
                                Description = mo["Description"] as string,
                                FixComments = mo["FixComments"] as string,
                                Caption = mo["Caption"] as string,
                                InstalledOn = mo["InstalledOn"] as string,
                                InstalledBy = mo["InstalledBy"] as string,
                                ServicePackInEffect = mo["ServicePackInEffect"] as string
                            });
                            writer.RecordItem();
                        }
                        finally { mo.Dispose(); }
                    }
                }
                finally { collection.Dispose(); }
            }
        }

        private sealed class HotfixRecord
        {
            public string HotFixId { get; set; }
            public string Description { get; set; }
            public string FixComments { get; set; }
            public string Caption { get; set; }
            public string InstalledOn { get; set; }
            public string InstalledBy { get; set; }
            public string ServicePackInEffect { get; set; }
        }
    }
}
