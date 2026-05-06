using System;
using System.Text;
using Forens.Core.Collection;
using Forens.Core.Collectors;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class RegistrySamSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new RegistrySamSource();
            Assert.Equal("registry-sam", src.Metadata.Id);
            Assert.Equal(Category.User, src.Metadata.Category);
            Assert.True(src.Metadata.RequiresElevation);
            Assert.False(src.Metadata.SupportsTimeRange);
            Assert.False(src.Metadata.SupportsProcessFilter);
            Assert.Contains(ContendedResource.RegistryHiveSam, src.Metadata.ContendedResources);
        }

        [Fact]
        public void Precondition_skips_when_unprivileged()
        {
            var src = new RegistrySamSource();
            var ctx = TestContexts.Build(Forens.Common.Host.ElevationState.NotElevated);
            var pre = src.CheckPrecondition(ctx);
            Assert.Equal(PreconditionResult.SkipRequiresElevation, pre.Result);
            Assert.Contains("SeBackupPrivilege", pre.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ParseFRecord_extracts_timestamps_flags_and_logon_count()
        {
            byte[] f = new byte[80];
            // last logon at 0x08 — 2026-02-01T12:00:00Z
            long lastLogon = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
            BitConverter.GetBytes(lastLogon).CopyTo(f, 0x08);
            // password last set at 0x18 — 2025-09-15T08:00:00Z
            long pwdSet = new DateTime(2025, 9, 15, 8, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
            BitConverter.GetBytes(pwdSet).CopyTo(f, 0x18);
            // account flags at 0x38: NormalAccount(0x10) | DontExpirePassword(0x200) = 0x210
            BitConverter.GetBytes(0x00000210u).CopyTo(f, 0x38);
            // failed login count uint16 at 0x40
            BitConverter.GetBytes((ushort)2).CopyTo(f, 0x40);
            // logon count uint16 at 0x42
            BitConverter.GetBytes((ushort)17).CopyTo(f, 0x42);

            var rec = RegistrySamSource.ParseFRecord(f);
            Assert.NotNull(rec.LastLogonUtc);
            Assert.Equal(2026, rec.LastLogonUtc.Value.Year);
            Assert.Equal(2, rec.LastLogonUtc.Value.Month);
            Assert.NotNull(rec.PasswordLastSetUtc);
            Assert.Equal(2025, rec.PasswordLastSetUtc.Value.Year);
            Assert.Equal("0x00000210", rec.AccountFlagsHex);
            Assert.Contains("NormalAccount", rec.AccountFlagsDecoded);
            Assert.Contains("DontExpirePassword", rec.AccountFlagsDecoded);
            Assert.DoesNotContain("Disabled", rec.AccountFlagsDecoded);
            Assert.Equal((ushort?)2, rec.FailedLoginCount);
            Assert.Equal((ushort?)17, rec.LogonCount);
        }

        [Fact]
        public void ParseFRecord_returns_empty_record_for_truncated_input()
        {
            var rec = RegistrySamSource.ParseFRecord(new byte[10]);
            Assert.Null(rec.LastLogonUtc);
            Assert.Null(rec.AccountFlagsHex);
        }

        [Fact]
        public void ExtractUsernameFromV_decodes_name_at_offset_table_position()
        {
            // V record layout: 0xCC byte header followed by string area. Header at 0x0C-0x10
            // is name's relative offset, 0x10-0x14 is name byte length. Name UTF-16 LE.
            string name = "Administrator";
            byte[] nameBytes = Encoding.Unicode.GetBytes(name);
            byte[] v = new byte[0xCC + nameBytes.Length + 16];
            // Place name 16 bytes into the strings area.
            int nameRelativeOffset = 16;
            Array.Copy(nameBytes, 0, v, 0xCC + nameRelativeOffset, nameBytes.Length);
            BitConverter.GetBytes((uint)nameRelativeOffset).CopyTo(v, 0x0C);
            BitConverter.GetBytes((uint)nameBytes.Length).CopyTo(v, 0x10);

            string extracted = RegistrySamSource.ExtractUsernameFromV(v);
            Assert.Equal(name, extracted);
        }

        [Fact]
        public void ExtractUsernameFromV_returns_null_for_garbage()
        {
            Assert.Null(RegistrySamSource.ExtractUsernameFromV(null));
            Assert.Null(RegistrySamSource.ExtractUsernameFromV(new byte[10]));
        }

        // ----------------------------------------------------------------
        // SC-006 / FR-013: this source MUST NEVER extract NTLM/LM hashes.
        // We assert that the public surface of the SAM record contains
        // ZERO hash-shaped fields. If a future refactor accidentally
        // added a "PasswordHash" / "NtHash" / "LmHash" property, this
        // test fails immediately.
        // ----------------------------------------------------------------
        [Fact]
        public void SamUserRecord_has_no_hash_fields()
        {
            var t = typeof(RegistrySamSource).GetNestedType("SamUserRecord",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(t);
            foreach (var prop in t.GetProperties())
            {
                string n = prop.Name.ToLowerInvariant();
                Assert.DoesNotContain("hash", n);
                Assert.DoesNotContain("ntlm", n);
                Assert.DoesNotContain("lmhash", n);
                Assert.DoesNotContain("secret", n);
            }
        }
    }
}
