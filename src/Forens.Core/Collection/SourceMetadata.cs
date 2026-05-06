using System;
using System.Collections.Generic;

namespace Forens.Core.Collection
{
    public sealed class SourceMetadata
    {
        public SourceMetadata(
            string id,
            string displayName,
            string description,
            Category category,
            bool requiresElevation,
            bool supportsTimeRange,
            bool supportsProcessFilter,
            ProcessFilterMode processFilterMode,
            IReadOnlyList<ContendedResource> contendedResources,
            int estimatedMemoryMB,
            Version minWindowsVersion)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("DisplayName is required.", nameof(displayName));
            if (description == null)
                throw new ArgumentNullException(nameof(description));
            if (estimatedMemoryMB < 0)
                throw new ArgumentOutOfRangeException(nameof(estimatedMemoryMB));

            Id = id;
            DisplayName = displayName;
            Description = description;
            Category = category;
            RequiresElevation = requiresElevation;
            SupportsTimeRange = supportsTimeRange;
            SupportsProcessFilter = supportsProcessFilter;
            ProcessFilterMode = processFilterMode;
            ContendedResources = contendedResources ?? Array.Empty<ContendedResource>();
            EstimatedMemoryMB = estimatedMemoryMB;
            MinWindowsVersion = minWindowsVersion;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public Category Category { get; }
        public bool RequiresElevation { get; }
        public bool SupportsTimeRange { get; }
        public bool SupportsProcessFilter { get; }
        public ProcessFilterMode ProcessFilterMode { get; }
        public IReadOnlyList<ContendedResource> ContendedResources { get; }
        public int EstimatedMemoryMB { get; }
        public Version MinWindowsVersion { get; }
    }
}
