using System;
using System.IO;
using Forens.Cli.Search;
using McMaster.Extensions.CommandLineUtils;

namespace Forens.Cli.Commands
{
    /// <summary>
    /// Standalone post-collection keyword aggregation across every source's
    /// JSONL output. Thin CLI wrapper over <see cref="KeywordSearchEngine"/>.
    /// For inline keyword search during a collect run, use
    /// <c>forens collect --keyword X</c> (see <see cref="CollectCommand"/>).
    /// </summary>
    [Command("search", Description = "Aggregate forensic records that mention a keyword (process name, path, SID, ...) across every source in a previously-collected run.")]
    public sealed class SearchCommand
    {
        [Option("--run", "Path to a previously-collected run directory (the one containing manifest.json + raw/).", CommandOptionType.SingleValue)]
        public string RunDir { get; set; }

        [Option("--keyword", "Keyword(s) to find. Repeat the flag for OR-logic across multiple keywords.", CommandOptionType.MultipleValue)]
        public string[] Keywords { get; set; }

        [Option("--case-sensitive", "Match exactly as typed. Default is case-insensitive.", CommandOptionType.NoValue)]
        public bool CaseSensitive { get; set; }

        [Option("--output", "Output file path. Defaults to <run>/search.jsonl.", CommandOptionType.SingleValue)]
        public string OutputPath { get; set; }

        [Option("--max-line-bytes", "Cap per-line scan size. Default 16384.", CommandOptionType.SingleValue)]
        public int MaxLineBytes { get; set; } = 16 * 1024;

        public int OnExecute(IConsole console)
        {
            if (string.IsNullOrEmpty(RunDir))
            {
                console.Error.WriteLine("--run is required");
                return 2;
            }
            if (!Directory.Exists(RunDir))
            {
                console.Error.WriteLine("Run directory not found: " + RunDir);
                return 2;
            }
            if (Keywords == null || Keywords.Length == 0)
            {
                console.Error.WriteLine("--keyword is required (repeat the flag for OR-logic across multiple keywords).");
                return 2;
            }
            if (!Directory.Exists(Path.Combine(RunDir, "raw")))
            {
                console.Error.WriteLine("No 'raw' subdirectory under " + RunDir + " — does not look like a forens run directory.");
                return 2;
            }

            KeywordSearchEngine.Result result;
            try
            {
                result = KeywordSearchEngine.Run(
                    runDir: RunDir,
                    keywords: Keywords,
                    caseSensitive: CaseSensitive,
                    outputPath: OutputPath,
                    maxLineBytes: MaxLineBytes);
            }
            catch (Exception ex)
            {
                console.Error.WriteLine("Search failed: " + ex.Message);
                return 5;
            }

            console.WriteLine(string.Format(
                "forens search: {0} match{1} across {2} source{3} in {4} file{5} ({6} line{7} scanned)",
                result.TotalMatches, result.TotalMatches == 1 ? "" : "es",
                result.MatchesPerSource.Count, result.MatchesPerSource.Count == 1 ? "" : "s",
                result.FilesScanned, result.FilesScanned == 1 ? "" : "s",
                result.TotalLinesScanned, result.TotalLinesScanned == 1 ? "" : "s"));
            console.WriteLine("  output: " + result.OutputPath);
            console.WriteLine("  summary: " + result.SummaryPath);

            return result.TotalMatches > 0 ? 0 : 1;
        }
    }
}
