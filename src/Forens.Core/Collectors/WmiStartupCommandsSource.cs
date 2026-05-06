using System;
using System.Management;
using Forens.Core.Collection;

namespace Forens.Core.Collectors
{
    /// <summary>
    /// Win32_StartupCommand enumeration — broader autostart coverage than the registry
    /// Run/RunOnce keys alone (also includes startup-folder shortcuts and per-user
    /// HKCU\...\Run, accessible across users when running elevated).
    /// </summary>
    public sealed class WmiStartupCommandsSource : IArtifactSource
    {
        public const string SourceId = "wmi-startup-commands";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "WMI Startup Commands",
            description: "Win32_StartupCommand WMI: registry Run keys + startup folders, with per-user attribution.",
            category: Category.Persistence,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: true,
            processFilterMode: ProcessFilterMode.HistoricalImagePath,
            contendedResources: new[] { ContendedResource.WmiCimV2 },
            estimatedMemoryMB: 8,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            using (var jl = writer.OpenJsonlFile("wmi-startup-commands.jsonl"))
            {
                ManagementObjectCollection collection;
                try
                {
                    var searcher = new ManagementObjectSearcher(
                        "SELECT Name, Caption, Description, Command, Location, User, UserSID FROM Win32_StartupCommand");
                    collection = searcher.Get();
                }
                catch (Exception ex)
                {
                    ctx.Logger.Warning(ex, "WMI Win32_StartupCommand query failed");
                    writer.RecordPartial("WMI Win32_StartupCommand query failed: " + ex.Message);
                    return;
                }

                try
                {
                    foreach (ManagementBaseObject mo in collection)
                    {
                        ctx.CancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            string command = mo["Command"] as string;
                            string image = ParseImagePath(command);
                            if (!string.IsNullOrEmpty(image) &&
                                !ctx.ProcessFilter.IncludesImagePath(image))
                            {
                                continue;
                            }
                            jl.Write(new StartupCommandRecord
                            {
                                Name = mo["Name"] as string,
                                Caption = mo["Caption"] as string,
                                Description = mo["Description"] as string,
                                Command = command,
                                ImagePath = image,
                                Location = mo["Location"] as string,
                                User = mo["User"] as string,
                                UserSid = mo["UserSID"] as string
                            });
                            writer.RecordItem();
                        }
                        finally { mo.Dispose(); }
                    }
                }
                finally { collection.Dispose(); }
            }
        }

        // Same image-path parsing as AutorunsSource.
        internal static string ParseImagePath(string command)
        {
            if (string.IsNullOrEmpty(command)) return null;
            string s = command.Trim();
            if (s.Length == 0) return null;

            if (s[0] == '"')
            {
                int end = s.IndexOf('"', 1);
                if (end > 1) return s.Substring(1, end - 1);
                return s.Substring(1);
            }

            int sp = s.IndexOf(' ');
            return sp > 0 ? s.Substring(0, sp) : s;
        }

        private sealed class StartupCommandRecord
        {
            public string Name { get; set; }
            public string Caption { get; set; }
            public string Description { get; set; }
            public string Command { get; set; }
            public string ImagePath { get; set; }
            public string Location { get; set; }
            public string User { get; set; }
            public string UserSid { get; set; }
        }
    }
}
