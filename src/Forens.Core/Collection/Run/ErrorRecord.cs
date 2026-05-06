using System;

namespace Forens.Core.Collection.Run
{
    public sealed class ErrorRecord
    {
        public ErrorRecord(string type, string message, string stackTrace)
        {
            Type = type ?? "Unknown";
            Message = message ?? string.Empty;
            StackTrace = stackTrace;
        }

        public string Type { get; }
        public string Message { get; }
        public string StackTrace { get; }

        public static ErrorRecord FromException(Exception ex)
        {
            if (ex == null) return null;
            return new ErrorRecord(ex.GetType().FullName, ex.Message, ex.StackTrace);
        }
    }
}
