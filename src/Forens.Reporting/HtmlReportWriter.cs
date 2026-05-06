using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;

namespace Forens.Reporting
{
    public static class HtmlReportWriter
    {
        private const string CssResource = "Forens.Reporting.Resources.report.css";
        private const string JsResource = "Forens.Reporting.Resources.report.js";

        public static string Render(ReportModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            string css = LoadResource(CssResource);
            string js = LoadResource(JsResource);

            var sb = new StringBuilder(64 * 1024);
            sb.Append("<!doctype html>\n");
            sb.Append("<html lang=\"en\"><head>\n");
            sb.Append("<meta charset=\"utf-8\">\n");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">\n");
            sb.Append("<title>Forens Report — ").Append(Esc(model.Run != null ? model.Run.Host?.Name : "")).Append("</title>\n");
            sb.Append("<style>\n").Append(css).Append("\n</style>\n");
            sb.Append("</head><body>\n");

            RenderHeader(sb, model);
            RenderControls(sb, model);
            RenderMain(sb, model);
            RenderFooter(sb, model);

            sb.Append("<script>\n").Append(js).Append("\n</script>\n");
            sb.Append("</body></html>\n");
            return sb.ToString();
        }

        public static void WriteToFile(ReportModel model, string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path required", nameof(path));
            string parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(path, Render(model), new UTF8Encoding(false));
        }

        private static void RenderHeader(StringBuilder sb, ReportModel m)
        {
            sb.Append("<header>\n");
            sb.Append("<h1>Forens Report</h1>\n");
            sb.Append("<div class=\"meta\">");
            if (m.Run != null)
            {
                sb.Append("Host: <b>").Append(Esc(m.Run.Host?.Name)).Append("</b> · ");
                sb.Append("OS: ").Append(Esc(m.Run.Host?.OsVersion)).Append(" · ");
                sb.Append("Started: ").Append(Esc(m.Run.StartedUtc.ToString("u"))).Append(" · ");
                sb.Append("Completed: ").Append(Esc(m.Run.CompletedUtc.ToString("u"))).Append(" · ");
                sb.Append("Run id: <code>").Append(Esc(m.Run.RunId)).Append("</code> · ");
                sb.Append("Status: <b>").Append(Esc(m.Run.Status)).Append("</b>");
                if (!string.IsNullOrEmpty(m.Run.Profile))
                    sb.Append(" · Profile: ").Append(Esc(m.Run.Profile));
                if (!string.IsNullOrEmpty(m.Run.CaseId))
                    sb.Append(" · Case: ").Append(Esc(m.Run.CaseId));
            }
            sb.Append("</div>\n</header>\n");
        }

        private static void RenderControls(StringBuilder sb, ReportModel m)
        {
            var categories = (m.Sections ?? new List<ReportSection>())
                .Select(s => s.Category ?? "Other")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToArray();

            sb.Append("<nav class=\"controls\">\n");
            sb.Append("<label for=\"filter-text\">Search</label>");
            sb.Append("<input id=\"filter-text\" type=\"text\" placeholder=\"id, name, status\">\n");

            sb.Append("<span class=\"kv\">Category:</span>");
            sb.Append("<button class=\"filter-btn active\" data-category=\"all\">all</button>");
            foreach (var c in categories)
                sb.Append("<button class=\"filter-btn\" data-category=\"").Append(Esc(c)).Append("\">").Append(Esc(c)).Append("</button>");

            sb.Append("<span class=\"kv\" style=\"margin-left:8px\">Status:</span>");
            sb.Append("<button class=\"filter-btn active\" data-status=\"all\">all</button>");
            foreach (var s in new[] { "Succeeded", "Partial", "Skipped", "Failed" })
                sb.Append("<button class=\"filter-btn\" data-status=\"").Append(Esc(s)).Append("\">").Append(Esc(s)).Append("</button>");

            sb.Append("\n</nav>\n");
        }

        private static void RenderMain(StringBuilder sb, ReportModel m)
        {
            sb.Append("<main>\n");
            var sections = m.Sections ?? new List<ReportSection>();
            var byCat = sections
                .GroupBy(s => s.Category ?? "Other", StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal);

            foreach (var grp in byCat)
            {
                sb.Append("<section class=\"category\">\n");
                sb.Append("<h2>").Append(Esc(grp.Key)).Append("</h2>\n");
                foreach (var s in grp.OrderBy(x => x.SourceId, StringComparer.Ordinal))
                {
                    RenderSource(sb, s);
                }
                sb.Append("</section>\n");
            }
            sb.Append("</main>\n");
        }

        private static void RenderSource(StringBuilder sb, ReportSection s)
        {
            string statusClass = s.Status == "Succeeded" ? "ok"
                : s.Status == "Partial" ? "partial"
                : s.Status == "Skipped" ? "skipped"
                : "failed";

            sb.Append("<article class=\"source\" data-category=\"")
              .Append(Esc(s.Category)).Append("\" data-status=\"")
              .Append(Esc(s.Status)).Append("\">\n");
            sb.Append("<div class=\"header\">");
            sb.Append("<span class=\"badge ").Append(statusClass).Append("\">").Append(Esc(s.Status)).Append("</span>");
            sb.Append("<span class=\"name\">").Append(Esc(s.DisplayName)).Append("</span>");
            sb.Append("<span class=\"id\">").Append(Esc(s.SourceId)).Append("</span>");
            sb.Append("</div>\n");

            sb.Append("<div class=\"body\">");

            if (s.Summary != null && s.Summary.Count > 0)
            {
                sb.Append("<div class=\"kv\">");
                bool first = true;
                foreach (var kv in s.Summary)
                {
                    if (!first) sb.Append(" · ");
                    first = false;
                    sb.Append(Esc(kv.Key)).Append("=<code>").Append(Esc(kv.Value?.ToString() ?? "")).Append("</code>");
                }
                sb.Append("</div>");
            }

            if (!string.IsNullOrEmpty(s.StatusReason))
            {
                sb.Append("<div class=\"reason\">").Append(Esc(s.StatusReason)).Append("</div>");
            }

            if (s.RawOutput != null && s.RawOutput.Count > 0)
            {
                sb.Append("<ul class=\"outputs\">");
                foreach (var f in s.RawOutput)
                {
                    sb.Append("<li><code>").Append(Esc(f.Path)).Append("</code> ");
                    sb.Append("<span class=\"kv\">").Append(f.ByteCount.ToString("N0")).Append(" bytes · sha256 <code>");
                    sb.Append(Esc(string.IsNullOrEmpty(f.Sha256) ? "" : f.Sha256.Substring(0, Math.Min(16, f.Sha256.Length))));
                    sb.Append("…</code></span></li>");
                }
                sb.Append("</ul>");
            }

            sb.Append("</div>\n");
            sb.Append("</article>\n");
        }

        private static void RenderFooter(StringBuilder sb, ReportModel m)
        {
            sb.Append("<footer>");
            sb.Append("Generated by Forens. Schema ");
            if (m.Schema != null) sb.Append(Esc(m.Schema.Name)).Append(" v").Append(Esc(m.Schema.Version));
            sb.Append(". Self-contained — no external network resources at view time.");
            sb.Append("</footer>\n");
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return WebUtility.HtmlEncode(s);
        }

        private static string LoadResource(string name)
        {
            var asm = typeof(HtmlReportWriter).Assembly;
            using (var stream = asm.GetManifestResourceStream(name))
            {
                if (stream == null)
                    throw new InvalidOperationException("Embedded resource not found: " + name);
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
