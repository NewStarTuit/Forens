using System;
using System.Collections.Generic;
using System.Linq;
using Forens.Core.Collection;
using Forens.Core.Collectors.Registry;
using Microsoft.Win32;

namespace Forens.Core.Collectors
{
    public sealed class AutorunsSource : IArtifactSource
    {
        public const string SourceId = "autoruns";

        private sealed class KeySpec
        {
            public KeySpec(RegistryHive hive, RegistryView view, string path)
            { Hive = hive; View = view; Path = path; }
            public RegistryHive Hive { get; }
            public RegistryView View { get; }
            public string Path { get; }
        }

        private static readonly KeySpec[] AutorunKeys =
        {
            new KeySpec(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            new KeySpec(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
            new KeySpec(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            new KeySpec(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
            new KeySpec(RegistryHive.CurrentUser,  RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Run"),
            new KeySpec(RegistryHive.CurrentUser,  RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\RunOnce"),
        };

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Autorun Registry Keys",
            description: "Run/RunOnce registry values from HKLM and current-user HKCU (incl. WoW64 32-bit views).",
            category: Category.Persistence,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: true,
            processFilterMode: ProcessFilterMode.HistoricalImagePath,
            contendedResources: new[] { ContendedResource.RegistryHiveSoftware, ContendedResource.RegistryHiveUser },
            estimatedMemoryMB: 8,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            using (var jl = writer.OpenJsonlFile("autoruns.jsonl"))
            {
                foreach (var k in AutorunKeys)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    IEnumerable<RegistryValueRecord> values;
                    try
                    {
                        values = RegistryReader.EnumerateValues(k.Hive, k.View, k.Path).ToArray();
                    }
                    catch (Exception ex)
                    {
                        ctx.Logger.Verbose(ex, "Failed to read autorun key {Key}", k.Path);
                        writer.RecordPartial("Failed to read at least one autorun key");
                        continue;
                    }

                    foreach (var v in values)
                    {
                        string command = v.Value as string ?? Convert.ToString(v.Value);
                        string imagePath = ParseImagePath(command);

                        if (!string.IsNullOrEmpty(imagePath) &&
                            !ctx.ProcessFilter.IncludesImagePath(imagePath))
                        {
                            continue;
                        }

                        var record = new AutorunRecord
                        {
                            Hive = v.Hive,
                            View = k.View == RegistryView.Registry32 ? "Wow6432" : "Native",
                            KeyPath = v.KeyPath,
                            ValueName = v.ValueName,
                            Command = command,
                            ImagePath = imagePath
                        };
                        jl.Write(record);
                        writer.RecordItem();
                    }
                }
            }
        }

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

        private sealed class AutorunRecord
        {
            public string Hive { get; set; }
            public string View { get; set; }
            public string KeyPath { get; set; }
            public string ValueName { get; set; }
            public string Command { get; set; }
            public string ImagePath { get; set; }
        }
    }
}
