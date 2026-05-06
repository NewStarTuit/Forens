using System.IO;
using System.Linq;
using Forens.Core.Collection;
using Forens.Core.Collectors;
using Forens.Core.Collectors.Network;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class NetworkConnectionsSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new NetworkConnectionsSource();
            Assert.Equal("network-connections", src.Metadata.Id);
            Assert.Equal(Category.Network, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
            Assert.True(src.Metadata.SupportsProcessFilter);
            Assert.Equal(ProcessFilterMode.LivePid, src.Metadata.ProcessFilterMode);
        }

        [Theory]
        [InlineData(1, "Closed")]
        [InlineData(2, "Listen")]
        [InlineData(5, "Established")]
        [InlineData(11, "TimeWait")]
        [InlineData(99, "Unknown(99)")]
        public void TcpStateName_decodes_known_states(uint state, string expected)
        {
            Assert.Equal(expected, IpHlpApi.TcpStateName(state));
        }

        [Theory]
        // dwPort stores the port in low 16 bits, with network byte order — so the bytes
        // appear swapped when read as little-endian uint16. Examples below show the
        // raw uint value the kernel gives us and the actual decoded port number.
        [InlineData(0x5000u, 80)]    // 0x50 0x00 → port 80
        [InlineData(0x0100u, 1)]     // 0x01 0x00 → port 1
        [InlineData(0x3500u, 53)]    // 0x35 0x00 → port 53
        [InlineData(0xBB01u, 443)]   // 0x01 0xBB → port 443
        public void NetworkPort_swaps_low_two_bytes(uint dwPort, int expected)
        {
            Assert.Equal(expected, IpHlpApi.NetworkPort(dwPort));
        }

        [Fact]
        public void Ipv4ToString_produces_dotted_quad()
        {
            // 0x0100007F = 1.0.0.127 in network byte order? Actually MIB stores in
            // little-endian dword form where bytes are read in order: byte0 = first octet.
            uint addr = 0x0100A8C0u; // 192.168.0.1
            Assert.Equal("192.168.0.1", IpHlpApi.Ipv4ToString(addr));
        }

        [Fact]
        public void Ipv6ToString_falls_back_to_hex_for_invalid_input()
        {
            Assert.Equal("", IpHlpApi.Ipv6ToString(null, 0));
            Assert.Equal("", IpHlpApi.Ipv6ToString(new byte[15], 0));
        }

        [Fact]
        public void Live_collect_against_this_host_produces_at_least_one_record()
        {
            string dir = Path.Combine(Path.GetTempPath(), "forens-net-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var src = new NetworkConnectionsSource();
                var ctx = TestContexts.Build(Forens.Common.Host.ElevationState.NotElevated);
                using (var w = new StreamingOutputWriter(dir, "raw/network-connections"))
                {
                    src.Collect(ctx, w);
                    Assert.True(w.ItemsCollected > 0, "expected at least one connection");
                }
                var first = JObject.Parse(File.ReadAllLines(Path.Combine(dir, "connections.jsonl")).First());
                Assert.NotNull(first["protocol"]);
                Assert.NotNull(first["family"]);
                Assert.NotNull(first["localAddress"]);
                Assert.NotNull(first["localPort"]);
                Assert.NotNull(first["pid"]);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
