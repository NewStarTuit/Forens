using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Forens.Integration.Tests.Cli
{
    public class SearchCommandTests
    {
        [Fact]
        public void Search_against_fixture_run_dir_finds_keyword_hits_with_provenance()
        {
            string rundir = Path.Combine(Path.GetTempPath(), "forens-search-fixture-" + Guid.NewGuid().ToString("N"));
            string sourceDir = Path.Combine(rundir, "raw", "fake-source");
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "data.jsonl"),
                "{\"who\":\"chrome.exe\",\"pid\":1234}\n" +
                "{\"who\":\"notepad.exe\",\"pid\":5678}\n" +
                "{\"who\":\"CHROME-Helper\",\"pid\":9012}\n");

            try
            {
                var r = CliRunner.Run("search", "--run", rundir, "--keyword", "chrome");
                Assert.Equal(0, r.ExitCode);
                Assert.Contains("forens search:", r.StdOut);

                string outPath = Path.Combine(rundir, "search.jsonl");
                Assert.True(File.Exists(outPath));
                var hits = File.ReadAllLines(outPath).Where(l => l.Length > 0).Select(JObject.Parse).ToList();
                // Case-insensitive default → 2 hits ("chrome.exe" and "CHROME-Helper")
                Assert.Equal(2, hits.Count);
                Assert.All(hits, h =>
                {
                    Assert.Equal("fake-source", (string)h["sourceId"]);
                    Assert.Equal("raw/fake-source/data.jsonl", (string)h["relativePath"]);
                    Assert.Equal("chrome", (string)h["match"]);
                    Assert.NotNull(h["record"]);
                });

                string summaryPath = Path.Combine(rundir, "search.summary.json");
                Assert.True(File.Exists(summaryPath));
                var summary = JObject.Parse(File.ReadAllText(summaryPath));
                Assert.Equal(2, (int)summary["totalMatches"]);
                Assert.Equal(2, (int)summary["matchesPerSource"]["fake-source"]);
                Assert.False((bool)summary["caseSensitive"]);
            }
            finally { try { Directory.Delete(rundir, true); } catch { } }
        }

        [Fact]
        public void Search_with_case_sensitive_flag_excludes_uppercase_hits()
        {
            string rundir = Path.Combine(Path.GetTempPath(), "forens-search-cs-" + Guid.NewGuid().ToString("N"));
            string sourceDir = Path.Combine(rundir, "raw", "src1");
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "x.jsonl"),
                "{\"name\":\"chrome\"}\n" +
                "{\"name\":\"CHROME\"}\n");

            try
            {
                var r = CliRunner.Run("search", "--run", rundir, "--keyword", "chrome", "--case-sensitive");
                Assert.Equal(0, r.ExitCode);
                var hits = File.ReadAllLines(Path.Combine(rundir, "search.jsonl"))
                    .Where(l => l.Length > 0).Select(JObject.Parse).ToList();
                Assert.Single(hits);
            }
            finally { try { Directory.Delete(rundir, true); } catch { } }
        }

        [Fact]
        public void Search_with_multiple_keywords_uses_OR_logic()
        {
            string rundir = Path.Combine(Path.GetTempPath(), "forens-search-or-" + Guid.NewGuid().ToString("N"));
            string sourceDir = Path.Combine(rundir, "raw", "src1");
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "x.jsonl"),
                "{\"text\":\"alpha\"}\n" +
                "{\"text\":\"beta\"}\n" +
                "{\"text\":\"gamma\"}\n");

            try
            {
                var r = CliRunner.Run("search", "--run", rundir, "--keyword", "alpha", "--keyword", "gamma");
                Assert.Equal(0, r.ExitCode);
                var hits = File.ReadAllLines(Path.Combine(rundir, "search.jsonl"))
                    .Where(l => l.Length > 0).Select(JObject.Parse).ToList();
                Assert.Equal(2, hits.Count);
                Assert.Contains(hits, h => (string)h["match"] == "alpha");
                Assert.Contains(hits, h => (string)h["match"] == "gamma");
            }
            finally { try { Directory.Delete(rundir, true); } catch { } }
        }

        [Fact]
        public void Search_returns_exit_1_when_no_matches_found()
        {
            string rundir = Path.Combine(Path.GetTempPath(), "forens-search-nomatch-" + Guid.NewGuid().ToString("N"));
            string sourceDir = Path.Combine(rundir, "raw", "src1");
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "x.jsonl"), "{\"x\":1}\n");

            try
            {
                var r = CliRunner.Run("search", "--run", rundir, "--keyword", "definitely-not-present");
                Assert.Equal(1, r.ExitCode);
            }
            finally { try { Directory.Delete(rundir, true); } catch { } }
        }

        [Fact]
        public void Search_rejects_missing_run_directory()
        {
            var r = CliRunner.Run("search", "--run", @"C:\does\not\exist\____", "--keyword", "anything");
            Assert.Equal(2, r.ExitCode);
            Assert.Contains("not found", r.StdErr);
        }

        [Fact]
        public void Search_rejects_run_dir_without_raw_subdir()
        {
            string rundir = Path.Combine(Path.GetTempPath(), "forens-search-noraw-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rundir);
            try
            {
                var r = CliRunner.Run("search", "--run", rundir, "--keyword", "x");
                Assert.Equal(2, r.ExitCode);
                Assert.Contains("'raw' subdirectory", r.StdErr);
            }
            finally { try { Directory.Delete(rundir, true); } catch { } }
        }

        [Fact]
        public void Search_requires_keyword()
        {
            string rundir = Path.Combine(Path.GetTempPath(), "forens-search-nokw-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(rundir, "raw"));
            try
            {
                var r = CliRunner.Run("search", "--run", rundir);
                Assert.Equal(2, r.ExitCode);
                Assert.Contains("--keyword is required", r.StdErr);
            }
            finally { try { Directory.Delete(rundir, true); } catch { } }
        }
    }
}
