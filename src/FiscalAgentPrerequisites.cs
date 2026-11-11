using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace ServiceTool
{
    internal static class FiscalAgentPrerequisites
    {
        internal static bool AppliesTo(string executable)
        {
            return String.Equals(Path.GetFileName(executable), "DK.Fiscalization.Agent.Atol.exe",
                StringComparison.OrdinalIgnoreCase);
        }

        internal static string Warning(string executable)
        {
            string detected = ExecutableArchitecture.Caption(executable);
            string architecture = detected == "32-разрядная" ? "x86" : detected == "64-разрядная" ? "x64" : null;
            string driver = architecture == null
                ? "Проверь, что разрядность драйвера ККТ совпадает с разрядностью агента."
                : "Выбран агент фискализации с разрядностью " + architecture + ". Проверь, что установлен драйвер ККТ " + architecture + ".";
            string runtime = RuntimeRequirements(executable);
            string requirements = String.IsNullOrEmpty(runtime)
                ? "Требования .NET уточни в инструкции к агенту."
                : "Требования агента: " + runtime + ". Проверь, что установлены совместимые версии" +
                  (architecture == null ? "." : " для " + architecture + ".");
            return driver + "\r\n\r\n" + requirements + "\r\n\r\nПродолжить установку?";
        }

        private static string RuntimeRequirements(string executable)
        {
            try
            {
                string path = Path.ChangeExtension(executable, ".runtimeconfig.json");
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > 65536) return "";
                var config = new JavaScriptSerializer { MaxJsonLength = 65536 }.DeserializeObject(File.ReadAllText(path))
                    as IDictionary<string, object>;
                var options = Value(config, "runtimeOptions") as IDictionary<string, object>;
                var requirements = new List<string>();
                AddRequirement(Value(options, "framework"), requirements);
                var frameworks = Value(options, "frameworks") as IList;
                if (frameworks != null)
                    foreach (object entry in frameworks) AddRequirement(entry, requirements);
                return String.Join(", ", requirements);
            }
            catch (Exception ex)
            {
                if (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException ||
                    ex is ArgumentException || ex is InvalidOperationException) return "";
                throw;
            }
        }

        private static object Value(IDictionary<string, object> values, string name)
        {
            object value;
            return values != null && values.TryGetValue(name, out value) ? value : null;
        }

        private static void AddRequirement(object entry, List<string> requirements)
        {
            var fields = entry as IDictionary<string, object>;
            string name = Value(fields, "name") as string;
            string version = Value(fields, "version") as string;
            if (String.IsNullOrEmpty(version) || version.Length > 40 || !System.Text.RegularExpressions.Regex.IsMatch(version, @"\A\d+\.\d+(?:\.\d+)?(?:-[a-zA-Z0-9.-]+)?\z")) return;
            string label = name == "Microsoft.NETCore.App" ? ".NET Runtime" :
                name == "Microsoft.AspNetCore.App" ? "ASP.NET Core Runtime" :
                name == "Microsoft.WindowsDesktop.App" ? ".NET Desktop Runtime" : null;
            if (label != null && !requirements.Contains(label + " " + version)) requirements.Add(label + " " + version);
        }
    }
}