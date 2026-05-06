using System;
using System.Collections.Generic;
using System.IO;
using Forens.Core.Collection;
using Newtonsoft.Json.Linq;

namespace Forens.Core.Collectors
{
    public sealed class BrowserBookmarksSource : IArtifactSource
    {
        public const string SourceId = "browser-bookmarks";

        public SourceMetadata Metadata { get; } = new SourceMetadata(
            id: SourceId,
            displayName: "Browser Bookmarks (Chromium-family)",
            description: "Bookmarks JSON from Chromium-family browsers (Chrome, Edge, Brave, Vivaldi, Opera) under the operator's profile.",
            category: Category.Browser,
            requiresElevation: false,
            supportsTimeRange: false,
            supportsProcessFilter: false,
            processFilterMode: ProcessFilterMode.None,
            contendedResources: Array.Empty<ContendedResource>(),
            estimatedMemoryMB: 16,
            minWindowsVersion: null);

        public SourcePrecondition CheckPrecondition(CollectionContext ctx)
        {
            return SourcePrecondition.Ok();
        }

        public void Collect(CollectionContext ctx, ISourceWriter writer)
        {
            using (var jl = writer.OpenJsonlFile("bookmarks.jsonl"))
            {
                int found = 0;
                foreach (var profile in EnumerateBrowserProfiles())
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    string bookmarksFile = Path.Combine(profile.ProfileDir, "Bookmarks");
                    if (!File.Exists(bookmarksFile)) continue;

                    try
                    {
                        ParseAndEmit(bookmarksFile, profile, jl, writer);
                        found++;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        writer.RecordPartial("Some Bookmarks files were not readable");
                    }
                    catch (Exception ex)
                    {
                        ctx.Logger.Verbose(ex, "Failed to parse Bookmarks file {Path}", bookmarksFile);
                        writer.RecordPartial("One or more Bookmarks files failed to parse");
                    }
                }

                if (found == 0)
                {
                    ctx.Logger.Verbose("No Chromium-family browser profiles found");
                }
            }
        }

        internal sealed class BrowserProfile
        {
            public string Vendor;
            public string Browser;
            public string ProfileName;
            public string ProfileDir;
        }

        internal static IEnumerable<BrowserProfile> EnumerateBrowserProfiles()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // Common Chromium-family layouts: <root>\<vendor>\<browser>\User Data\<profile>\Bookmarks
            var roots = new[]
            {
                new { Vendor = "Google",   Browser = "Chrome",                Root = Path.Combine(localAppData, "Google", "Chrome", "User Data") },
                new { Vendor = "Microsoft",Browser = "Edge",                  Root = Path.Combine(localAppData, "Microsoft", "Edge", "User Data") },
                new { Vendor = "BraveSoftware", Browser = "Brave-Browser",    Root = Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data") },
                new { Vendor = "Vivaldi",  Browser = "Vivaldi",               Root = Path.Combine(localAppData, "Vivaldi", "User Data") },
                new { Vendor = "Opera Software", Browser = "Opera Stable",    Root = Path.Combine(roaming, "Opera Software", "Opera Stable") },
                new { Vendor = "Chromium", Browser = "Chromium",              Root = Path.Combine(localAppData, "Chromium", "User Data") },
            };

            foreach (var r in roots)
            {
                if (!Directory.Exists(r.Root)) continue;

                // For Chromium-style "User Data" roots, the immediate subfolders are profiles
                // ("Default", "Profile 1", "Profile 2", ...). Opera Stable stores Bookmarks
                // directly in its root.
                bool isOpera = r.Browser == "Opera Stable";
                if (isOpera)
                {
                    if (File.Exists(Path.Combine(r.Root, "Bookmarks")))
                    {
                        yield return new BrowserProfile { Vendor = r.Vendor, Browser = r.Browser, ProfileName = "(default)", ProfileDir = r.Root };
                    }
                    continue;
                }

                string[] subdirs;
                try { subdirs = Directory.GetDirectories(r.Root); }
                catch { continue; }

                foreach (var sub in subdirs)
                {
                    string name = Path.GetFileName(sub);
                    if (string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("Guest ", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return new BrowserProfile { Vendor = r.Vendor, Browser = r.Browser, ProfileName = name, ProfileDir = sub };
                    }
                }
            }
        }

        private static void ParseAndEmit(string filePath, BrowserProfile profile, IRecordWriter jl, ISourceWriter writer)
        {
            string text = File.ReadAllText(filePath);
            var root = JObject.Parse(text);
            var roots = root["roots"] as JObject;
            if (roots == null) return;

            foreach (var pair in roots)
            {
                if (!(pair.Value is JObject section)) continue;
                WalkNode(section, pair.Key, profile, jl, writer);
            }
        }

        private static void WalkNode(JObject node, string folderPath, BrowserProfile profile,
            IRecordWriter jl, ISourceWriter writer)
        {
            string type = (string)node["type"];
            if (string.Equals(type, "url", StringComparison.OrdinalIgnoreCase))
            {
                jl.Write(new BookmarkRecord
                {
                    Vendor = profile.Vendor,
                    Browser = profile.Browser,
                    ProfileName = profile.ProfileName,
                    Folder = folderPath,
                    Title = (string)node["name"],
                    Url = (string)node["url"],
                    DateAddedUtc = ChromiumTimeUtc((string)node["date_added"]),
                    DateModifiedUtc = ChromiumTimeUtc((string)node["date_modified"]),
                    GuidStr = (string)node["guid"]
                });
                writer.RecordItem();
                return;
            }

            // folder-type node: recurse into "children"
            string name = (string)node["name"];
            string childFolder = string.IsNullOrEmpty(name) ? folderPath : folderPath + "/" + name;
            if (node["children"] is JArray children)
            {
                foreach (var child in children)
                {
                    if (child is JObject childObj) WalkNode(childObj, childFolder, profile, jl, writer);
                }
            }
        }

        // Chromium stores timestamps as microseconds since 1601-01-01 UTC (Windows FILETIME / 10).
        internal static DateTimeOffset? ChromiumTimeUtc(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            if (!long.TryParse(raw, out long microsSince1601)) return null;
            if (microsSince1601 <= 0) return null;
            try
            {
                long ticks = microsSince1601 * 10L; // microseconds → 100-nanosecond ticks
                var epoch = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                return new DateTimeOffset(epoch.AddTicks(ticks), TimeSpan.Zero);
            }
            catch { return null; }
        }

        private sealed class BookmarkRecord
        {
            public string Vendor { get; set; }
            public string Browser { get; set; }
            public string ProfileName { get; set; }
            public string Folder { get; set; }
            public string Title { get; set; }
            public string Url { get; set; }
            public DateTimeOffset? DateAddedUtc { get; set; }
            public DateTimeOffset? DateModifiedUtc { get; set; }
            public string GuidStr { get; set; }
        }
    }
}
