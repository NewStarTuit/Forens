using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Forens.Core.Collection;
using Xunit;

namespace Forens.Core.Tests.Collection
{
    public class SchedulerTests
    {
        [Fact]
        public async Task Parallelism_cap_is_honored()
        {
            int active = 0, peak = 0;
            object gate = new object();
            var sources = new List<IArtifactSource>();
            for (int i = 0; i < 8; i++) sources.Add(new InstrumentedSource("s" + i, ContendedResource.None));

            var scheduler = new Scheduler(maxParallelism: 3);
            await scheduler.RunAsync(sources, async (src, ct) =>
            {
                lock (gate)
                {
                    active++;
                    if (active > peak) peak = active;
                }
                await Task.Delay(20, ct);
                lock (gate) { active--; }
            }, CancellationToken.None);

            Assert.True(peak <= 3, "peak concurrency was " + peak);
            Assert.True(peak >= 2, "expected at least 2 concurrent at parallelism 3 with 8 sources");
        }

        [Fact]
        public async Task Sources_sharing_a_resource_serialize()
        {
            int activeOnResource = 0, peakOnResource = 0;
            object gate = new object();
            var s1 = new InstrumentedSource("a", ContendedResource.RegistryHiveSoftware);
            var s2 = new InstrumentedSource("b", ContendedResource.RegistryHiveSoftware);
            var s3 = new InstrumentedSource("c", ContendedResource.RegistryHiveSoftware);

            var scheduler = new Scheduler(maxParallelism: 8);
            await scheduler.RunAsync(new[] { (IArtifactSource)s1, s2, s3 }, async (src, ct) =>
            {
                lock (gate)
                {
                    activeOnResource++;
                    if (activeOnResource > peakOnResource) peakOnResource = activeOnResource;
                }
                await Task.Delay(15, ct);
                lock (gate) activeOnResource--;
            }, CancellationToken.None);

            Assert.Equal(1, peakOnResource);
        }

        [Fact]
        public async Task Cancellation_propagates_to_in_flight_sources()
        {
            using (var cts = new CancellationTokenSource())
            {
                var sources = new List<IArtifactSource>();
                for (int i = 0; i < 4; i++) sources.Add(new InstrumentedSource("s" + i, ContendedResource.None));
                var scheduler = new Scheduler(maxParallelism: 4);

                var run = scheduler.RunAsync(sources, async (src, ct) =>
                {
                    await Task.Delay(2000, ct);
                }, cts.Token);

                cts.CancelAfter(50);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            }
        }

        private sealed class InstrumentedSource : IArtifactSource
        {
            public InstrumentedSource(string id, ContendedResource res)
            {
                Metadata = new SourceMetadata(id, id, id, Category.System,
                    false, false, false, ProcessFilterMode.None,
                    res == ContendedResource.None ? Array.Empty<ContendedResource>() : new[] { res },
                    8, null);
            }
            public SourceMetadata Metadata { get; }
            public SourcePrecondition CheckPrecondition(CollectionContext ctx) { return SourcePrecondition.Ok(); }
            public void Collect(CollectionContext ctx, ISourceWriter writer) { }
        }
    }
}
