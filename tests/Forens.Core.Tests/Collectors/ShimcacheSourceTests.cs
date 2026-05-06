using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Forens.Core.Collection;
using Forens.Core.Collectors;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class ShimcacheSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new ShimcacheSource();
            Assert.Equal("shimcache", src.Metadata.Id);
            Assert.Equal(Category.Persistence, src.Metadata.Category);
            Assert.True(src.Metadata.RequiresElevation);
            Assert.True(src.Metadata.SupportsProcessFilter);
            Assert.Contains(ContendedResource.RegistryHiveSystem, src.Metadata.ContendedResources);
        }

        [Fact]
        public void Precondition_skips_when_unprivileged()
        {
            var src = new ShimcacheSource();
            var ctx = TestContexts.Build(Forens.Common.Host.ElevationState.NotElevated);
            var pre = src.CheckPrecondition(ctx);
            Assert.Equal(PreconditionResult.SkipRequiresElevation, pre.Result);
            Assert.Contains("administrator", pre.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ParseEntries_extracts_path_and_last_modified_from_synthetic_blob()
        {
            // Build a synthetic Win 10 entry: "10ts" magic, entry length, path length,
            // path bytes, last-modified FILETIME, data length 0.
            string path = @"C:\Windows\System32\notepad.exe";
            byte[] pathBytes = Encoding.Unicode.GetBytes(path);
            long ft = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();

            byte[] blob;
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                // 0x30-byte header (we just pad 0 bytes — parser scans for the first "10ts").
                bw.Write(new byte[0x30]);
                // First entry
                bw.Write(0x73743031u);   // "10ts"
                // entryLen = 2 (path len) + pathBytes.Length + 8 (FILETIME) + 4 (dataLen)
                uint entryLen = (uint)(2 + pathBytes.Length + 8 + 4);
                bw.Write(entryLen);
                bw.Write((ushort)pathBytes.Length);
                bw.Write(pathBytes);
                bw.Write(ft);
                bw.Write(0u); // data length 0
                bw.Write(0u); // sentinel
                bw.Flush();
                blob = ms.ToArray();
            }

            var entries = ShimcacheSource.ParseEntries(blob, null, NullSourceWriter.Instance).ToList();
            Assert.Single(entries);
            Assert.Equal(path, entries[0].Path);
            Assert.NotNull(entries[0].LastModifiedUtc);
            Assert.Equal(2026, entries[0].LastModifiedUtc.Value.Year);
        }

        [Fact]
        public void ParseEntries_returns_empty_when_no_signature_present()
        {
            byte[] noisy = new byte[256];
            Assert.Empty(ShimcacheSource.ParseEntries(noisy, null, NullSourceWriter.Instance).ToList());
        }
    }

    // Minimal ISourceWriter that swallows every call — for ParseEntries unit tests.
    internal sealed class NullSourceWriter : ISourceWriter
    {
        public static readonly NullSourceWriter Instance = new NullSourceWriter();
        public void Dispose() { }
        public IRecordWriter OpenJsonlFile(string relativePath) => new NullRecordWriter();
        public Stream OpenBinaryFile(string relativePath) => new MemoryStream();
        public void RecordItem() { }
        public void RecordPartial(string reason) { }
        private sealed class NullRecordWriter : IRecordWriter
        {
            public void Dispose() { }
            public void Write<T>(T record) { }
        }
    }
}
