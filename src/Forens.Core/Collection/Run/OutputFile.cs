using System;

namespace Forens.Core.Collection.Run
{
    public sealed class OutputFile
    {
        public OutputFile(string relativePath, string sha256, long byteCount, DateTimeOffset writtenUtc)
        {
            if (string.IsNullOrEmpty(relativePath))
                throw new ArgumentException("relativePath is required.", nameof(relativePath));
            if (string.IsNullOrEmpty(sha256) || sha256.Length != 64)
                throw new ArgumentException("sha256 must be a 64-char hex string.", nameof(sha256));
            if (byteCount < 0)
                throw new ArgumentOutOfRangeException(nameof(byteCount));

            RelativePath = relativePath.Replace('\\', '/');
            Sha256 = sha256;
            ByteCount = byteCount;
            WrittenUtc = writtenUtc;
        }

        public string RelativePath { get; }
        public string Sha256 { get; }
        public long ByteCount { get; }
        public DateTimeOffset WrittenUtc { get; }
    }
}
