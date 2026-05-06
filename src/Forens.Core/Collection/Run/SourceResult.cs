using System;
using System.Collections.Generic;

namespace Forens.Core.Collection.Run
{
    public enum SourceStatus
    {
        Succeeded,
        Partial,
        Skipped,
        Failed
    }

    public sealed class SourceResult
    {
        public SourceResult(
            string sourceId,
            SourceStatus status,
            string statusReason,
            DateTimeOffset startedUtc,
            DateTimeOffset completedUtc,
            long itemsCollected,
            long bytesWritten,
            IReadOnlyList<OutputFile> outputFiles,
            ErrorRecord error)
        {
            if (string.IsNullOrEmpty(sourceId))
                throw new ArgumentException("sourceId is required.", nameof(sourceId));

            SourceId = sourceId;
            Status = status;
            StatusReason = statusReason;
            StartedUtc = startedUtc;
            CompletedUtc = completedUtc;
            ItemsCollected = itemsCollected;
            BytesWritten = bytesWritten;
            OutputFiles = outputFiles ?? Array.Empty<OutputFile>();
            Error = error;
        }

        public string SourceId { get; }
        public SourceStatus Status { get; }
        public string StatusReason { get; }
        public DateTimeOffset StartedUtc { get; }
        public DateTimeOffset CompletedUtc { get; }
        public long ItemsCollected { get; }
        public long BytesWritten { get; }
        public IReadOnlyList<OutputFile> OutputFiles { get; }
        public ErrorRecord Error { get; }

        public long ElapsedMs
        {
            get { return (long)(CompletedUtc - StartedUtc).TotalMilliseconds; }
        }
    }
}
