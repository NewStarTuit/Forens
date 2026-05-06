using System;
using System.IO;
using System.Threading;

namespace Forens.Core.Collection
{
    public interface IDriveProbe
    {
        long GetAvailableFreeSpace();
    }

    internal sealed class FileSystemDriveProbe : IDriveProbe
    {
        private readonly string _path;
        public FileSystemDriveProbe(string path) { _path = path; }
        public long GetAvailableFreeSpace()
        {
            string root = Path.GetPathRoot(Path.GetFullPath(_path));
            if (string.IsNullOrEmpty(root)) return long.MaxValue;
            try { return new DriveInfo(root).AvailableFreeSpace; }
            catch { return long.MaxValue; }
        }
    }

    public sealed class DiskFloorWatchdog : IDisposable
    {
        private readonly IDriveProbe _probe;
        private readonly long _floorBytes;
        private readonly CancellationTokenSource _cts;
        private readonly Action<string> _onTrigger;
        private Timer _timer;
        private int _triggered;

        public DiskFloorWatchdog(
            IDriveProbe probe,
            long floorBytes,
            CancellationTokenSource cts,
            Action<string> onTrigger,
            TimeSpan? pollInterval = null)
        {
            if (probe == null) throw new ArgumentNullException(nameof(probe));
            if (cts == null) throw new ArgumentNullException(nameof(cts));
            if (floorBytes < 0) throw new ArgumentOutOfRangeException(nameof(floorBytes));
            _probe = probe;
            _floorBytes = floorBytes;
            _cts = cts;
            _onTrigger = onTrigger ?? (_ => { });
            var interval = pollInterval ?? TimeSpan.FromSeconds(1);
            _timer = new Timer(Tick, null, interval, interval);
        }

        public static DiskFloorWatchdog ForPath(string outputPath, long floorBytes, CancellationTokenSource cts, Action<string> onTrigger)
        {
            return new DiskFloorWatchdog(new FileSystemDriveProbe(outputPath), floorBytes, cts, onTrigger);
        }

        public bool Triggered { get { return Volatile.Read(ref _triggered) != 0; } }
        public string Reason { get; private set; }

        private void Tick(object state)
        {
            if (Triggered) return;
            long free;
            try { free = _probe.GetAvailableFreeSpace(); }
            catch { return; }
            if (free < _floorBytes)
            {
                if (Interlocked.Exchange(ref _triggered, 1) == 0)
                {
                    Reason = string.Format(
                        "Free disk {0:N0} bytes < floor {1:N0} bytes",
                        free, _floorBytes);
                    try { _onTrigger(Reason); } catch { }
                    try { _cts.Cancel(); } catch { }
                }
            }
        }

        public void Dispose()
        {
            var t = Interlocked.Exchange(ref _timer, null);
            if (t != null) t.Dispose();
        }
    }
}
