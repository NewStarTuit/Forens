using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Forens.Core.Collection;
using Forens.Core.Collectors.Network;

namespace Forens.Core.Collectors
{
    public sealed class NetworkConnectionsSource : IArtifactSource
    {
        public const string SourceId = "network-connections";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Network Connections",
            description: "Current TCP and UDP endpoints (IPv4 + IPv6) with owning PID and process name.",
            category: Category.Network,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: true,
            processFilterMode: ProcessFilterMode.LivePid,
            contendedResources: Array.Empty<ContendedResource>(),
            estimatedMemoryMB: 16,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            var pidNames = SnapshotPidNames();

            using (var jl = writer.OpenJsonlFile("connections.jsonl"))
            {
                EmitTcp4(ctx, writer, jl, pidNames);
                EmitTcp6(ctx, writer, jl, pidNames);
                EmitUdp4(ctx, writer, jl, pidNames);
                EmitUdp6(ctx, writer, jl, pidNames);
            }
        }

        private static Dictionary<int, string> SnapshotPidNames()
        {
            var dict = new Dictionary<int, string>();
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    using (p)
                    {
                        try { dict[p.Id] = p.ProcessName; } catch { }
                    }
                }
            }
            catch { }
            return dict;
        }

        private static void EmitTcp4(CollectionContext ctx, ISourceWriter writer, IRecordWriter jl, Dictionary<int, string> pidNames)
        {
            int size = 0;
            int rc = IpHlpApi.GetExtendedTcpTable(IntPtr.Zero, ref size, false, IpHlpApi.AF_INET, IpHlpApi.TCP_TABLE_OWNER_PID_ALL, 0);
            if (rc != IpHlpApi.ERROR_INSUFFICIENT_BUFFER && rc != IpHlpApi.NO_ERROR)
            {
                ctx.Logger.Warning("GetExtendedTcpTable(AF_INET) sizing failed rc={Rc}", rc);
                writer.RecordPartial("Failed to size TCP IPv4 table");
                return;
            }
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                rc = IpHlpApi.GetExtendedTcpTable(buf, ref size, false, IpHlpApi.AF_INET, IpHlpApi.TCP_TABLE_OWNER_PID_ALL, 0);
                if (rc != IpHlpApi.NO_ERROR)
                {
                    writer.RecordPartial("Failed to read TCP IPv4 table: rc=" + rc);
                    return;
                }
                int count = Marshal.ReadInt32(buf);
                int rowSize = Marshal.SizeOf(typeof(IpHlpApi.MIB_TCPROW_OWNER_PID));
                IntPtr p = IntPtr.Add(buf, 4);
                for (int i = 0; i < count; i++)
                {
                    var row = (IpHlpApi.MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(p, typeof(IpHlpApi.MIB_TCPROW_OWNER_PID));
                    int pid = (int)row.dwOwningPid;
                    if (!ctx.ProcessFilter.Includes(pid)) { p = IntPtr.Add(p, rowSize); continue; }
                    pidNames.TryGetValue(pid, out var name);
                    jl.Write(new ConnRecord
                    {
                        Protocol = "TCP",
                        Family = "IPv4",
                        State = IpHlpApi.TcpStateName(row.dwState),
                        LocalAddress = IpHlpApi.Ipv4ToString(row.dwLocalAddr),
                        LocalPort = IpHlpApi.NetworkPort(row.dwLocalPort),
                        RemoteAddress = IpHlpApi.Ipv4ToString(row.dwRemoteAddr),
                        RemotePort = IpHlpApi.NetworkPort(row.dwRemotePort),
                        Pid = pid,
                        ProcessName = name
                    });
                    writer.RecordItem();
                    p = IntPtr.Add(p, rowSize);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        private static void EmitTcp6(CollectionContext ctx, ISourceWriter writer, IRecordWriter jl, Dictionary<int, string> pidNames)
        {
            int size = 0;
            int rc = IpHlpApi.GetExtendedTcpTable(IntPtr.Zero, ref size, false, IpHlpApi.AF_INET6, IpHlpApi.TCP_TABLE_OWNER_PID_ALL, 0);
            if (rc != IpHlpApi.ERROR_INSUFFICIENT_BUFFER && rc != IpHlpApi.NO_ERROR)
            {
                ctx.Logger.Verbose("TCP6 sizing rc={Rc}", rc);
                return;
            }
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                rc = IpHlpApi.GetExtendedTcpTable(buf, ref size, false, IpHlpApi.AF_INET6, IpHlpApi.TCP_TABLE_OWNER_PID_ALL, 0);
                if (rc != IpHlpApi.NO_ERROR) return;
                int count = Marshal.ReadInt32(buf);
                int rowSize = Marshal.SizeOf(typeof(IpHlpApi.MIB_TCP6ROW_OWNER_PID));
                IntPtr p = IntPtr.Add(buf, 4);
                for (int i = 0; i < count; i++)
                {
                    var row = (IpHlpApi.MIB_TCP6ROW_OWNER_PID)Marshal.PtrToStructure(p, typeof(IpHlpApi.MIB_TCP6ROW_OWNER_PID));
                    int pid = (int)row.dwOwningPid;
                    if (!ctx.ProcessFilter.Includes(pid)) { p = IntPtr.Add(p, rowSize); continue; }
                    pidNames.TryGetValue(pid, out var name);
                    jl.Write(new ConnRecord
                    {
                        Protocol = "TCP",
                        Family = "IPv6",
                        State = IpHlpApi.TcpStateName(row.dwState),
                        LocalAddress = IpHlpApi.Ipv6ToString(row.LocalAddr, row.dwLocalScopeId),
                        LocalPort = IpHlpApi.NetworkPort(row.dwLocalPort),
                        RemoteAddress = IpHlpApi.Ipv6ToString(row.RemoteAddr, row.dwRemoteScopeId),
                        RemotePort = IpHlpApi.NetworkPort(row.dwRemotePort),
                        Pid = pid,
                        ProcessName = name
                    });
                    writer.RecordItem();
                    p = IntPtr.Add(p, rowSize);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        private static void EmitUdp4(CollectionContext ctx, ISourceWriter writer, IRecordWriter jl, Dictionary<int, string> pidNames)
        {
            int size = 0;
            int rc = IpHlpApi.GetExtendedUdpTable(IntPtr.Zero, ref size, false, IpHlpApi.AF_INET, IpHlpApi.UDP_TABLE_OWNER_PID, 0);
            if (rc != IpHlpApi.ERROR_INSUFFICIENT_BUFFER && rc != IpHlpApi.NO_ERROR) return;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                rc = IpHlpApi.GetExtendedUdpTable(buf, ref size, false, IpHlpApi.AF_INET, IpHlpApi.UDP_TABLE_OWNER_PID, 0);
                if (rc != IpHlpApi.NO_ERROR) return;
                int count = Marshal.ReadInt32(buf);
                int rowSize = Marshal.SizeOf(typeof(IpHlpApi.MIB_UDPROW_OWNER_PID));
                IntPtr p = IntPtr.Add(buf, 4);
                for (int i = 0; i < count; i++)
                {
                    var row = (IpHlpApi.MIB_UDPROW_OWNER_PID)Marshal.PtrToStructure(p, typeof(IpHlpApi.MIB_UDPROW_OWNER_PID));
                    int pid = (int)row.dwOwningPid;
                    if (!ctx.ProcessFilter.Includes(pid)) { p = IntPtr.Add(p, rowSize); continue; }
                    pidNames.TryGetValue(pid, out var name);
                    jl.Write(new ConnRecord
                    {
                        Protocol = "UDP",
                        Family = "IPv4",
                        State = "Listen",
                        LocalAddress = IpHlpApi.Ipv4ToString(row.dwLocalAddr),
                        LocalPort = IpHlpApi.NetworkPort(row.dwLocalPort),
                        Pid = pid,
                        ProcessName = name
                    });
                    writer.RecordItem();
                    p = IntPtr.Add(p, rowSize);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        private static void EmitUdp6(CollectionContext ctx, ISourceWriter writer, IRecordWriter jl, Dictionary<int, string> pidNames)
        {
            int size = 0;
            int rc = IpHlpApi.GetExtendedUdpTable(IntPtr.Zero, ref size, false, IpHlpApi.AF_INET6, IpHlpApi.UDP_TABLE_OWNER_PID, 0);
            if (rc != IpHlpApi.ERROR_INSUFFICIENT_BUFFER && rc != IpHlpApi.NO_ERROR) return;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                rc = IpHlpApi.GetExtendedUdpTable(buf, ref size, false, IpHlpApi.AF_INET6, IpHlpApi.UDP_TABLE_OWNER_PID, 0);
                if (rc != IpHlpApi.NO_ERROR) return;
                int count = Marshal.ReadInt32(buf);
                int rowSize = Marshal.SizeOf(typeof(IpHlpApi.MIB_UDP6ROW_OWNER_PID));
                IntPtr p = IntPtr.Add(buf, 4);
                for (int i = 0; i < count; i++)
                {
                    var row = (IpHlpApi.MIB_UDP6ROW_OWNER_PID)Marshal.PtrToStructure(p, typeof(IpHlpApi.MIB_UDP6ROW_OWNER_PID));
                    int pid = (int)row.dwOwningPid;
                    if (!ctx.ProcessFilter.Includes(pid)) { p = IntPtr.Add(p, rowSize); continue; }
                    pidNames.TryGetValue(pid, out var name);
                    jl.Write(new ConnRecord
                    {
                        Protocol = "UDP",
                        Family = "IPv6",
                        State = "Listen",
                        LocalAddress = IpHlpApi.Ipv6ToString(row.LocalAddr, row.dwLocalScopeId),
                        LocalPort = IpHlpApi.NetworkPort(row.dwLocalPort),
                        Pid = pid,
                        ProcessName = name
                    });
                    writer.RecordItem();
                    p = IntPtr.Add(p, rowSize);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        private sealed class ConnRecord
        {
            public string Protocol { get; set; }
            public string Family { get; set; }
            public string State { get; set; }
            public string LocalAddress { get; set; }
            public int LocalPort { get; set; }
            public string RemoteAddress { get; set; }
            public int? RemotePort { get; set; }
            public int Pid { get; set; }
            public string ProcessName { get; set; }
        }
    }
}
