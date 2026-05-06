using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Forens.Common.Host;
using Forens.Core.Collection;
using Forens.Core.Collectors;
using Newtonsoft.Json.Linq;
using Serilog;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class ScheduledTasksSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new ScheduledTasksSource();
            Assert.Equal("scheduled-tasks", src.Metadata.Id);
            Assert.Equal(Category.Persistence, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.False(src.Metadata.SupportsTimeRange);
            Assert.True(src.Metadata.SupportsProcessFilter);
            Assert.Equal(ProcessFilterMode.HistoricalImagePath, src.Metadata.ProcessFilterMode);
        }

        [Fact]
        public void ParseTasksXml_extracts_task_records_from_schtasks_format()
        {
            const string xml = @"<Tasks>
<!-- \DemoTask -->
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Author>Demo Author</Author>
    <Description>A demo task</Description>
    <URI>\DemoTask</URI>
  </RegistrationInfo>
  <Triggers>
    <TimeTrigger><Enabled>true</Enabled></TimeTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author""><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal>
  </Principals>
  <Settings>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
  </Settings>
  <Actions>
    <Exec>
      <Command>C:\Demo\app.exe</Command>
      <Arguments>--run</Arguments>
      <WorkingDirectory>C:\Demo</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
</Tasks>";
            var records = ScheduledTasksSource.ParseTasksXml(xml, ctx: null).ToList();
            Assert.Single(records);
            var r = records[0];
            Assert.Equal(@"\DemoTask", r.Uri);
            Assert.Equal("Demo Author", r.Author);
            Assert.True(r.Enabled);
            Assert.Equal("S-1-5-18", r.PrincipalUserId);
            Assert.Equal("HighestAvailable", r.PrincipalRunLevel);
            Assert.Equal(@"C:\Demo\app.exe", r.ActionCommand);
            Assert.Equal("--run", r.ActionArguments);
            Assert.Single(r.Triggers);
            Assert.Equal("TimeTrigger", r.Triggers[0]);
        }

        [Fact]
        public void ParseTasksXml_returns_empty_for_garbage_input()
        {
            Assert.Empty(ScheduledTasksSource.ParseTasksXml("not xml at all", ctx: null).ToList());
            Assert.Empty(ScheduledTasksSource.ParseTasksXml("", ctx: null).ToList());
        }

        [Fact]
        public void DecodeOutput_strips_utf16le_BOM()
        {
            // "<Task" preceded by FF FE BOM, in UTF-16 LE
            byte[] payload = new byte[] { 0xFF, 0xFE, 0x3C, 0x00, 0x54, 0x00, 0x61, 0x00, 0x73, 0x00, 0x6B, 0x00 };
            string s = ScheduledTasksSource.DecodeOutput(payload);
            Assert.Equal("<Task", s);
        }

        [Fact]
        public void DecodeOutput_handles_utf16le_without_BOM()
        {
            byte[] payload = new byte[] { 0x3C, 0x00, 0x54, 0x00, 0x61, 0x00, 0x73, 0x00, 0x6B, 0x00 };
            string s = ScheduledTasksSource.DecodeOutput(payload);
            Assert.Equal("<Task", s);
        }

        [Fact]
        public void Live_collect_against_this_host_produces_records()
        {
            string dir = Path.Combine(Path.GetTempPath(), "forens-st-live-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var src = new ScheduledTasksSource();
                Assert.Equal(PreconditionResult.Ok, src.CheckPrecondition(Build(dir)).Result);
                using (var w = new StreamingOutputWriter(dir, "raw/scheduled-tasks"))
                {
                    src.Collect(Build(dir), w);
                    Assert.True(w.ItemsCollected > 0, "expected at least one task on a typical Windows host");
                }
                var first = JObject.Parse(File.ReadAllLines(Path.Combine(dir, "scheduled-tasks.jsonl")).First());
                Assert.NotNull(first["uri"]);
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
