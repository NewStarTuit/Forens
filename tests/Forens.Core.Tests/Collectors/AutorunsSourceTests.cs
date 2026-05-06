using System;
using System.IO;
using System.Linq;
using System.Threading;
using Forens.Common.Host;
using Forens.Core.Collection;
using Forens.Core.Collectors;
using Newtonsoft.Json.Linq;
using Serilog;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class AutorunsSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new AutorunsSource();
            Assert.Equal("autoruns", src.Metadata.Id);
            Assert.Equal(Category.Persistence, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.False(src.Metadata.SupportsTimeRange);
            Assert.True(src.Metadata.SupportsProcessFilter);
            Assert.Equal(ProcessFilterMode.HistoricalImagePath, src.Metadata.ProcessFilterMode);
        }

        [Fact]
        public void Output_records_have_required_fields_when_keys_present()
        {
            string dir = Path.Combine(Path.GetTempPath(), "forens-autoruns-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var src = new AutorunsSource();
                using (var w = new StreamingOutputWriter(dir, "raw/autoruns"))
                {
                    src.Collect(Build(dir), w);
                }
                string outFile = Path.Combine(dir, "autoruns.jsonl");
                Assert.True(File.Exists(outFile));
                var lines = File.ReadAllLines(outFile);
                if (lines.Length > 0)
                {
                    var first = JObject.Parse(lines[0]);
                    Assert.NotNull(first["hive"]);
                    Assert.NotNull(first["keyPath"]);
                    Assert.NotNull(first["valueName"]);
                    Assert.NotNull(first["command"]);
                    Assert.NotNull(first["imagePath"]);
                }
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Theory]
        [InlineData(@"C:\Windows\system32\notepad.exe", @"C:\Windows\system32\notepad.exe")]
        [InlineData("\"C:\\Program Files\\App\\app.exe\" --background", @"C:\Program Files\App\app.exe")]
        [InlineData(@"app.exe arg1 arg2", "app.exe")]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void ParseImagePath_handles_quoted_unquoted_and_empty(string input, string expected)
        {
            Assert.Equal(expected, AutorunsSource.ParseImagePath(input));
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
