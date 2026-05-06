using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Forens.Reporting
{
    public static class JsonReportWriter
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateFormatString = "yyyy-MM-ddTHH:mm:ss.fffK",
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };

        public static string Serialize(ReportModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            return JsonConvert.SerializeObject(model, JsonSettings);
        }

        public static void WriteToFile(ReportModel model, string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path required", nameof(path));
            string parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(path, Serialize(model), new UTF8Encoding(false));
        }
    }
}
