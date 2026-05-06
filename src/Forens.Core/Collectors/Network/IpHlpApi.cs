using System;
using System.Runtime.InteropServices;

namespace Forens.Core.Collectors.Network
{
    internal static class IpHlpApi
    {
        public const int AF_INET = 2;
        public const int AF_INET6 = 23;

        // TCP_TABLE_CLASS
        public const int TCP_TABLE_OWNER_PID_ALL = 5;
        // UDP_TABLE_CLASS
        public const int UDP_TABLE_OWNER_PID = 1;

        public const int NO_ERROR = 0;
        public const int ERROR_INSUFFICIENT_BUFFER = 122;

        [DllImport("iphlpapi.dll", SetLastError = false)]
        public static extern int GetExtendedTcpTable(
            IntPtr pTcpTable,
            ref int dwOutBufLen,
            bool sort,
            int ipVersion,
            int tableClass,
            int reserved);

        [DllImport("iphlpapi.dll", SetLastError = false)]
        public static extern int GetExtendedUdpTable(
            IntPtr pUdpTable,
            ref int dwOutBufLen,
            bool sort,
            int ipVersion,
            int tableClass,
            int reserved);

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCPROW_OWNER_PID
        {
            public uint dwState;
            public uint dwLocalAddr;
            public uint dwLocalPort;   // network byte order, low 16 bits
            public uint dwRemoteAddr;
            public uint dwRemotePort;  // network byte order, low 16 bits
            public uint dwOwningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCP6ROW_OWNER_PID
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] LocalAddr;
            public uint dwLocalScopeId;
            public uint dwLocalPort;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] RemoteAddr;
            public uint dwRemoteScopeId;
            public uint dwRemotePort;
            public uint dwState;
            public uint dwOwningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_UDPROW_OWNER_PID
        {
            public uint dwLocalAddr;
            public uint dwLocalPort;
            public uint dwOwningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_UDP6ROW_OWNER_PID
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] LocalAddr;
            public uint dwLocalScopeId;
            public uint dwLocalPort;
            public uint dwOwningPid;
        }

        public static string TcpStateName(uint state)
        {
            switch (state)
            {
                case 1: return "Closed";
                case 2: return "Listen";
                case 3: return "SynSent";
                case 4: return "SynRcvd";
                case 5: return "Established";
                case 6: return "FinWait1";
                case 7: return "FinWait2";
                case 8: return "CloseWait";
                case 9: return "Closing";
                case 10: return "LastAck";
                case 11: return "TimeWait";
                case 12: return "DeleteTcb";
                default: return "Unknown(" + state + ")";
            }
        }

        public static int NetworkPort(uint dwPort)
        {
            // dwPort is stored with the port number in the low 16 bits,
            // already in host byte order on Windows. Some samples advise
            // ntohs; testing shows the values are little-endian uint with
            // the actual port in the low byte pair, byte-swapped.
            // Pattern for GetExtended*Table: low 16 bits, network byte order.
            ushort be = (ushort)(dwPort & 0xFFFF);
            return ((be & 0xFF) << 8) | ((be >> 8) & 0xFF);
        }

        public static string Ipv4ToString(uint addr)
        {
            // Stored in network byte order (little-end first byte = first octet).
            byte b0 = (byte)(addr & 0xFF);
            byte b1 = (byte)((addr >> 8) & 0xFF);
            byte b2 = (byte)((addr >> 16) & 0xFF);
            byte b3 = (byte)((addr >> 24) & 0xFF);
            return string.Format("{0}.{1}.{2}.{3}", b0, b1, b2, b3);
        }

        public static string Ipv6ToString(byte[] addr, uint scopeId)
        {
            if (addr == null || addr.Length != 16) return "";
            try
            {
                var ip = new System.Net.IPAddress(addr, scopeId);
                return ip.ToString();
            }
            catch
            {
                return BitConverter.ToString(addr).Replace("-", "");
            }
        }
    }
}
