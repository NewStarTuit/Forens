using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Forens.Cli.Search
{
    /// <summary>
    /// Core keyword-aggregation engine. Scans every <c>raw/&lt;source&gt;/*.jsonl</c>
    /// under a run directory line-by-line, finds lines containing any of the
    /// supplied keywords (OR-logic), and emits hits to the configured output
    /// path with provenance + a per-source summary.
    ///
    /// Used by:
    ///   - <see cref="Forens.Cli.Commands.SearchCommand"/> for standalone post-run search
    ///   - <see cref="Forens.Cli.Commands.CollectCommand"/> for inline post-collection search
    ///     (when --keyword is supplied alongside --profile or --sources)
    /// </summary>
    public static class KeywordSearchEngine
    {
        public sealed class Result
        {
            public int FilesScanned;
            public long TotalLinesScanned;
            public int TotalMatches;
            public IDictionary<string, int> MatchesPerSource;
            public string OutputPath;
            public string SummaryPath;
        }

        /// <summary>
        /// Run the keyword search.
        /// </summary>
        /// <param name="runDir">A previously-collected run directory (the one containing <c>manifest.json</c> + <c>raw/</c>).</param>
        /// <param name="keywords">Keywords to search for (OR-logic; null/empty entries are ignored).</param>
        /// <param name="caseSensitive">If false (default), substring match is case-insensitive.</param>
        /// <param name="outputPath">Hits output path. If null, defaults to <c>&lt;runDir&gt;/search.jsonl</c>.</param>
        /// <param name="maxLineBytes">Cap on the size of <c>rawLineTruncated</c> emitted in the hit record. Default 16 KiB.</param>
        public static Result Run(
            string runDir,
            string[] keywords,
            bool caseSensitive = false,
            string outputPath = null,
            int maxLineBytes = 16 * 1024)
        {
            if (string.IsNullOrEmpty(runDir)) throw new ArgumentException("runDir required", nameof(runDir));
            if (!Directory.Exists(runDir)) throw new DirectoryNotFoundException("Run directory not found: " + runDir);
            string rawDir = Path.Combine(runDir, "raw");
            if (!Directory.Exists(rawDir)) throw new DirectoryNotFoundException("No 'raw' subdirectory under " + runDir);
            if (keywords == null || keywords.Length == 0)
                throw new ArgumentException("At least one keyword required", nameof(keywords));

            string outPath = !string.IsNullOrEmpty(outputPath) ? outputPath : Path.Combine(runDir, "search.jsonl");
            string summaryPath = Path.ChangeExtension(outPath, ".summary.json");

            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            var hitSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.None
            };
            var summarySettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.Indented
            };

            var perSourceCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int totalMatches = 0;
            int filesScanned = 0;
            long totalLinesScanned = 0;
            var startedUtc = DateTime.UtcNow;

            using (var outFs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.Read, 65536))
            using (var sw = new StreamWriter(outFs, new UTF8Encoding(false)) { NewLine = "\n" })
            {
                foreach (var jsonl in Directory.EnumerateFiles(rawDir, "*.jsonl", SearchOption.AllDirectories))
                {
                    filesScanned++;
                    string sourceId = ExtractSourceId(jsonl, rawDir);
                    string relPath = jsonl.Substring(runDir.Length).TrimStart('\\', '/').Replace('\\', '/');

                    using (var reader = new StreamReader(jsonl, Encoding.UTF8, true, 64 * 1024))
                    {
                        string line;
                        long lineNumber = 0;
                        while ((line = reader.ReadLine()) != null)
                        {
                            lineNumber++;
                            totalLinesScanned++;
                            string matched = FirstMatch(line, keywords, comparison);
                            if (matched == null) continue;

                            JToken record = TryParse(line);
                            var hit = new SearchHit
                            {
                                SourceId = sourceId,
                                RelativePath = relPath,
                                LineNumber = lineNumber,
                                Match = matched,
                                Record = record,
                                RawLineTruncated = line.Length > maxLineBytes ? line.Substring(0, maxLineBytes) : null
                            };
                            sw.WriteLine(JsonConvert.SerializeObject(hit, hitSettings));
                            totalMatches++;
                            perSourceCounts.TryGetValue(sourceId, out int n);
                            perSourceCounts[sourceId] = n + 1;
                        }
                    }
                }
            }

            var sortedCounts = perSourceCounts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            var summary = new SearchSummary
            {
                RunDir = Path.GetFullPath(runDir),
                StartedUtc = startedUtc,
                CompletedUtc = DateTime.UtcNow,
                CaseSensitive = caseSensitive,
                Keywords = keywords,
                FilesScanned = filesScanned,
                TotalLinesScanned = totalLinesScanned,
                TotalMatches = totalMatches,
                MatchesPerSource = sortedCounts,
                OutputPath = Path.GetFullPath(outPath)
            };
            File.WriteAllText(summaryPath, JsonConvert.SerializeObject(summary, summarySettings), new UTF8Encoding(false));

            return new Result
            {
                FilesScanned = filesScanned,
                TotalLinesScanned = totalLinesScanned,
                TotalMatches = totalMatches,
                MatchesPerSource = sortedCounts,
                OutputPath = outPath,
                SummaryPath = summaryPath
            };
        }

        public static string FirstMatch(string line, string[] keywords, StringComparison comparison)
        {
            foreach (var kw in keywords)
            {
                if (string.IsNullOrEmpty(kw)) continue;
                if (line.IndexOf(kw, comparison) >= 0) return kw;
            }
            return null;
        }

        private static JToken TryParse(string line)
        {
            try { return JToken.Parse(line); } catch { return null; }
        }

        private static string ExtractSourceId(string fullPath, string rawDir)
        {
            string rel = fullPath.Substring(rawDir.Length).TrimStart('\\', '/');
            int sep = rel.IndexOfAny(new[] { '\\', '/' });
            return sep >= 0 ? rel.Substring(0, sep) : "(unknown)";
        }

        private sealed class SearchHit
        {
            public string SourceId { get; set; }
            public string RelativePath { get; set; }
            public long LineNumber { get; set; }
            public string Match { get; set; }
            public JToken Record { get; set; }
            public string RawLineTruncated { get; set; }
        }

        private sealed class SearchSummary
        {
            public string RunDir { get; set; }
            public DateTime StartedUtc { get; set; }
            public DateTime CompletedUtc { get; set; }
            public bool CaseSensitive { get; set; }
            public string[] Keywords { get; set; }
            public int FilesScanned { get; set; }
            public long TotalLinesScanned { get; set; }
            public int TotalMatches { get; set; }
            public IDictionary<string, int> MatchesPerSource { get; set; }
            public string OutputPath { get; set; }
        }
    }
}
