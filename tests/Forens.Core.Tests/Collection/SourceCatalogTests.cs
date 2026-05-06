using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Forens.Core.Collection;
using Xunit;

namespace Forens.Core.Tests.Collection
{
    public class SourceCatalogTests
    {
        [Fact]
        public void Discovers_concrete_IArtifactSource_with_parameterless_ctor()
        {
            var cat = SourceCatalog.DiscoverFromTypes(new[]
            {
                typeof(TestSourceA), typeof(TestSourceB),
                typeof(AbstractTestSource), typeof(TestSourceNoCtor)
            });
            Assert.True(cat.Contains("test-source-a"));
            Assert.True(cat.Contains("test-source-b"));
        }

        [Fact]
        public void Ignores_abstract_and_interface_and_non_default_ctor_types()
        {
            var cat = SourceCatalog.DiscoverFromTypes(new[]
            {
                typeof(TestSourceA), typeof(TestSourceB),
                typeof(AbstractTestSource), typeof(TestSourceNoCtor)
            });
            Assert.False(cat.Contains("test-source-noctor"));
            Assert.True(cat.Sources.All(s => s.GetType() != typeof(AbstractTestSource)));
        }

        [Fact]
        public void Duplicate_ids_throw_listing_both_type_names()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                SourceCatalog.DiscoverFromTypes(new[]
                {
                    typeof(DuplicateSourceA), typeof(DuplicateSourceB)
                }));
            Assert.Contains("dup-id", ex.Message);
            Assert.Contains(nameof(DuplicateSourceA), ex.Message);
            Assert.Contains(nameof(DuplicateSourceB), ex.Message);
        }

        // --- fixtures ---

        internal sealed class TestSourceA : IArtifactSource
        {
            public SourceMetadata Metadata { get; } = new SourceMetadata(
                "test-source-a", "Test Source A", "First test source",
                Category.System, false, false, false, ProcessFilterMode.None,
                Array.Empty<ContendedResource>(), 8, null);
            public SourcePrecondition CheckPrecondition(CollectionContext ctx) { return SourcePrecondition.Ok(); }
            public void Collect(CollectionContext ctx, ISourceWriter writer) { }
        }

        internal sealed class TestSourceB : IArtifactSource
        {
            public SourceMetadata Metadata { get; } = new SourceMetadata(
                "test-source-b", "Test Source B", "Second test source",
                Category.System, false, false, false, ProcessFilterMode.None,
                Array.Empty<ContendedResource>(), 8, null);
            public SourcePrecondition CheckPrecondition(CollectionContext ctx) { return SourcePrecondition.Ok(); }
            public void Collect(CollectionContext ctx, ISourceWriter writer) { }
        }

        internal abstract class AbstractTestSource : IArtifactSource
        {
            public abstract SourceMetadata Metadata { get; }
            public abstract SourcePrecondition CheckPrecondition(CollectionContext ctx);
            public abstract void Collect(CollectionContext ctx, ISourceWriter writer);
        }

        internal sealed class TestSourceNoCtor : IArtifactSource
        {
            public TestSourceNoCtor(int x) { _ = x; }
            public SourceMetadata Metadata { get; } = new SourceMetadata(
                "test-source-noctor", "No ctor", "Has non-default ctor",
                Category.System, false, false, false, ProcessFilterMode.None,
                Array.Empty<ContendedResource>(), 8, null);
            public SourcePrecondition CheckPrecondition(CollectionContext ctx) { return SourcePrecondition.Ok(); }
            public void Collect(CollectionContext ctx, ISourceWriter writer) { }
        }

        // Duplicate-id pair (lives on its own marker assembly only via this test class).
        internal sealed class DupAssemblyMarker { }

        internal sealed class DuplicateSourceA : IArtifactSource
        {
            public SourceMetadata Metadata { get; } = MakeMeta();
            public SourcePrecondition CheckPrecondition(CollectionContext ctx) { return SourcePrecondition.Ok(); }
            public void Collect(CollectionContext ctx, ISourceWriter writer) { }
            private static SourceMetadata MakeMeta()
            {
                return new SourceMetadata("dup-id", "A", "A", Category.System,
                    false, false, false, ProcessFilterMode.None,
                    Array.Empty<ContendedResource>(), 8, null);
            }
        }

        internal sealed class DuplicateSourceB : IArtifactSource
        {
            public SourceMetadata Metadata { get; } = MakeMeta();
            public SourcePrecondition CheckPrecondition(CollectionContext ctx) { return SourcePrecondition.Ok(); }
            public void Collect(CollectionContext ctx, ISourceWriter writer) { }
            private static SourceMetadata MakeMeta()
            {
                return new SourceMetadata("dup-id", "B", "B", Category.System,
                    false, false, false, ProcessFilterMode.None,
                    Array.Empty<ContendedResource>(), 8, null);
            }
        }
    }
}
