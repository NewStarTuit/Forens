using System;
using System.Collections.Generic;
using System.IO;
using Forens.Reporting;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using Xunit;

namespace Forens.Reporting.Tests
{
    public class JsonReportWriterTests
    {
        [Fact]
        public void Produced_report_validates_against_schema()
        {
            var model = SampleModel();
            string json = JsonReportWriter.Serialize(model);
            var doc = JObject.Parse(json);

            string schemaPath = Path.Combine(AppContext.BaseDirectory, "schemas", "report-schema.json");
            Assert.True(File.Exists(schemaPath), "schema not copied to output: " + schemaPath);
            var schema = JSchema.Parse(File.ReadAllText(schemaPath));
            IList<string> errors;
            bool valid = doc.IsValid(schema, out errors);
            Assert.True(valid, "Report does not validate. Errors:\n  " + string.Join("\n  ", errors));
        }

        [Fact]
        public void Schema_name_and_version_present()
        {
            var doc = JObject.Parse(JsonReportWriter.Serialize(SampleModel()));
            Assert.Equal("forens-report", (string)doc["schema"]?["name"]);
            Assert.Equal("1.0.0", (string)doc["schema"]?["version"]);
        }

        [Fact]
        public void One_section_per_source_in_input()
        {
            var doc = JObject.Parse(JsonReportWriter.Serialize(SampleModel()));
            var sections = doc["sections"] as JArray;
            Assert.NotNull(sections);
            Assert.Equal(2, sections.Count);
            Assert.Equal("alpha", (string)sections[0]["sourceId"]);
            Assert.Equal("beta", (string)sections[1]["sourceId"]);
        }

        private static ReportModel SampleModel()
        {
            return new ReportModel
            {
                Schema = ReportSchemaInfo.Default,
                Run = new RunSummary
                {
                    RunId = Guid.NewGuid().ToString("D"),
                    Profile = "default",
                    Host = new RunHostSummary { Name = "H", OsVersion = "Windows 10.0.19045" },
                    StartedUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
                    CompletedUtc = DateTimeOffset.UtcNow,
                    Status = "Completed"
                },
                Sections = new List<ReportSection>
                {
                    new ReportSection {
                        SourceId = "alpha", DisplayName = "Alpha", Category = "System", Status = "Succeeded",
                        Summary = new Dictionary<string, object> { { "itemsCollected", 5 } },
                        RawOutput = new List<RawOutputRef> {
                            new RawOutputRef { Path = "raw/alpha/data.jsonl",
                                               Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                                               ByteCount = 100 }
                        }
                    },
                    new ReportSection {
                        SourceId = "beta", DisplayName = "Beta", Category = "Process", Status = "Skipped",
                        StatusReason = "Requires elevation"
                    }
                }
            };
        }
    }
}
