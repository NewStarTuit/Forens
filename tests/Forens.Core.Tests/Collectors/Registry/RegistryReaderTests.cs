using System.Linq;
using Forens.Core.Collectors.Registry;
using Microsoft.Win32;
using Xunit;

namespace Forens.Core.Tests.Collectors.Registry
{
    public class RegistryReaderTests
    {
        [Fact]
        public void EnumerateSubkeys_returns_more_than_zero_for_a_known_HKLM_key()
        {
            var subkeys = RegistryReader.EnumerateSubkeys(
                RegistryHive.LocalMachine, RegistryView.Registry64,
                @"SOFTWARE\Microsoft\Windows").ToArray();
            Assert.NotEmpty(subkeys);
        }

        [Fact]
        public void EnumerateSubkeys_returns_empty_for_a_non_existent_key()
        {
            var subkeys = RegistryReader.EnumerateSubkeys(
                RegistryHive.LocalMachine, RegistryView.Registry64,
                @"SOFTWARE\This\Path\Does\Not\Exist\___" + System.Guid.NewGuid().ToString("N")).ToArray();
            Assert.Empty(subkeys);
        }

        [Fact]
        public void EnumerateValues_returns_empty_for_a_non_existent_key_and_does_not_throw()
        {
            var values = RegistryReader.EnumerateValues(
                RegistryHive.LocalMachine, RegistryView.Registry64,
                @"SOFTWARE\This\Path\Does\Not\Exist\___" + System.Guid.NewGuid().ToString("N")).ToArray();
            Assert.Empty(values);
        }

        [Fact]
        public void Subkey_records_carry_hive_label_and_full_path()
        {
            var subkeys = RegistryReader.EnumerateSubkeys(
                RegistryHive.LocalMachine, RegistryView.Registry64,
                @"SOFTWARE\Microsoft\Windows").Take(1).ToArray();
            Assert.Single(subkeys);
            Assert.Equal("HKLM", subkeys[0].Hive);
            Assert.StartsWith(@"SOFTWARE\Microsoft\Windows\", subkeys[0].FullKeyPath);
        }
    }
}
