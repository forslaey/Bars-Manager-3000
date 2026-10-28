using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;

namespace ServiceTool
{
    // Receipts only authorize replacing unchanged files previously published by
    // this manager. They are not a signature or proof of publisher identity.
    internal sealed class BundleReceipts
    {
        readonly string root;
        static readonly object gate=new object();
        internal BundleReceipts(string root){this.root=root;}
        string Receipt(BundleRemoteFile file)
        {
            string key;
            using(var hash=System.Security.Cryptography.SHA256.Create())key=BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(file.Path.ToLowerInvariant()))).Replace("-","");
            return YandexBundleSource.LocalPath(root,"/.bars-download/receipts/"+key+".xml");
        }
        internal bool CanReplace(BundleRemoteFile file,string currentHash)
        {
            // Configuration and arbitrary documents are always conservative.
            string ext=Path.GetExtension(file.Path);
            if(!String.Equals(ext,".exe",StringComparison.OrdinalIgnoreCase)&&!String.Equals(ext,".msi",StringComparison.OrdinalIgnoreCase))return false;
            // Utilities can modify their own installation; preserve the whole tree.
            if(file.Group=="Утилиты")return false;
            lock(gate)
            {
                string path=Receipt(file);if(!File.Exists(path))return false;
                try
                {
                    if(new FileInfo(path).Length>4096)return false;
                    var settings=new System.Xml.XmlReaderSettings {DtdProcessing=System.Xml.DtdProcessing.Prohibit,XmlResolver=null};
                    using(var reader=System.Xml.XmlReader.Create(path,settings))
                    {
                        var doc=new XmlDocument {XmlResolver=null};doc.Load(reader);var node=doc.DocumentElement;
                        return node!=null&&node.Name=="file"&&node.GetAttribute("path")==file.Path&&String.Equals(node.GetAttribute("sha256"),currentHash,StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch(System.Xml.XmlException){return false;}
                catch(IOException){return false;}
            }
        }
        internal void Record(BundleRemoteFile file)
        {
            lock(gate)
            {
                string path=Receipt(file);Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                var doc=new XmlDocument();var node=doc.CreateElement("file");node.SetAttribute("path",file.Path);node.SetAttribute("sha256",file.Sha256);doc.AppendChild(node);doc.Save(temp);
                if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
            }
        }
    }
}
