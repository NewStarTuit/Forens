using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Forens.Reporting;
using Xunit;

namespace Forens.Reporting.Tests
{
    public class HtmlReportWriterTests
    {
        [Fact]
        public void Output_has_no_external_network_resources()
        {
            var html = HtmlReportWriter.Render(SampleModel());
            Assert.DoesNotContain("http://", html);
            Assert.DoesNotContain("https://", html);
            Assert.DoesNotContain("//cdn.", html);
            Assert.DoesNotMatch(new Regex(@"src\s*=\s*['""]\s*//"), html);
            Assert.DoesNotMatch(new Regex(@"href\s*=\s*['""]\s*//"), html);
        }

        [Fact]
        public void User_supplied_strings_are_html_encoded()
        {
            var model = SampleModel();
            model.Sections.Add(new ReportSection
            {
                SourceId = "xss-test",
                DisplayName = "<script>alert('x')</script>",
                Category = "System",
                Status = "Succeeded",
                Summary = new Dictionary<string, object>(),
                RawOutput = new List<RawOutputRef>()
            });
            var html = HtmlReportWriter.Render(model);
            Assert.DoesNotContain("<script>alert('x')</script>", html);
            Assert.Contains("&lt;script&gt;", html);
        }

        [Fact]
        public void Renders_one_category_section_per_distinct_category()
        {
            var html = HtmlReportWriter.Render(SampleModel());
            int processCount = CountOccurrences(html, "<section class=\"category\">");
            Assert.Equal(2, processCount); // System + Process in the sample
        }

        [Fact]
        public void Embeds_inline_style_and_script_tags()
        {
            var html = HtmlReportWriter.Render(SampleModel());
            Assert.Contains("<style>", html);
            Assert.Contains("</style>", html);
            Assert.Contains("<script>", html);
            Assert.Contains("</script>", html);
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) != -1)
            {
                count++;
                i += needle.Length;
            }
            return count;
        }

        private static ReportModel SampleModel()
        {
            return new ReportModel
            {
                Schema = ReportSchemaInfo.Default,
                Run = new RunSummary
                {
                    RunId = Guid.NewGuid().ToString("D"),
                    Host = new RunHostSummary { Name = "H", OsVersion = "Windows 10.0.19045" },
                    StartedUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
                    CompletedUtc = DateTimeOffset.UtcNow,
                    Status = "Completed"
                },
                Sections = new List<ReportSection>
                {
                    new ReportSection {
                        SourceId = "alpha", DisplayName = "Alpha", Category = "System", Status = "Succeeded",
                        Summary = new Dictionary<string, object>(), RawOutput = new List<RawOutputRef>()
                    },
                    new ReportSection {
                        SourceId = "beta", DisplayName = "Beta", Category = "Process", Status = "Skipped",
                        Summary = new Dictionary<string, object>(), RawOutput = new List<RawOutputRef>()
                    }
                }
            };
        }
    }
}
