using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Forens.Core.Collection;
using Forens.Core.Profiles;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Forens.Cli.Commands
{
    [Command("list", Description = "List available artifact sources or profiles.")]
    public sealed class ListCommand
    {
        [Option("--json", "Output as JSON to stdout.", CommandOptionType.NoValue)]
        public bool Json { get; set; }

        [Option("--profiles", "Print profiles instead of sources.", CommandOptionType.NoValue)]
        public bool Profiles { get; set; }

        public int OnExecute(IConsole console)
        {
            if (Profiles)
                return PrintProfiles(console);

            var catalog = SourceCatalog.Discover();
            var rows = catalog.Sources
                .OrderBy(s => s.Metadata.Id, StringComparer.Ordinal)
                .Select(s => new SourceRow
                {
                    Id = s.Metadata.Id,
                    DisplayName = s.Metadata.DisplayName,
                    Description = s.Metadata.Description,
                    Category = s.Metadata.Category.ToString(),
                    RequiresElevation = s.Metadata.RequiresElevation,
                    SupportsTimeRange = s.Metadata.SupportsTimeRange,
                    SupportsProcessFilter = s.Metadata.SupportsProcessFilter
                })
                .ToList();

            if (Json)
            {
                var settings = new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver(),
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore
                };
                console.WriteLine(JsonConvert.SerializeObject(rows, settings));
                return 0;
            }

            // Human-readable
            console.WriteLine(string.Format("{0,-32} {1,-5} {2,-5} {3,-5} {4,-12} {5}",
                "ID", "ELEV", "TIME", "PROC", "CATEGORY", "DESCRIPTION"));
            foreach (var r in rows)
            {
                console.WriteLine(string.Format("{0,-32} {1,-5} {2,-5} {3,-5} {4,-12} {5}",
                    Trunc(r.Id, 32),
                    r.RequiresElevation ? "yes" : "no",
                    r.SupportsTimeRange ? "yes" : "no",
                    r.SupportsProcessFilter ? "yes" : "no",
                    Trunc(r.Category, 12),
                    r.Description ?? ""));
            }
            return 0;
        }

        private int PrintProfiles(IConsole console)
        {
            var catalog = SourceCatalog.Discover();
            if (Json)
            {
                var rows = CollectionProfiles.All.Select(p => new
                {
                    name = p.Name,
                    description = p.Description,
                    memoryCeilingMB = p.MemoryCeilingMB,
                    parallelism = p.Parallelism,
                    diskFloorBytes = p.DiskFloorBytes,
                    sources = p.ResolveSourceIds(catalog)
                });
                var settings = new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver(),
                    Formatting = Formatting.Indented
                };
                console.WriteLine(JsonConvert.SerializeObject(rows, settings));
                return 0;
            }

            console.WriteLine(string.Format("{0,-14} {1,5}  {2,5}  {3,8}  {4}",
                "PROFILE", "MEM", "PAR", "DISK", "SOURCES"));
            foreach (var p in CollectionProfiles.All)
            {
                var ids = p.ResolveSourceIds(catalog);
                console.WriteLine(string.Format("{0,-14} {1,4}M  {2,5}  {3,7}M  {4}",
                    p.Name, p.MemoryCeilingMB, p.Parallelism,
                    p.DiskFloorBytes / (1024L * 1024L), ids.Count));
                console.WriteLine("               " + p.Description);
                console.WriteLine("               -> " + (ids.Count == 0 ? "(none)" : string.Join(", ", ids)));
            }
            return 0;
        }

        private static string Trunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        private sealed class SourceRow
        {
            public string Id { get; set; }
            public string DisplayName { get; set; }
            public string Description { get; set; }
            public string Category { get; set; }
            public bool RequiresElevation { get; set; }
            public bool SupportsTimeRange { get; set; }
            public bool SupportsProcessFilter { get; set; }
        }
    }
}
