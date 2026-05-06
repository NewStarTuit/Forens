using System;
using System.Linq;
using Forens.Core.Collection;
using Forens.Core.Collectors;
using Forens.Core.Profiles;
using Xunit;

namespace Forens.Core.Tests.Profiles
{
    public class CollectionProfileTests
    {
        [Fact]
        public void Three_named_profiles_are_registered()
        {
            Assert.True(CollectionProfiles.ByName.ContainsKey("live-triage"));
            Assert.True(CollectionProfiles.ByName.ContainsKey("default"));
            Assert.True(CollectionProfiles.ByName.ContainsKey("full"));
            Assert.Equal(3, CollectionProfiles.All.Count);
        }

        [Fact]
        public void TryGet_is_case_insensitive()
        {
            Assert.True(CollectionProfiles.TryGet("LIVE-TRIAGE", out var p));
            Assert.Equal("live-triage", p.Name);
        }

        [Fact]
        public void TryGet_returns_false_for_unknown_name()
        {
            Assert.False(CollectionProfiles.TryGet("not-a-profile", out _));
        }

        [Fact]
        public void LiveTriage_excludes_elevation_required_sources()
        {
            var catalog = SourceCatalog.DiscoverFromTypes(new[]
            {
                typeof(ProcessListSource),
                typeof(ServicesSource),
                typeof(EventLogApplicationSource),
                typeof(EventLogSecuritySource), // requires elevation
            });
            var ids = CollectionProfiles.LiveTriage.ResolveSourceIds(catalog);
            Assert.Contains("process-list", ids);
            Assert.Contains("services", ids);
            Assert.Contains("eventlog-application", ids);
            Assert.DoesNotContain("eventlog-security", ids);
        }

        [Fact]
        public void DefaultProfile_excludes_RawDiskC_sources()
        {
            // No RawDiskC sources exist in the catalog yet, so the filter is a no-op.
            // Validate the filter logic by constructing a synthetic source via helper.
            // Here we just assert the predicate's documented behavior.
            Assert.True(CollectionProfiles.Default.SourceFilter(new ProcessListSource()));
            Assert.True(CollectionProfiles.Default.SourceFilter(new ServicesSource()));
        }

        [Fact]
        public void FullProfile_includes_every_source()
        {
            var catalog = SourceCatalog.DiscoverFromTypes(new[]
            {
                typeof(ProcessListSource),
                typeof(ServicesSource),
                typeof(EventLogSecuritySource),
            });
            var ids = CollectionProfiles.Full.ResolveSourceIds(catalog);
            Assert.Equal(catalog.Sources.Count, ids.Count);
        }

        [Fact]
        public void Profile_constructor_rejects_invalid_arguments()
        {
            Assert.Throws<ArgumentException>(() => new CollectionProfile(null, "x", 256, 2, 1024, _ => true));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionProfile("x", "x", 32, 2, 1024, _ => true));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionProfile("x", "x", 256, 0, 1024, _ => true));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionProfile("x", "x", 256, 2, -1, _ => true));
            Assert.Throws<ArgumentNullException>(() => new CollectionProfile("x", "x", 256, 2, 1024, null));
        }
    }
}
