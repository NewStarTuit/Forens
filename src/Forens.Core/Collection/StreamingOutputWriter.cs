using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Forens.Core.Collection.Run;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Forens.Core.Collection
{
    public sealed class StreamingOutputWriter : ISourceWriter
    {
        private const int FileBufferSize = 64 * 1024;

        private readonly string _outputDir;
        private readonly string _relativeRoot;
        private readonly object _lock = new object();
        private readonly List<OutputFile> _finished = new List<OutputFile>();
        private readonly List<IDisposable> _open = new List<IDisposable>();
        private long _items;
        private bool _partial;
        private string _partialReason;
        private bool _disposed;

        public StreamingOutputWriter(string outputDir, string relativeRootForReporting)
        {
            if (string.IsNullOrEmpty(outputDir))
                throw new ArgumentException("outputDir is required.", nameof(outputDir));
            _outputDir = outputDir;
            _relativeRoot = relativeRootForReporting ?? string.Empty;
            Directory.CreateDirectory(_outputDir);
        }

        public IRecordWriter OpenJsonlFile(string relativePath)
        {
            EnsureOpen();
            var w = new JsonlRecordWriter(this, relativePath);
            lock (_lock) _open.Add(w);
            return w;
        }

        public Stream OpenBinaryFile(string relativePath)
        {
            EnsureOpen();
            var s = new BinaryOutputStream(this, relativePath);
            lock (_lock) _open.Add(s);
            return s;
        }

        public void RecordItem()
        {
            Interlocked.Increment(ref _items);
        }

        public void RecordPartial(string reason)
        {
            if (string.IsNullOrEmpty(reason)) reason = "Partial output";
            lock (_lock)
            {
                _partial = true;
                if (_partialReason == null) _partialReason = reason;
            }
        }

        public long ItemsCollected
        {
            get { return Interlocked.Read(ref _items); }
        }

        public bool IsPartial
        {
            get { lock (_lock) return _partial; }
        }

        public string PartialReason
        {
            get { lock (_lock) return _partialReason; }
        }

        public IReadOnlyList<OutputFile> FinishedFiles
        {
            get { lock (_lock) return _finished.ToArray(); }
        }

        public long TotalBytesWritten
        {
            get { lock (_lock) return _finished.Sum(f => f.ByteCount); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            IDisposable[] toClose;
            lock (_lock) toClose = _open.ToArray();
            foreach (var d in toClose)
            {
                try { d.Dispose(); } catch { }
            }
        }

        private void EnsureOpen()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(StreamingOutputWriter));
        }

        private OpenedFile OpenChain(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                throw new ArgumentException("relativePath is required.", nameof(relativePath));
            string norm = relativePath.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(norm))
                throw new ArgumentException("relativePath must be relative.", nameof(relativePath));

            string outDirFull = Path.GetFullPath(_outputDir);
            string absPath = Path.GetFullPath(Path.Combine(outDirFull, norm));
            if (!absPath.StartsWith(outDirFull, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("relativePath must not escape the source output directory.");

            string parent = Path.GetDirectoryName(absPath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            string posixRel = string.IsNullOrEmpty(_relativeRoot)
                ? norm.Replace(Path.DirectorySeparatorChar, '/')
                : _relativeRoot + "/" + norm.Replace(Path.DirectorySeparatorChar, '/');

            var fs = new FileStream(absPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileBufferSize);
            var sha = SHA256.Create();
            var cs = new CryptoStream(fs, sha, CryptoStreamMode.Write);
            return new OpenedFile(fs, sha, cs, absPath, posixRel);
        }

        private void NotifyFinished(IDisposable writer, OutputFile file)
        {
            lock (_lock)
            {
                _open.Remove(writer);
                if (file != null) _finished.Add(file);
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }

        private sealed class OpenedFile
        {
            public OpenedFile(FileStream fs, SHA256 sha, CryptoStream cs, string abs, string posixRel)
            {
                FileStream = fs;
                Hash = sha;
                CryptoStream = cs;
                AbsolutePath = abs;
                RelativePathPosix = posixRel;
            }

            public FileStream FileStream { get; }
            public SHA256 Hash { get; }
            public CryptoStream CryptoStream { get; }
            public string AbsolutePath { get; }
            public string RelativePathPosix { get; }
            public bool Finalized { get; private set; }

            public OutputFile FinalizeAndClose(DateTimeOffset writtenUtc)
            {
                if (Finalized) return null;
                Finalized = true;
                CryptoStream.FlushFinalBlock();
                FileStream.Flush();
                long bytes = FileStream.Length;
                string hex = ToHex(Hash.Hash);
                CryptoStream.Dispose();
                Hash.Dispose();
                return new OutputFile(RelativePathPosix, hex, bytes, writtenUtc);
            }

            public void DiscardClose()
            {
                if (Finalized) return;
                Finalized = true;
                try { CryptoStream.Dispose(); } catch { }
                try { Hash.Dispose(); } catch { }
                try { FileStream.Dispose(); } catch { }
            }
        }

        private sealed class JsonlRecordWriter : IRecordWriter
        {
            private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                DateFormatHandling = DateFormatHandling.IsoDateFormat,
                DateFormatString = "yyyy-MM-ddTHH:mm:ss.fffK",
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.None
            };

            private readonly StreamingOutputWriter _parent;
            private readonly OpenedFile _file;
            private readonly StreamWriter _writer;
            private readonly JsonTextWriter _jsonWriter;
            private readonly JsonSerializer _serializer;
            private bool _disposed;

            public JsonlRecordWriter(StreamingOutputWriter parent, string relativePath)
            {
                _parent = parent;
                _file = parent.OpenChain(relativePath);
                _writer = new StreamWriter(_file.CryptoStream, new UTF8Encoding(false), FileBufferSize, leaveOpen: true)
                {
                    NewLine = "\n"
                };
                _jsonWriter = new JsonTextWriter(_writer) { CloseOutput = false, Formatting = Formatting.None };
                _serializer = JsonSerializer.Create(JsonSettings);
            }

            public void Write<T>(T record)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(JsonlRecordWriter));
                _serializer.Serialize(_jsonWriter, record);
                _jsonWriter.Flush();
                _writer.WriteLine();
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try
                {
                    _writer.Flush();
                    _writer.Dispose();
                    var of = _file.FinalizeAndClose(DateTimeOffset.UtcNow);
                    _parent.NotifyFinished(this, of);
                }
                catch
                {
                    _file.DiscardClose();
                    _parent.NotifyFinished(this, null);
                    throw;
                }
            }
        }

        private sealed class BinaryOutputStream : Stream
        {
            private readonly StreamingOutputWriter _parent;
            private readonly OpenedFile _file;
            private long _written;
            private bool _disposed;

            public BinaryOutputStream(StreamingOutputWriter parent, string relativePath)
            {
                _parent = parent;
                _file = parent.OpenChain(relativePath);
            }

            public override bool CanRead { get { return false; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return !_disposed; } }
            public override long Length { get { return _written; } }
            public override long Position
            {
                get { return _written; }
                set { throw new NotSupportedException(); }
            }

            public override void Flush() { _file.CryptoStream.Flush(); }
            public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(BinaryOutputStream));
                _file.CryptoStream.Write(buffer, offset, count);
                _written += count;
            }

            protected override void Dispose(bool disposing)
            {
                if (_disposed) { base.Dispose(disposing); return; }
                _disposed = true;
                if (disposing)
                {
                    try
                    {
                        var of = _file.FinalizeAndClose(DateTimeOffset.UtcNow);
                        _parent.NotifyFinished(this, of);
                    }
                    catch
                    {
                        _file.DiscardClose();
                        _parent.NotifyFinished(this, null);
                        throw;
                    }
                }
                base.Dispose(disposing);
            }
        }
    }
}
