using System;
using System.IO;

namespace Forens.Core.Collection
{
    public interface IRecordWriter : IDisposable
    {
        void Write<T>(T record);
    }

    public interface ISourceWriter : IDisposable
    {
        IRecordWriter OpenJsonlFile(string relativePath);

        Stream OpenBinaryFile(string relativePath);

        void RecordItem();

        void RecordPartial(string reason);
    }
}
