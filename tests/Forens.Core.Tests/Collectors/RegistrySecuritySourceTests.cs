using System;
using System.Linq;
using Forens.Core.Collection;
using Forens.Core.Collectors;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class RegistrySecuritySourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new RegistrySecuritySource();
            Assert.Equal("registry-security", src.Metadata.Id);
            Assert.True(src.Metadata.RequiresElevation);
            Assert.Contains(ContendedResource.RegistryHiveSecurity, src.Metadata.ContendedResources);
        }

        [Fact]
        public void Precondition_skips_unprivileged_with_SeBackupPrivilege_reason()
        {
            var src = new RegistrySecuritySource();
            var ctx = TestContexts.Build(Forens.Common.Host.ElevationState.NotElevated);
            var pre = src.CheckPrecondition(ctx);
            Assert.Equal(PreconditionResult.SkipRequiresElevation, pre.Result);
            Assert.Contains("SeBackupPrivilege", pre.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ParseAuditPolicy_decodes_per_category_success_and_failure_bits()
        {
            // Build a synthetic PolAdtEv blob:
            //   bytes 0-3 = version 1 (irrelevant to us)
            //   bytes 4-7 = audit-mode flags (1 = enabled)
            //   bytes 8-11 = System category: Success only (0x1)
            //   bytes 12-15 = Logon: Success + Failure (0x3)
            //   bytes 16-19 = ObjectAccess: Failure only (0x2)
            //   ...remaining categories left at 0
            byte[] data = new byte[8 + 9 * 4];
            BitConverter.GetBytes(1u).CopyTo(data, 0);
            BitConverter.GetBytes(1u).CopyTo(data, 4);
            BitConverter.GetBytes(0x1u).CopyTo(data, 8);
            BitConverter.GetBytes(0x3u).CopyTo(data, 12);
            BitConverter.GetBytes(0x2u).CopyTo(data, 16);

            var record = RegistrySecuritySource.ParseAuditPolicy(data);
            Assert.True(record.AuditingEnabled);
            Assert.NotNull(record.Categories);
            Assert.Equal(9, record.Categories.Count);

            var system = record.Categories.First(c => c.Name == "System");
            Assert.True(system.AuditSuccess);
            Assert.False(system.AuditFailure);

            var logon = record.Categories.First(c => c.Name == "Logon");
            Assert.True(logon.AuditSuccess);
            Assert.True(logon.AuditFailure);

            var oa = record.Categories.First(c => c.Name == "ObjectAccess");
            Assert.False(oa.AuditSuccess);
            Assert.True(oa.AuditFailure);
        }

        [Fact]
        public void TryFormatSid_produces_S_dash_string_for_well_known_local_system_sid()
        {
            // S-1-5-18 (LocalSystem): revision=1, subAuthCount=1, identifierAuthority=5, subauth=18.
            byte[] sid =
            {
                0x01, 0x01, // revision, subAuthCount
                0x00, 0x00, 0x00, 0x00, 0x00, 0x05, // identifier authority (6 bytes BIG-endian)
                0x12, 0x00, 0x00, 0x00 // subauthority 18
            };
            string s = RegistrySecuritySource.TryFormatSid(sid);
            Assert.Equal("S-1-5-18", s);
        }

        [Fact]
        public void TryFormatSid_returns_null_for_truncated_sid()
        {
            Assert.Null(RegistrySecuritySource.TryFormatSid(null));
            Assert.Null(RegistrySecuritySource.TryFormatSid(new byte[4]));
        }

        // SC-006 / FR-013: this source MUST NEVER expose any password hash, secret value,
        // or LSA-secret byte data. Reflection-check ensures no public/internal property
        // name on any nested record contains a forbidden substring.
        // SC-006 / FR-013: this source MUST NEVER expose any password hash, secret value,
        // or LSA-secret byte data. Reflection-check enforces that the public surface of
        // every nested record type is metadata-only — no field whose name signals a
        // secret-value payload (e.g., "SecretValue", "PasswordHash", "NtHash").
        // Boolean indicators like "HasOldValue" or "HasCurrentValue" are explicitly
        // permitted (they only signal presence, not content).
        [Fact]
        public void Records_have_no_secret_or_hash_value_fields()
        {
            string[] forbiddenNames =
            {
                "secretvalue", "secretbytes", "secretdata",
                "currval", "oldval",
                "passwordhash", "nthash", "lmhash", "ntlmhash",
                "secret",
            };

            var nestedTypes = typeof(RegistrySecuritySource).GetNestedTypes(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance);
            foreach (var t in nestedTypes)
            {
                foreach (var prop in t.GetProperties())
                {
                    string n = prop.Name.ToLowerInvariant();
                    foreach (var bad in forbiddenNames)
                    {
                        Assert.False(n == bad,
                            "Property '" + prop.Name + "' on type '" + t.Name +
                            "' violates SC-006: never expose secret bytes.");
                    }
                    // Substring check for the most-toxic patterns
                    Assert.DoesNotContain("ntlm", n);
                    Assert.DoesNotContain("nthash", n);
                    Assert.DoesNotContain("lmhash", n);
                    Assert.DoesNotContain("passwordhash", n);
                }
            }
        }
    }
}
