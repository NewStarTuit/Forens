using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Forens.Core.Collection;
using Xunit;

namespace Forens.Core.Tests.Collection
{
    public class StreamingOutputWriterTests
    {
        private static string TempDir()
        {
            string p = Path.Combine(Path.GetTempPath(), "forens-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(p);
            return p;
        }

        [Fact]
        public void Jsonl_produces_one_line_per_record_with_correct_byte_count_and_sha256()
        {
            string dir = TempDir();
            try
            {
                using (var w = new StreamingOutputWriter(dir, "raw/test"))
                {
                    using (var jl = w.OpenJsonlFile("data.jsonl"))
                    {
                        jl.Write(new { a = 1, b = "hello" });
                        jl.Write(new { a = 2, b = "world" });
                        jl.Write(new { a = 3, b = "!" });
                    }
                    var files = w.FinishedFiles;
                    Assert.Single(files);
                    var of = files[0];
                    Assert.Equal("raw/test/data.jsonl", of.RelativePath);

                    string filePath = Path.Combine(dir, "data.jsonl");
                    byte[] actualBytes = File.ReadAllBytes(filePath);
                    Assert.Equal(actualBytes.LongLength, of.ByteCount);

                    using (var sha = SHA256.Create())
                    {
                        string expectedHex = ToHex(sha.ComputeHash(actualBytes));
                        Assert.Equal(expectedHex, of.Sha256);
                    }

                    string text = Encoding.UTF8.GetString(actualBytes);
                    var lines = text.TrimEnd('\n').Split('\n');
                    Assert.Equal(3, lines.Length);
                    Assert.Contains("\"a\":1", lines[0]);
                    Assert.Contains("\"a\":2", lines[1]);
                }
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void Binary_file_byte_count_and_sha256_match_recomputed_values()
        {
            string dir = TempDir();
            try
            {
                byte[] payload = new byte[100_000];
                new Random(42).NextBytes(payload);

                using (var w = new StreamingOutputWriter(dir, "raw/bin"))
                {
                    using (var s = w.OpenBinaryFile("blob.bin"))
                    {
                        s.Write(payload, 0, payload.Length);
                    }
                    var files = w.FinishedFiles;
                    Assert.Single(files);
                    var of = files[0];
                    Assert.Equal(payload.LongLength, of.ByteCount);
                    using (var sha = SHA256.Create())
                    {
                        Assert.Equal(ToHex(sha.ComputeHash(payload)), of.Sha256);
                    }
                }
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void RecordPartial_observable_via_IsPartial_and_PartialReason()
        {
            string dir = TempDir();
            try
            {
                using (var w = new StreamingOutputWriter(dir, "raw/p"))
                {
                    Assert.False(w.IsPartial);
                    w.RecordPartial("event log paged out");
                    Assert.True(w.IsPartial);
                    Assert.Equal("event log paged out", w.PartialReason);
                }
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void RecordItem_increments_counter_thread_safely()
        {
            string dir = TempDir();
            try
            {
                using (var w = new StreamingOutputWriter(dir, "raw/c"))
                {
                    System.Threading.Tasks.Parallel.For(0, 10_000, _ => w.RecordItem());
                    Assert.Equal(10_000, w.ItemsCollected);
                }
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void Relative_path_traversal_is_rejected()
        {
            string dir = TempDir();
            try
            {
                using (var w = new StreamingOutputWriter(dir, "raw/t"))
                {
                    Assert.Throws<ArgumentException>(() => w.OpenJsonlFile("..\\escape.jsonl"));
                }
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
