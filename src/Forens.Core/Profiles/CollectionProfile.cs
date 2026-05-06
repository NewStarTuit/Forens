using System;
using System.Collections.Generic;
using System.Linq;
using Forens.Core.Collection;

namespace Forens.Core.Profiles
{
    public sealed class CollectionProfile
    {
        public CollectionProfile(
            string name,
            string description,
            int memoryCeilingMB,
            int parallelism,
            long diskFloorBytes,
            Func<IArtifactSource, bool> sourceFilter)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name required", nameof(name));
            if (memoryCeilingMB < 64) throw new ArgumentOutOfRangeException(nameof(memoryCeilingMB));
            if (parallelism < 1) throw new ArgumentOutOfRangeException(nameof(parallelism));
            if (diskFloorBytes < 0) throw new ArgumentOutOfRangeException(nameof(diskFloorBytes));
            if (sourceFilter == null) throw new ArgumentNullException(nameof(sourceFilter));

            Name = name;
            Description = description ?? "";
            MemoryCeilingMB = memoryCeilingMB;
            Parallelism = parallelism;
            DiskFloorBytes = diskFloorBytes;
            SourceFilter = sourceFilter;
        }

        public string Name { get; }
        public string Description { get; }
        public int MemoryCeilingMB { get; }
        public int Parallelism { get; }
        public long DiskFloorBytes { get; }
        public Func<IArtifactSource, bool> SourceFilter { get; }

        public IReadOnlyList<string> ResolveSourceIds(SourceCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            return catalog.Sources
                .Where(SourceFilter)
                .Select(s => s.Metadata.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public static class CollectionProfiles
    {
        public static readonly CollectionProfile LiveTriage = new CollectionProfile(
            name: "live-triage",
            description: "Unprivileged + light: every source where RequiresElevation==false AND EstimatedMemoryMB<=64.",
            memoryCeilingMB: 256,
            parallelism: Math.Min(2, Environment.ProcessorCount),
            diskFloorBytes: 1L * 1024 * 1024 * 1024,
            sourceFilter: s => !s.Metadata.RequiresElevation && s.Metadata.EstimatedMemoryMB <= 64);

        public static readonly CollectionProfile Default = new CollectionProfile(
            name: "default",
            description: "Every catalog source except those that read raw disk (MFT/USN).",
            memoryCeilingMB: 512,
            parallelism: Math.Min(Environment.ProcessorCount, 8),
            diskFloorBytes: 1L * 1024 * 1024 * 1024,
            sourceFilter: s => !s.Metadata.ContendedResources.Contains(ContendedResource.RawDiskC));

        public static readonly CollectionProfile Full = new CollectionProfile(
            name: "full",
            description: "Every catalog source.",
            memoryCeilingMB: 1024,
            parallelism: Math.Min(Environment.ProcessorCount, 8),
            diskFloorBytes: 5L * 1024 * 1024 * 1024,
            sourceFilter: _ => true);

        private static readonly Dictionary<string, CollectionProfile> _byName =
            new Dictionary<string, CollectionProfile>(StringComparer.OrdinalIgnoreCase)
            {
                { LiveTriage.Name, LiveTriage },
                { Default.Name, Default },
                { Full.Name, Full },
            };

        public static IReadOnlyDictionary<string, CollectionProfile> ByName => _byName;

        public static IReadOnlyList<CollectionProfile> All => new[] { LiveTriage, Default, Full };

        public static bool TryGet(string name, out CollectionProfile profile)
        {
            return _byName.TryGetValue(name ?? "", out profile);
        }
    }
}
