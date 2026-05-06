using System;
using System.Threading;
using Forens.Core.Collection;
using Xunit;

namespace Forens.Core.Tests.Collection
{
    public class DiskFloorWatchdogTests
    {
        [Fact]
        public void Triggers_and_cancels_when_free_space_below_floor()
        {
            using (var cts = new CancellationTokenSource())
            {
                string capturedReason = null;
                using (var dog = new DiskFloorWatchdog(
                    new FixedFreeSpace(100),
                    floorBytes: 1000,
                    cts: cts,
                    onTrigger: r => capturedReason = r,
                    pollInterval: TimeSpan.FromMilliseconds(20)))
                {
                    Assert.True(SpinUntil(() => dog.Triggered, 2000));
                    Assert.True(cts.IsCancellationRequested);
                    Assert.NotNull(capturedReason);
                    Assert.Contains("100", capturedReason);
                }
            }
        }

        [Fact]
        public void Does_not_trigger_when_free_space_above_floor()
        {
            using (var cts = new CancellationTokenSource())
            using (var dog = new DiskFloorWatchdog(
                new FixedFreeSpace(10_000),
                floorBytes: 1000,
                cts: cts,
                onTrigger: _ => { },
                pollInterval: TimeSpan.FromMilliseconds(20)))
            {
                Thread.Sleep(150);
                Assert.False(dog.Triggered);
                Assert.False(cts.IsCancellationRequested);
            }
        }

        [Fact]
        public void Safe_to_dispose_repeatedly()
        {
            using (var cts = new CancellationTokenSource())
            {
                var dog = new DiskFloorWatchdog(
                    new FixedFreeSpace(10_000),
                    floorBytes: 1000,
                    cts: cts,
                    onTrigger: _ => { },
                    pollInterval: TimeSpan.FromMilliseconds(20));
                dog.Dispose();
                dog.Dispose();
            }
        }

        private static bool SpinUntil(Func<bool> condition, int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                if (condition()) return true;
                Thread.Sleep(20);
                waited += 20;
            }
            return false;
        }

        private sealed class FixedFreeSpace : IDriveProbe
        {
            private readonly long _free;
            public FixedFreeSpace(long free) { _free = free; }
            public long GetAvailableFreeSpace() { return _free; }
        }
    }
}
