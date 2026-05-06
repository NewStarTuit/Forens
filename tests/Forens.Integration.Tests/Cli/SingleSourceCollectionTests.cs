using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Forens.Integration.Tests.Cli
{
    public class SingleSourceCollectionTests
    {
        [Fact]
        public void Single_source_run_produces_output_for_only_that_source()
        {
            string outRoot = Path.Combine(Path.GetTempPath(), "forens-it-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outRoot);
            try
            {
                var r = CliRunner.Run("collect", "--sources", "process-list", "--output", outRoot, "--quiet");
                Assert.True(r.ExitCode == 0, "stderr=" + r.StdErr + " stdout=" + r.StdOut);

                var runDirs = Directory.EnumerateDirectories(outRoot).ToArray();
                Assert.Single(runDirs);
                string runDir = runDirs[0];

                Assert.True(File.Exists(Path.Combine(runDir, "manifest.json")));
                Assert.True(File.Exists(Path.Combine(runDir, "report.json")));
                Assert.True(File.Exists(Path.Combine(runDir, "report.html")));
                Assert.True(File.Exists(Path.Combine(runDir, "run.log")));

                string rawDir = Path.Combine(runDir, "raw");
                Assert.True(Directory.Exists(rawDir));
                var sourceDirs = Directory.EnumerateDirectories(rawDir).ToArray();
                Assert.Single(sourceDirs);
                Assert.EndsWith("process-list", sourceDirs[0]);
                Assert.True(File.Exists(Path.Combine(sourceDirs[0], "processes.jsonl")));

                var manifest = JObject.Parse(File.ReadAllText(Path.Combine(runDir, "manifest.json")));
                Assert.Equal("Completed", (string)manifest["status"]);
                var sources = (JArray)manifest["request"]["sources"];
                Assert.Single(sources);
                Assert.Equal("process-list", (string)sources[0]);
                var results = (JArray)manifest["results"];
                Assert.Single(results);
                Assert.Equal("process-list", (string)results[0]["sourceId"]);
                Assert.Equal("Succeeded", (string)results[0]["status"]);
            }
            finally { try { Directory.Delete(outRoot, true); } catch { } }
        }

        [Fact]
        public void Manifest_sha256_matches_recomputed_file_hash()
        {
            string outRoot = Path.Combine(Path.GetTempPath(), "forens-it-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outRoot);
            try
            {
                var r = CliRunner.Run("collect", "--sources", "process-list", "--output", outRoot, "--quiet");
                Assert.Equal(0, r.ExitCode);

                string runDir = Directory.EnumerateDirectories(outRoot).Single();
                var manifest = JObject.Parse(File.ReadAllText(Path.Combine(runDir, "manifest.json")));
                var outputFile = manifest["results"][0]["outputFiles"][0];
                string declaredHash = (string)outputFile["sha256"];
                string declaredPath = (string)outputFile["relativePath"];
                long declaredBytes = (long)outputFile["byteCount"];

                string actualPath = Path.Combine(runDir, declaredPath.Replace('/', Path.DirectorySeparatorChar));
                byte[] bytes = File.ReadAllBytes(actualPath);
                Assert.Equal(declaredBytes, bytes.LongLength);
                using (var sha = SHA256.Create())
                {
                    string actualHex = ToHex(sha.ComputeHash(bytes));
                    Assert.Equal(declaredHash, actualHex);
                }
            }
            finally { try { Directory.Delete(outRoot, true); } catch { } }
        }

        [Fact]
        public void Dry_run_does_not_create_output_dir()
        {
            string outRoot = Path.Combine(Path.GetTempPath(), "forens-it-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outRoot);
            try
            {
                var r = CliRunner.Run("collect", "--sources", "process-list", "--output", outRoot, "--dry-run");
                Assert.Equal(0, r.ExitCode);
                Assert.Empty(Directory.EnumerateDirectories(outRoot));
                Assert.Contains("--dry-run", r.StdOut);
            }
            finally { try { Directory.Delete(outRoot, true); } catch { } }
        }

        [Fact]
        public void Pre_run_summary_lists_elevation_and_sources_to_run()
        {
            string outRoot = Path.Combine(Path.GetTempPath(), "forens-it-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outRoot);
            try
            {
                var r = CliRunner.Run("collect", "--sources", "process-list", "--output", outRoot, "--dry-run");
                Assert.Equal(0, r.ExitCode);
                Assert.Contains("pre-run summary", r.StdOut);
                Assert.Contains("Elevation:", r.StdOut);
                Assert.Contains("Sources to run", r.StdOut);
                Assert.Contains("process-list", r.StdOut);
            }
            finally { try { Directory.Delete(outRoot, true); } catch { } }
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
