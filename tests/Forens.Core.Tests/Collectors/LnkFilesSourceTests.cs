using System;
using System.IO;
using System.Threading;
using Forens.Common.Host;
using Forens.Core.Collection;
using Forens.Core.Collectors;
using Newtonsoft.Json.Linq;
using Serilog;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class LnkFilesSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new LnkFilesSource();
            Assert.Equal("lnk-files", src.Metadata.Id);
            Assert.Equal(Category.User, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.False(src.Metadata.SupportsTimeRange);
        }

        [Fact]
        public void Live_collect_writes_jsonl_with_required_fields()
        {
            string dir = Path.Combine(Path.GetTempPath(), "forens-lnk-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var src = new LnkFilesSource();
                using (var w = new StreamingOutputWriter(dir, "raw/lnk-files"))
                {
                    src.Collect(Build(dir), w);
                }
                string outFile = Path.Combine(dir, "lnk-files.jsonl");
                Assert.True(File.Exists(outFile));
                var lines = File.ReadAllLines(outFile);
                if (lines.Length > 0)
                {
                    var first = JObject.Parse(lines[0]);
                    Assert.NotNull(first["label"]);
                    Assert.NotNull(first["fileName"]);
                    Assert.NotNull(first["fullPath"]);
                    Assert.NotNull(first["sizeBytes"]);
                }
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        private static CollectionContext Build(string outputDir)
        {
            return new CollectionContext(
                runId: Guid.Empty,
                outputDir: outputDir,
                timeFrom: null, timeTo: null,
                processFilter: ProcessFilter.Empty,
                elevation: ElevationState.NotElevated,
                hostOsVersion: new Version(10, 0),
                cancellationToken: CancellationToken.None,
                logger: new LoggerConfiguration().CreateLogger());
        }
    }
}
