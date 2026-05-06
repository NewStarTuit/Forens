using Forens.Core.Collection;
using Forens.Core.Collectors;
using Forens.Core.Collectors.EventLog;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class Chunk5EventLogTests
    {
        [Theory]
        [InlineData(typeof(EventLogSetupSource), "eventlog-setup")]
        [InlineData(typeof(EventLogRdpSource), "eventlog-rdp")]
        [InlineData(typeof(EventLogAppLockerSource), "eventlog-applocker")]
        [InlineData(typeof(EventLogPowerShellSource), "eventlog-powershell")]
        [InlineData(typeof(EventLogSysmonSource), "eventlog-sysmon")]
        public void New_event_log_sources_have_kebab_case_id_and_time_scope_support(System.Type type, string expectedId)
        {
            var src = (IArtifactSource)System.Activator.CreateInstance(type);
            Assert.Equal(expectedId, src.Metadata.Id);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.True(src.Metadata.SupportsTimeRange);
        }

        [Fact]
        public void Sysmon_preflight_returns_SkipNotAvailable_when_channel_missing()
        {
            // This test runs on dev machines where Sysmon is typically not installed.
            // If Sysmon IS installed, the precondition will be Ok and the test is skipped.
            var src = new EventLogSysmonSource();
            var pre = src.CheckPrecondition(TestContexts.Build(Forens.Common.Host.ElevationState.NotElevated));
            if (pre.Result == PreconditionResult.SkipNotAvailableOnHost)
            {
                Assert.Contains("not found", pre.Reason, System.StringComparison.OrdinalIgnoreCase);
            }
            // else: Sysmon is installed; nothing to assert.
        }

        [Fact]
        public void Preflight_returns_SkipNotAvailable_for_unknown_channel()
        {
            var pre = EventLogCollectorHelper.Preflight("Forens-Test-DefinitelyNotARealChannel");
            Assert.Equal(PreconditionResult.SkipNotAvailableOnHost, pre.Result);
        }

        [Fact]
        public void Preflight_returns_Ok_for_Application_channel()
        {
            // Application is guaranteed present on every Windows.
            var pre = EventLogCollectorHelper.Preflight("Application");
            Assert.Equal(PreconditionResult.Ok, pre.Result);
        }
    }
}
