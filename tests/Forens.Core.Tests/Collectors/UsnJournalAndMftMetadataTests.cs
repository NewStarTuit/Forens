using Forens.Core.Collection;
using Forens.Core.Collectors;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class UsnJournalSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new UsnJournalSource();
            Assert.Equal("usn-journal", src.Metadata.Id);
            Assert.Equal(Category.Filesystem, src.Metadata.Category);
            Assert.True(src.Metadata.RequiresElevation);
            Assert.False(src.Metadata.SupportsTimeRange);
            Assert.False(src.Metadata.SupportsProcessFilter);
            Assert.Contains(ContendedResource.RawDiskC, src.Metadata.ContendedResources);
        }

        [Fact]
        public void Precondition_skips_unprivileged_with_SeBackupPrivilege_reason()
        {
            var src = new UsnJournalSource();
            var ctx = TestContexts.Build(Forens.Common.Host.ElevationState.NotElevated);
            var pre = src.CheckPrecondition(ctx);
            Assert.Equal(PreconditionResult.SkipRequiresElevation, pre.Result);
            Assert.Contains("SeBackupPrivilege", pre.Reason, System.StringComparison.OrdinalIgnoreCase);
        }
    }

    public class MftMetadataSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new MftMetadataSource();
            Assert.Equal("mft-metadata", src.Metadata.Id);
            Assert.Equal(Category.Filesystem, src.Metadata.Category);
            Assert.True(src.Metadata.RequiresElevation);
            Assert.False(src.Metadata.SupportsTimeRange);
            Assert.False(src.Metadata.SupportsProcessFilter);
            Assert.Contains(ContendedResource.RawDiskC, src.Metadata.ContendedResources);
        }

        [Fact]
        public void Precondition_skips_unprivileged_with_SeBackupPrivilege_reason()
        {
            var src = new MftMetadataSource();
            var ctx = TestContexts.Build(Forens.Common.Host.ElevationState.NotElevated);
            var pre = src.CheckPrecondition(ctx);
            Assert.Equal(PreconditionResult.SkipRequiresElevation, pre.Result);
            Assert.Contains("SeBackupPrivilege", pre.Reason, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
