using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ServiceTool
{
    internal static class PausedLibraryQueue
    {
        internal static string FilePath { get { return ApplicationStorage.FilePath("paused-library-selection.xml"); } }
        internal static void Save(IEnumerable<string> ids, LibraryOperation operation)
        {
            SaveAt(FilePath, ids, operation);
        }
        internal static void SaveAt(string path, IEnumerable<string> ids, LibraryOperation operation)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var document = new System.Xml.XmlDocument();
            var root = document.CreateElement("selection"); root.SetAttribute("operation", operation.ToString()); document.AppendChild(root);
            foreach (string id in ids.Distinct()) { var element = document.CreateElement("package"); element.SetAttribute("id", id); root.AppendChild(element); }
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { document.Save(temp); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        internal static HashSet<string> Load()
        {
            return LoadAt(FilePath);
        }
        internal static HashSet<string> LoadAt(string path)
        {
            var result = new HashSet<string>(); if (!File.Exists(path)) return result;
            using (var reader = System.Xml.XmlReader.Create(path, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 }))
                while (reader.Read()) if (reader.NodeType == System.Xml.XmlNodeType.Element && reader.Name == "package") { string id = reader.GetAttribute("id"); if (!String.IsNullOrEmpty(id)) result.Add(id); }
            return result;
        }
    }
}
