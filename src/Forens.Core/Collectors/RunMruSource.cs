using System;
using Forens.Core.Collection;
using Forens.Core.Collectors.Registry;
using Microsoft.Win32;

namespace Forens.Core.Collectors
{
    public sealed class RunMruSource : IArtifactSource
    {
        public const string SourceId = "runmru";
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Run Dialog History (RunMRU)",
            description: "Most-recently-used commands from the Win+R Run dialog (per-user, current operator).",
            category: Category.User,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: new[] { ContendedResource.RegistryHiveUser },
            estimatedMemoryMB: 4,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            // Two-pass: registry value-enumeration order is not guaranteed, so we collect
            // everything first, locate MRUList, then emit slot records with correct order.
            var values = new System.Collections.Generic.List<RegistryValueRecord>();
            foreach (var v in RegistryReader.EnumerateValues(
                RegistryHive.CurrentUser, RegistryView.Registry64, KeyPath))
            {
                values.Add(v);
            }

            string mruList = null;
            foreach (var v in values)
            {
                if (string.Equals(v.ValueName, "MRUList", StringComparison.OrdinalIgnoreCase))
                {
                    mruList = v.Value?.ToString();
                    break;
                }
            }

            using (var jl = writer.OpenJsonlFile("runmru.jsonl"))
            {
                foreach (var v in values)
                {
                    if (string.Equals(v.ValueName, "MRUList", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string command = v.Value?.ToString();
                    string trimmed = StripBackslash1(command);
                    int order = ComputeOrder(mruList, v.ValueName);
                    jl.Write(new RunMruRecord
                    {
                        Slot = v.ValueName,
                        OrderInMruList = order,
                        Command = trimmed
                    });
                    writer.RecordItem();
                }
            }
        }

        // RunMRU values end with "\\1" (terminator). Strip it for analyst readability.
        internal static string StripBackslash1(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.EndsWith("\\1", StringComparison.Ordinal))
                return s.Substring(0, s.Length - 2);
            return s;
        }

        // MRUList is a string like "abcdef" where the first char is the most-recent slot.
        // Returns 0-based order (0 = most recent), or -1 if the slot isn't listed.
        internal static int ComputeOrder(string mruList, string slot)
        {
            if (string.IsNullOrEmpty(mruList) || string.IsNullOrEmpty(slot) || slot.Length != 1)
                return -1;
            return mruList.IndexOf(slot[0]);
        }

        private sealed class RunMruRecord
        {
            public string Slot { get; set; }
            public int OrderInMruList { get; set; }
            public string Command { get; set; }
        }
    }
}
