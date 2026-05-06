using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Forens.Core.Collection
{
    public sealed class Scheduler
    {
        private readonly int _maxParallelism;

        public Scheduler(int maxParallelism)
        {
            if (maxParallelism < 1) throw new ArgumentOutOfRangeException(nameof(maxParallelism));
            _maxParallelism = maxParallelism;
        }

        public Task RunAsync(
            IEnumerable<IArtifactSource> sources,
            Func<IArtifactSource, CancellationToken, Task> runOne,
            CancellationToken cancellationToken)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            if (runOne == null) throw new ArgumentNullException(nameof(runOne));

            var globalGate = new SemaphoreSlim(_maxParallelism, _maxParallelism);
            var resourceLocks = new ConcurrentDictionary<ContendedResource, SemaphoreSlim>();
            var ordered = sources
                .OrderByDescending(s => s.Metadata.EstimatedMemoryMB)
                .ThenBy(s => s.Metadata.Id, StringComparer.Ordinal)
                .ToArray();

            var tasks = new List<Task>(ordered.Length);
            foreach (var src in ordered)
            {
                tasks.Add(RunOneAsync(src, runOne, globalGate, resourceLocks, cancellationToken));
            }
            return Task.WhenAll(tasks);
        }

        private static async Task RunOneAsync(
            IArtifactSource src,
            Func<IArtifactSource, CancellationToken, Task> runOne,
            SemaphoreSlim globalGate,
            ConcurrentDictionary<ContendedResource, SemaphoreSlim> resourceLocks,
            CancellationToken ct)
        {
            await globalGate.WaitAsync(ct).ConfigureAwait(false);
            var taken = new List<SemaphoreSlim>();
            try
            {
                foreach (var res in src.Metadata.ContendedResources
                    .Where(r => r != ContendedResource.None)
                    .Distinct()
                    .OrderBy(r => (int)r))
                {
                    var sem = resourceLocks.GetOrAdd(res, _ => new SemaphoreSlim(1, 1));
                    await sem.WaitAsync(ct).ConfigureAwait(false);
                    taken.Add(sem);
                }
                await runOne(src, ct).ConfigureAwait(false);
            }
            finally
            {
                foreach (var sem in taken) sem.Release();
                globalGate.Release();
            }
        }
    }
}
