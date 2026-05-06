using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Forens.Integration.Tests.Cli
{
    public class ListCommandTests
    {
        [Fact]
        public void List_human_form_includes_required_columns_and_process_list_row()
        {
            var r = CliRunner.Run("list");
            Assert.Equal(0, r.ExitCode);
            Assert.Contains("ID", r.StdOut);
            Assert.Contains("ELEV", r.StdOut);
            Assert.Contains("TIME", r.StdOut);
            Assert.Contains("PROC", r.StdOut);
            Assert.Contains("CATEGORY", r.StdOut);
            Assert.Contains("DESCRIPTION", r.StdOut);
            Assert.Contains("process-list", r.StdOut);
        }

        [Fact]
        public void List_json_form_returns_valid_array_with_required_metadata_fields()
        {
            var r = CliRunner.Run("list", "--json");
            Assert.Equal(0, r.ExitCode);
            var arr = JArray.Parse(r.StdOut);
            Assert.NotEmpty(arr);
            var processList = arr.OfType<JObject>().FirstOrDefault(o => (string)o["id"] == "process-list");
            Assert.NotNull(processList);
            Assert.NotNull(processList["displayName"]);
            Assert.NotNull(processList["description"]);
            Assert.NotNull(processList["category"]);
            Assert.NotNull(processList["requiresElevation"]);
            Assert.NotNull(processList["supportsTimeRange"]);
            Assert.NotNull(processList["supportsProcessFilter"]);
        }

        [Fact]
        public void Version_command_prints_version_and_target_framework()
        {
            var r = CliRunner.Run("version");
            Assert.Equal(0, r.ExitCode);
            Assert.Contains("forens", r.StdOut);
            Assert.Contains("net462", r.StdOut);
        }
    }
}
