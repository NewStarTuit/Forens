using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using Forens.Core.Collection;

namespace Forens.Core.Collectors
{
    public sealed class UsersGroupsSource : IArtifactSource
    {
        public const string SourceId = "users-groups";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Local Users and Groups",
            description: "Local user accounts (with SIDs and status) and local groups with their members.",
            category: Category.User,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: new[] { ContendedResource.WmiCimV2 },
            estimatedMemoryMB: 8,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            using (var users = writer.OpenJsonlFile("users.jsonl"))
            {
                EnumerateUsers(ctx, users, writer);
            }
            using (var groups = writer.OpenJsonlFile("groups.jsonl"))
            {
                EnumerateGroups(ctx, groups, writer);
            }
        }

        private static void EnumerateUsers(CollectionContext ctx, IRecordWriter jl, ISourceWriter writer)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(
                    "SELECT Name, FullName, Caption, Description, SID, Disabled, Lockout, PasswordChangeable, PasswordExpires, PasswordRequired, AccountType, LocalAccount, Domain FROM Win32_UserAccount WHERE LocalAccount=TRUE"))
                using (var c = s.Get())
                {
                    foreach (ManagementBaseObject mo in c)
                    {
                        ctx.CancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            jl.Write(new UserRecord
                            {
                                Name = mo["Name"] as string,
                                FullName = mo["FullName"] as string,
                                Caption = mo["Caption"] as string,
                                Description = mo["Description"] as string,
                                Sid = mo["SID"] as string,
                                Domain = mo["Domain"] as string,
                                Disabled = mo["Disabled"] as bool?,
                                Lockout = mo["Lockout"] as bool?,
                                PasswordChangeable = mo["PasswordChangeable"] as bool?,
                                PasswordExpires = mo["PasswordExpires"] as bool?,
                                PasswordRequired = mo["PasswordRequired"] as bool?,
                                AccountType = ToInt(mo["AccountType"]),
                                LocalAccount = mo["LocalAccount"] as bool?
                            });
                            writer.RecordItem();
                        }
                        finally { mo.Dispose(); }
                    }
                }
            }
            catch (Exception ex)
            {
                ctx.Logger.Warning(ex, "Win32_UserAccount enumeration failed");
                writer.RecordPartial("Win32_UserAccount enumeration failed: " + ex.Message);
            }
        }

        private static void EnumerateGroups(CollectionContext ctx, IRecordWriter jl, ISourceWriter writer)
        {
            // Build a member map: GroupSid -> list of user/group names+SIDs
            var members = new Dictionary<string, List<GroupMember>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var s = new ManagementObjectSearcher(
                    "SELECT GroupComponent, PartComponent FROM Win32_GroupUser"))
                using (var c = s.Get())
                {
                    foreach (ManagementBaseObject mo in c)
                    {
                        try
                        {
                            string groupComponent = mo["GroupComponent"]?.ToString();
                            string partComponent = mo["PartComponent"]?.ToString();
                            if (string.IsNullOrEmpty(groupComponent) || string.IsNullOrEmpty(partComponent))
                                continue;

                            string groupName = ExtractRefProp(groupComponent, "Name");
                            string memberDomain = ExtractRefProp(partComponent, "Domain");
                            string memberName = ExtractRefProp(partComponent, "Name");
                            if (string.IsNullOrEmpty(groupName) || string.IsNullOrEmpty(memberName))
                                continue;

                            string key = groupName.ToLowerInvariant();
                            if (!members.TryGetValue(key, out var list))
                            {
                                list = new List<GroupMember>();
                                members[key] = list;
                            }
                            list.Add(new GroupMember { Domain = memberDomain, Name = memberName });
                        }
                        finally { mo.Dispose(); }
                    }
                }
            }
            catch (Exception ex)
            {
                ctx.Logger.Verbose(ex, "Win32_GroupUser enumeration failed; group memberships will be empty");
            }

            try
            {
                using (var s = new ManagementObjectSearcher(
                    "SELECT Name, Caption, Description, SID, Domain, LocalAccount FROM Win32_Group WHERE LocalAccount=TRUE"))
                using (var c = s.Get())
                {
                    foreach (ManagementBaseObject mo in c)
                    {
                        ctx.CancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            string groupName = mo["Name"] as string;
                            members.TryGetValue((groupName ?? "").ToLowerInvariant(), out var memberList);
                            jl.Write(new GroupRecord
                            {
                                Name = groupName,
                                Caption = mo["Caption"] as string,
                                Description = mo["Description"] as string,
                                Sid = mo["SID"] as string,
                                Domain = mo["Domain"] as string,
                                LocalAccount = mo["LocalAccount"] as bool?,
                                Members = memberList ?? new List<GroupMember>()
                            });
                            writer.RecordItem();
                        }
                        finally { mo.Dispose(); }
                    }
                }
            }
            catch (Exception ex)
            {
                ctx.Logger.Warning(ex, "Win32_Group enumeration failed");
                writer.RecordPartial("Win32_Group enumeration failed: " + ex.Message);
            }
        }

        internal static string ExtractRefProp(string componentRef, string propName)
        {
            // Format: Domain="...",Name="..."  appearing after a class reference like Win32_UserAccount.Domain="..."
            int idx = componentRef.IndexOf(propName + "=\"", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            int start = idx + propName.Length + 2;
            int end = componentRef.IndexOf('"', start);
            if (end < 0) return null;
            return componentRef.Substring(start, end - start);
        }

        private static int ToInt(object o)
        {
            if (o == null) return 0;
            try { return Convert.ToInt32(o, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        private sealed class UserRecord
        {
            public string Name { get; set; }
            public string FullName { get; set; }
            public string Caption { get; set; }
            public string Description { get; set; }
            public string Sid { get; set; }
            public string Domain { get; set; }
            public bool? Disabled { get; set; }
            public bool? Lockout { get; set; }
            public bool? PasswordChangeable { get; set; }
            public bool? PasswordExpires { get; set; }
            public bool? PasswordRequired { get; set; }
            public int AccountType { get; set; }
            public bool? LocalAccount { get; set; }
        }

        private sealed class GroupRecord
        {
            public string Name { get; set; }
            public string Caption { get; set; }
            public string Description { get; set; }
            public string Sid { get; set; }
            public string Domain { get; set; }
            public bool? LocalAccount { get; set; }
            public List<GroupMember> Members { get; set; }
        }

        private sealed class GroupMember
        {
            public string Domain { get; set; }
            public string Name { get; set; }
        }
    }
}
