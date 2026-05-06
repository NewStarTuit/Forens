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
    public class UsersGroupsSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new UsersGroupsSource();
            Assert.Equal("users-groups", src.Metadata.Id);
            Assert.Equal(Category.User, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.False(src.Metadata.SupportsTimeRange);
            Assert.False(src.Metadata.SupportsProcessFilter);
        }

        [Theory]
        [InlineData(@"\\.\root\cimv2:Win32_UserAccount.Domain=""DESKTOP"",Name=""blade""", "Domain", "DESKTOP")]
        [InlineData(@"\\.\root\cimv2:Win32_UserAccount.Domain=""DESKTOP"",Name=""blade""", "Name", "blade")]
        [InlineData(@"Win32_Group.Domain=""HOST"",Name=""Admins""", "Name", "Admins")]
        public void ExtractRefProp_pulls_named_property_from_WMI_object_path(string componentRef, string propName, string expected)
        {
            Assert.Equal(expected, UsersGroupsSource.ExtractRefProp(componentRef, propName));
        }

        [Fact]
        public void ExtractRefProp_returns_null_for_missing_property()
        {
            Assert.Null(UsersGroupsSource.ExtractRefProp(@"Win32_Group.Domain=""H""", "Name"));
        }

        [Fact]
        public void Live_collect_against_this_host_produces_users_and_groups()
        {
            string dir = Path.Combine(Path.GetTempPath(), "forens-ug-live-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var src = new UsersGroupsSource();
                using (var w = new StreamingOutputWriter(dir, "raw/users-groups"))
                {
                    src.Collect(Build(dir), w);
                    Assert.True(w.ItemsCollected > 0);
                }
                var users = File.ReadAllLines(Path.Combine(dir, "users.jsonl"));
                var groups = File.ReadAllLines(Path.Combine(dir, "groups.jsonl"));
                Assert.NotEmpty(users);
                Assert.NotEmpty(groups);
                Assert.NotNull(JObject.Parse(users[0])["sid"]);
                Assert.NotNull(JObject.Parse(groups[0])["members"]);
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
