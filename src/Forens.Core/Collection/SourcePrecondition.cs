using System;

namespace Forens.Core.Collection
{
    public enum PreconditionResult
    {
        Ok,
        SkipRequiresElevation,
        SkipNotAvailableOnHost,
        SkipDisabled
    }

    public sealed class SourcePrecondition
    {
        private SourcePrecondition(PreconditionResult result, string reason)
        {
            Result = result;
            Reason = reason;
        }

        public PreconditionResult Result { get; }
        public string Reason { get; }

        public static SourcePrecondition Ok()
        {
            return new SourcePrecondition(PreconditionResult.Ok, null);
        }

        public static SourcePrecondition Skip(PreconditionResult result, string reason)
        {
            if (result == PreconditionResult.Ok)
                throw new ArgumentException("Skip requires a non-Ok PreconditionResult.", nameof(result));
            if (string.IsNullOrEmpty(reason))
                throw new ArgumentException("Skip requires a reason.", nameof(reason));
            return new SourcePrecondition(result, reason);
        }
    }
}
