using System;
using System.Threading;
using Forens.Common.Host;
using Serilog;

namespace Forens.Core.Collection
{
    public sealed class CollectionContext
    {
        public CollectionContext(
            Guid runId,
            string outputDir,
            DateTimeOffset? timeFrom,
            DateTimeOffset? timeTo,
            ProcessFilter processFilter,
            ElevationState elevation,
            Version hostOsVersion,
            CancellationToken cancellationToken,
            ILogger logger)
        {
            if (string.IsNullOrEmpty(outputDir))
                throw new ArgumentException("OutputDir is required.", nameof(outputDir));
            if (timeFrom.HasValue && timeTo.HasValue && timeFrom.Value > timeTo.Value)
                throw new ArgumentException("TimeFrom must be <= TimeTo.");

            RunId = runId;
            OutputDir = outputDir;
            TimeFrom = timeFrom;
            TimeTo = timeTo;
            ProcessFilter = processFilter ?? ProcessFilter.Empty;
            Elevation = elevation;
            HostOsVersion = hostOsVersion ?? new Version(0, 0);
            CancellationToken = cancellationToken;
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Guid RunId { get; }
        public string OutputDir { get; }
        public DateTimeOffset? TimeFrom { get; }
        public DateTimeOffset? TimeTo { get; }
        public ProcessFilter ProcessFilter { get; }
        public ElevationState Elevation { get; }
        public Version HostOsVersion { get; }
        public CancellationToken CancellationToken { get; }
        public ILogger Logger { get; }
    }
}
