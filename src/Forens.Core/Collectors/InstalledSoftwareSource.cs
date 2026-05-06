using System;
using System.Collections.Generic;
using System.Globalization;
using Forens.Core.Collection;
using Forens.Core.Collectors.Registry;
using Microsoft.Win32;

namespace Forens.Core.Collectors
{
    public sealed class InstalledSoftwareSource : IArtifactSource
    {
        public const string SourceId = "installed-software";

        private sealed class KeySpec
        {
            public KeySpec(RegistryHive hive, RegistryView view, string path)
            { Hive = hive; View = view; Path = path; }
            public RegistryHive Hive { get; }
            public RegistryView View { get; }
            public string Path { get; }
        }

        private static readonly KeySpec[] UninstallKeys =
        {
            new KeySpec(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            new KeySpec(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            new KeySpec(RegistryHive.CurrentUser,  RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        };

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Installed Software",
            description: "Programs registered under the Uninstall registry keys (per-machine + current-user).",
            category: Category.System,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: new[] { ContendedResource.RegistryHiveSoftware, ContendedResource.RegistryHiveUser },
            estimatedMemoryMB: 16,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            using (var jl = writer.OpenJsonlFile("installed-software.jsonl"))
            {
                foreach (var k in UninstallKeys)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    IEnumerable<RegistrySubkeyRecord> subkeys;
                    try
                    {
                        subkeys = RegistryReader.EnumerateSubkeys(k.Hive, k.View, k.Path);
                    }
                    catch (Exception ex)
                    {
                        ctx.Logger.Verbose(ex, "Failed to read uninstall key {Key}", k.Path);
                        writer.RecordPartial("Failed to read at least one uninstall key");
                        continue;
                    }

                    foreach (var sk in subkeys)
                    {
                        string displayName = sk.GetString("DisplayName");
                        if (string.IsNullOrEmpty(displayName)) continue; // skip KB updates and headless registrations

                        var record = new SoftwareRecord
                        {
                            Hive = sk.Hive,
                            View = k.View == RegistryView.Registry32 ? "Wow6432" : "Native",
                            ProductCode = sk.SubkeyName,
                            DisplayName = displayName,
                            DisplayVersion = sk.GetString("DisplayVersion"),
                            Publisher = sk.GetString("Publisher"),
                            InstallDate = sk.GetString("InstallDate"),
                            InstallLocation = sk.GetString("InstallLocation"),
                            UninstallString = sk.GetString("UninstallString"),
                            EstimatedSize = TryGetInt(sk.Values, "EstimatedSize")
                        };
                        jl.Write(record);
                        writer.RecordItem();
                    }
                }
            }
        }

        private static int? TryGetInt(IReadOnlyDictionary<string, object> values, string name)
        {
            object v;
            if (!values.TryGetValue(name, out v) || v == null) return null;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        private sealed class SoftwareRecord
        {
            public string Hive { get; set; }
            public string View { get; set; }
            public string ProductCode { get; set; }
            public string DisplayName { get; set; }
            public string DisplayVersion { get; set; }
            public string Publisher { get; set; }
            public string InstallDate { get; set; }
            public string InstallLocation { get; set; }
            public string UninstallString { get; set; }
            public int? EstimatedSize { get; set; }
        }
    }
}
