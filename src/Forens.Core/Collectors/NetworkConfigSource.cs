using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using Forens.Core.Collection;

namespace Forens.Core.Collectors
{
    public sealed class NetworkConfigSource : IArtifactSource
    {
        public const string SourceId = "network-config";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Network Interface Configuration",
            description: "Network interfaces with addresses, gateways, DNS servers, and MAC addresses.",
            category: Category.Network,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: Array.Empty<ContendedResource>(),
            estimatedMemoryMB: 8,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            NetworkInterface[] interfaces;
            try { interfaces = NetworkInterface.GetAllNetworkInterfaces(); }
            catch (Exception ex)
            {
                ctx.Logger.Warning(ex, "GetAllNetworkInterfaces failed");
                writer.RecordPartial("GetAllNetworkInterfaces failed: " + ex.Message);
                return;
            }

            using (var jl = writer.OpenJsonlFile("interfaces.jsonl"))
            {
                foreach (var nic in interfaces)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        IPInterfaceProperties props = null;
                        try { props = nic.GetIPProperties(); } catch { }

                        var addresses = new List<string>();
                        var gateways = new List<string>();
                        var dnsServers = new List<string>();
                        var dnsSuffixes = new List<string>();

                        if (props != null)
                        {
                            foreach (var u in props.UnicastAddresses ?? Enumerable.Empty<UnicastIPAddressInformation>())
                                addresses.Add(string.Format("{0}/{1}", u.Address, u.IPv4Mask));
                            foreach (var g in props.GatewayAddresses ?? Enumerable.Empty<GatewayIPAddressInformation>())
                                gateways.Add(g.Address?.ToString());
                            foreach (var d in props.DnsAddresses ?? Enumerable.Empty<System.Net.IPAddress>())
                                dnsServers.Add(d.ToString());
                            if (!string.IsNullOrEmpty(props.DnsSuffix))
                                dnsSuffixes.Add(props.DnsSuffix);
                        }

                        string mac = null;
                        try
                        {
                            var pa = nic.GetPhysicalAddress();
                            if (pa != null) mac = FormatMac(pa.GetAddressBytes());
                        }
                        catch { }

                        long? speedBps = null;
                        try { speedBps = nic.Speed; } catch { }

                        jl.Write(new NicRecord
                        {
                            Id = nic.Id,
                            Name = nic.Name,
                            Description = nic.Description,
                            InterfaceType = nic.NetworkInterfaceType.ToString(),
                            OperationalStatus = nic.OperationalStatus.ToString(),
                            IsReceiveOnly = nic.IsReceiveOnly,
                            SupportsMulticast = nic.SupportsMulticast,
                            SpeedBps = speedBps,
                            MacAddress = mac,
                            Addresses = addresses,
                            Gateways = gateways,
                            DnsServers = dnsServers,
                            DnsSuffix = dnsSuffixes.FirstOrDefault()
                        });
                        writer.RecordItem();
                    }
                    catch (Exception ex)
                    {
                        ctx.Logger.Verbose(ex, "Skipped one NIC");
                        writer.RecordPartial("One or more interfaces could not be inspected");
                    }
                }
            }
        }

        internal static string FormatMac(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            var parts = new string[bytes.Length];
            for (int i = 0; i < bytes.Length; i++) parts[i] = bytes[i].ToString("X2");
            return string.Join("-", parts);
        }

        private sealed class NicRecord
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public string InterfaceType { get; set; }
            public string OperationalStatus { get; set; }
            public bool IsReceiveOnly { get; set; }
            public bool SupportsMulticast { get; set; }
            public long? SpeedBps { get; set; }
            public string MacAddress { get; set; }
            public List<string> Addresses { get; set; }
            public List<string> Gateways { get; set; }
            public List<string> DnsServers { get; set; }
            public string DnsSuffix { get; set; }
        }
    }
}
