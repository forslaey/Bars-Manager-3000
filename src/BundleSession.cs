using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
namespace ServiceTool
{
    internal static class BundleSession
    {
        static readonly object gate=new object();
        static string PathIn(string root,string name){return YandexBundleSource.LocalPath(root,"/.bars-download/"+name);}
        internal static void Log(string root,string text)
        {
            lock(gate)
            {
                try{string path=PathIn(root,"transfer.log");Directory.CreateDirectory(Path.GetDirectoryName(path));
                    // Bound diagnostics; do not include temporary download URLs.
                    if(File.Exists(path)&&new FileInfo(path).Length>2*1024*1024){string previous=PathIn(root,"transfer.previous.log");if(File.Exists(previous))File.Delete(previous);File.Move(path,previous);}
                    File.AppendAllText(path,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" "+text.Replace("\r"," ").Replace("\n"," ")+Environment.NewLine);
                }catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
        internal static void Save(string root,IEnumerable<BundleRemoteFile> files)
        {
            string path=PathIn(root,"queue.xml");Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=PathIn(root,"queue-"+Guid.NewGuid().ToString("N")+".tmp");
            var doc=new XmlDocument();var node=doc.CreateElement("queue");node.SetAttribute("source",YandexBundleSource.PublicLink);doc.AppendChild(node);
            foreach(var file in files){var item=doc.CreateElement("file");item.SetAttribute("path",file.Path);item.SetAttribute("sha256",file.Sha256);node.AppendChild(item);}
            try{doc.Save(temp);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
        internal static HashSet<string> Load(string root)
        {
            string path=PathIn(root,"queue.xml");if(!File.Exists(path))return null;
            if(new FileInfo(path).Length>4*1024*1024)throw new InvalidDataException("Слишком большая сохранённая очередь");
            try
            {
                var settings=new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null};
                using(var reader=XmlReader.Create(path,settings))
                {
                    var doc=new XmlDocument {XmlResolver=null};doc.Load(reader);var node=doc.DocumentElement;
                    if(node==null||node.Name!="queue"||node.GetAttribute("source")!=YandexBundleSource.PublicLink)return null;
                    return new HashSet<string>(node.ChildNodes.OfType<XmlElement>().Where(n=>n.Name=="file").Select(n=>n.GetAttribute("path")+"|"+n.GetAttribute("sha256")),StringComparer.OrdinalIgnoreCase);
                }
            }catch(XmlException){return null;}
        }
    }
}
