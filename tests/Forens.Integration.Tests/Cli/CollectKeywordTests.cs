using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Forens.Integration.Tests.Cli
{
    /// <summary>
    /// `forens collect --keyword X` runs a real collection AND a post-collection
    /// keyword aggregation in one shot, emitting search.jsonl + search.summary.json
    /// inside the run directory.
    /// </summary>
    public class CollectKeywordTests
    {
        [Fact]
        public void Collect_with_single_keyword_runs_search_and_emits_search_jsonl()
        {
            string outRoot = Path.Combine(Path.GetTempPath(), "forens-ck-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outRoot);
            try
            {
                var r = CliRunner.Run(
                    "collect",
                    "--sources", "process-list",
                    "--output", outRoot,
                    "--keyword", "svchost",
                    "--quiet");
                Assert.True(r.ExitCode == 0 || r.ExitCode == 3,
                    "Unexpected exit code " + r.ExitCode + ": stderr=" + r.StdErr);
                Assert.Contains("forens search", r.StdOut);

                string runDir = Directory.EnumerateDirectories(outRoot).Single();
                string searchPath = Path.Combine(runDir, "search.jsonl");
                string summaryPath = Path.Combine(runDir, "search.summary.json");
                Assert.True(File.Exists(searchPath), "search.jsonl missing");
                Assert.True(File.Exists(summaryPath), "search.summary.json missing");

                var summary = JObject.Parse(File.ReadAllText(summaryPath));
                Assert.NotNull(summary["keywords"]);
                Assert.Equal("svchost", (string)summary["keywords"][0]);
                Assert.False((bool)summary["caseSensitive"]);
                // Every Windows host has at least one svchost process running.
                Assert.True((int)summary["totalMatches"] >= 1, "expected >= 1 match for svchost");
            }
            finally { try { Directory.Delete(outRoot, true); } catch { } }
        }

        [Fact]
        public void Collect_with_multiple_keywords_combines_OR_logic()
        {
            string outRoot = Path.Combine(Path.GetTempPath(), "forens-ck-multi-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outRoot);
            try
            {
                // Two keywords that BOTH appear in process-list output on a typical host.
                var r = CliRunner.Run(
                    "collect",
                    "--sources", "process-list",
                    "--output", outRoot,
                    "--keyword", "svchost",
                    "--keyword", "explorer",
                    "--quiet");
                Assert.True(r.ExitCode == 0 || r.ExitCode == 3);

                string runDir = Directory.EnumerateDirectories(outRoot).Single();
                var summary = JObject.Parse(File.ReadAllText(Path.Combine(runDir, "search.summary.json")));
                Assert.Equal(2, ((JArray)summary["keywords"]).Count);

                var hits = File.ReadAllLines(Path.Combine(runDir, "search.jsonl"))
                    .Where(l => l.Length > 0).Select(JObject.Parse).ToList();
                var distinctMatches = hits.Select(h => (string)h["match"]).Distinct().ToList();
                // At least one keyword should fire on a typical host.
                Assert.NotEmpty(distinctMatches);
            }
            finally { try { Directory.Delete(outRoot, true); } catch { } }
        }

        [Fact]
        public void Collect_without_keyword_does_not_emit_search_files()
        {
            string outRoot = Path.Combine(Path.GetTempPath(), "forens-ck-none-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outRoot);
            try
            {
                var r = CliRunner.Run(
                    "collect",
                    "--sources", "process-list",
                    "--output", outRoot,
                    "--quiet");
                Assert.True(r.ExitCode == 0 || r.ExitCode == 3);
                Assert.DoesNotContain("forens search", r.StdOut);

                string runDir = Directory.EnumerateDirectories(outRoot).Single();
                Assert.False(File.Exists(Path.Combine(runDir, "search.jsonl")));
                Assert.False(File.Exists(Path.Combine(runDir, "search.summary.json")));
            }
            finally { try { Directory.Delete(outRoot, true); } catch { } }
        }
    }
}
