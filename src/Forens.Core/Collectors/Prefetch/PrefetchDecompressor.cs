using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Forens.Core.Collectors.Prefetch
{
    /// <summary>
    /// Decompresses MAM-wrapped Windows Prefetch files (Win 8.0+ default).
    /// Wraps ntdll!RtlDecompressBufferEx with COMPRESSION_FORMAT_XPRESS_HUFF.
    /// </summary>
    internal static class PrefetchDecompressor
    {
        private const ushort COMPRESSION_FORMAT_XPRESS_HUFF = 0x0004;
        private const ushort COMPRESSION_ENGINE_MAXIMUM = 0x0100;

        // NTSTATUS - any non-zero value indicates failure for our purposes.
        [DllImport("ntdll.dll", ExactSpelling = true)]
        private static extern int RtlGetCompressionWorkSpaceSize(
            ushort CompressionFormatAndEngine,
            out uint CompressBufferAndWorkSpaceSize,
            out uint CompressFragmentWorkSpaceSize);

        [DllImport("ntdll.dll", ExactSpelling = true)]
        private static extern int RtlDecompressBufferEx(
            ushort CompressionFormat,
            byte[] UncompressedBuffer,
            uint UncompressedBufferSize,
            byte[] CompressedBuffer,
            uint CompressedBufferSize,
            out uint FinalUncompressedSize,
            IntPtr WorkSpace);

        public static bool IsMamCompressed(byte[] data)
        {
            return data != null && data.Length >= 4
                && data[0] == 0x4D && data[1] == 0x41 && data[2] == 0x4D; // "MAM"
        }

        public static byte[] Decompress(byte[] mamData)
        {
            if (!IsMamCompressed(mamData))
                throw new ArgumentException("Buffer is not MAM-compressed", nameof(mamData));
            if (mamData.Length < 8)
                throw new ArgumentException("MAM-compressed buffer too short", nameof(mamData));

            // Bytes 4..7 are the decompressed size (little-endian uint32).
            int uncompressedSize = BitConverter.ToInt32(mamData, 4);
            if (uncompressedSize <= 0 || uncompressedSize > 100_000_000)
                throw new ArgumentException("MAM-compressed buffer reports absurd uncompressed size: " + uncompressedSize, nameof(mamData));

            uint workSpaceSize, fragmentSize;
            int rc = RtlGetCompressionWorkSpaceSize(
                (ushort)(COMPRESSION_FORMAT_XPRESS_HUFF | COMPRESSION_ENGINE_MAXIMUM),
                out workSpaceSize, out fragmentSize);
            if (rc != 0)
                throw new Win32Exception(rc & 0xFFFF, "RtlGetCompressionWorkSpaceSize failed: 0x" + rc.ToString("X8"));

            IntPtr workspace = Marshal.AllocHGlobal((int)workSpaceSize);
            try
            {
                byte[] uncompressed = new byte[uncompressedSize];
                // Compressed payload starts at offset 8 (skipping the MAM header).
                byte[] compressed = new byte[mamData.Length - 8];
                Array.Copy(mamData, 8, compressed, 0, compressed.Length);

                uint finalSize;
                rc = RtlDecompressBufferEx(
                    COMPRESSION_FORMAT_XPRESS_HUFF,
                    uncompressed, (uint)uncompressed.Length,
                    compressed, (uint)compressed.Length,
                    out finalSize, workspace);
                if (rc != 0)
                    throw new Win32Exception(rc & 0xFFFF, "RtlDecompressBufferEx failed: 0x" + rc.ToString("X8"));

                if (finalSize != uncompressed.Length)
                {
                    byte[] trimmed = new byte[finalSize];
                    Array.Copy(uncompressed, trimmed, finalSize);
                    return trimmed;
                }
                return uncompressed;
            }
            finally { Marshal.FreeHGlobal(workspace); }
        }
    }
}
