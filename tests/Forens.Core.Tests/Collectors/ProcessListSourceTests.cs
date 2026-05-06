using System;
using System.IO;
using System.Linq;
using System.Threading;
using Forens.Core.Collection;
using Forens.Core.Collectors;
using Newtonsoft.Json.Linq;
using Serilog;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class ProcessListSourceTests
    {
        [Fact]
        public void Metadata_id_is_stable_kebab_case()
        {
            var src = new ProcessListSource();
            Assert.Equal("process-list", src.Metadata.Id);
            Assert.Matches("^[a-z][a-z0-9-]*$", src.Metadata.Id);
        }

        [Fact]
        public void Metadata_declares_unprivileged_process_filter_capable()
        {
            var src = new ProcessListSource();
            Assert.False(src.Metadata.RequiresElevation);
            Assert.False(src.Metadata.SupportsTimeRange);
            Assert.True(src.Metadata.SupportsProcessFilter);
            Assert.Equal(ProcessFilterMode.LivePid, src.Metadata.ProcessFilterMode);
            Assert.Contains(ContendedResource.WmiCimV2, src.Metadata.ContendedResources);
        }

        [Fact]
        public void Precondition_is_Ok_under_unprivileged_context()
        {
            var src = new ProcessListSource();
            var ctx = BuildContext();
            var pre = src.CheckPrecondition(ctx);
            Assert.Equal(PreconditionResult.Ok, pre.Result);
        }

        [Fact]
        public void Collect_writes_jsonl_with_expected_top_level_fields()
        {
            string dir = Path.Combine(Path.GetTempPath(), "forens-pls-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var src = new ProcessListSource();
                var ctx = BuildContext(dir);
                using (var writer = new StreamingOutputWriter(dir, "raw/process-list"))
                {
                    src.Collect(ctx, writer);
                    Assert.True(writer.ItemsCollected > 0, "expected to enumerate at least one process");
                }

                string jsonlPath = Path.Combine(dir, "processes.jsonl");
                Assert.True(File.Exists(jsonlPath));
                var firstLine = File.ReadAllLines(jsonlPath).First();
                var record = JObject.Parse(firstLine);
                Assert.NotNull(record["pid"]);
                // ProcessName, ImagePath, CommandLine, ParentPid etc. are best-effort fields;
                // assert at least pid is always present (every process has one).
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        private static CollectionContext BuildContext(string outputDir = null)
        {
            var logger = new LoggerConfiguration().CreateLogger();
            return new CollectionContext(
                runId: Guid.Empty,
                outputDir: outputDir ?? Path.GetTempPath(),
                timeFrom: null,
                timeTo: null,
                processFilter: ProcessFilter.Empty,
                elevation: Forens.Common.Host.ElevationState.NotElevated,
                hostOsVersion: new Version(10, 0),
                cancellationToken: CancellationToken.None,
                logger: logger);
        }
    }
}
