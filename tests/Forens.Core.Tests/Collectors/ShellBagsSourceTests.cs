using System.Linq;
using System.Text;
using Forens.Core.Collection;
using Forens.Core.Collectors;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class ShellBagsSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new ShellBagsSource();
            Assert.Equal("shellbags", src.Metadata.Id);
            Assert.Equal(Category.User, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
        }

        [Fact]
        public void ParseMruListEx_decodes_uint32_array_terminated_by_minus_one()
        {
            byte[] data = new byte[20];
            System.BitConverter.GetBytes(2).CopyTo(data, 0);
            System.BitConverter.GetBytes(0).CopyTo(data, 4);
            System.BitConverter.GetBytes(1).CopyTo(data, 8);
            System.BitConverter.GetBytes(-1).CopyTo(data, 12);
            // After the -1 terminator, garbage at bytes 16-19 should be ignored.
            var order = ShellBagsSource.ParseMruListEx(data);
            Assert.Equal(new[] { 2, 0, 1 }, order);
        }

        [Fact]
        public void ParseMruListEx_returns_empty_for_immediate_terminator()
        {
            byte[] data = System.BitConverter.GetBytes(-1);
            var order = ShellBagsSource.ParseMruListEx(data);
            Assert.Empty(order);
        }

        [Fact]
        public void ExtractUnicodeStrings_finds_runs_of_printable_chars()
        {
            // "abcd" (UTF-16 LE) + null + "EF" (too short) + null + "Hello World" (UTF-16 LE)
            var bytes = new System.IO.MemoryStream();
            bytes.Write(Encoding.Unicode.GetBytes("abcd"), 0, 8);
            bytes.WriteByte(0); bytes.WriteByte(0);
            bytes.Write(Encoding.Unicode.GetBytes("EF"), 0, 4);
            bytes.WriteByte(0); bytes.WriteByte(0);
            bytes.Write(Encoding.Unicode.GetBytes("Hello World"), 0, 22);

            var strings = ShellBagsSource.ExtractUnicodeStrings(bytes.ToArray(), minLen: 3);
            Assert.Contains("abcd", strings);
            Assert.Contains("Hello World", strings);
            Assert.DoesNotContain("EF", strings);
        }

        [Fact]
        public void HexEncode_produces_uppercase_hex()
        {
            byte[] data = { 0x00, 0xDE, 0xAD, 0xBE, 0xEF };
            Assert.Equal("00DEADBEEF", ShellBagsSource.HexEncode(data, 0, data.Length));
        }
    }
}
