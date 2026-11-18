using System;
using System.IO;
using System.Text;
using System.Xml;

namespace ServiceTool
{
    internal static class HttpServiceConfiguration
    {
        internal static bool Prepare(string executable, bool validateOnly = false)
        {
            if(!String.Equals(Path.GetFileName(executable),"HttpService.exe",StringComparison.OrdinalIgnoreCase))
                return false;
            if(!File.Exists(executable))throw new FileNotFoundException("HttpService.exe не найден.",executable);
            string config=Path.GetFullPath(executable)+".config";
            if(!File.Exists(config))return false;
            byte[] original;
            using(var input=new FileStream(config,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                if(input.Length>4*1024*1024)throw new IOException("Конфиг HttpService слишком большой.");
                original=new byte[(int)input.Length];
                int offset=0;
                while(offset<original.Length)
                {
                    int read=input.Read(original,offset,original.Length-offset);
                    if(read==0)throw new EndOfStreamException("Не удалось прочитать конфиг HttpService.");
                    offset+=read;
                }
            }
            var document=new XmlDocument {PreserveWhitespace=true,XmlResolver=null};
            var settings=new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null};
            using(var input=new MemoryStream(original,false))
            using(var reader=XmlReader.Create(input,settings))document.Load(reader);
            var values=document.SelectNodes("/configuration/applicationSettings/HttpService.Properties.Settings/setting[@name='ExtensionLibrary']/value");
            if(values.Count==0)return false;
            if(values.Count!=1)throw new IOException("В конфиге HttpService повторяется настройка ExtensionLibrary.");
            var value=values[0];
            string configured=value.InnerText.Trim();
            if(configured.Length==0)return false;
            string directory=Path.GetDirectoryName(Path.GetFullPath(executable));
            if(Path.IsPathRooted(configured)&&!IsAbsolute(configured))
                throw new IOException("В ExtensionLibrary указан неоднозначный путь. Укажи полный путь к DLL.");
            string resolved=Path.GetFullPath(Path.Combine(directory,configured));
            if(!File.Exists(resolved))
                throw new FileNotFoundException("Расширение HttpService не найдено: "+resolved+". Проверь папку Extension.",resolved);
            if(IsAbsolute(configured))return false;
            if(validateOnly)return true;

            // Preserve the XML declaration, whitespace, comments and all other settings.
            Encoding encoding=new UTF8Encoding(false);
            var declaration=document.FirstChild as XmlDeclaration;
            if(declaration!=null&&!String.IsNullOrEmpty(declaration.Encoding))
                encoding=Encoding.GetEncoding(declaration.Encoding);
            using(var reader=new StreamReader(new MemoryStream(original,false),encoding,true))
            {
                reader.ReadToEnd();
                encoding=reader.CurrentEncoding;
            }
            bool utf8Bom=original.Length>=3&&original[0]==239&&original[1]==187&&original[2]==191;
            if(encoding.CodePage==65001)encoding=new UTF8Encoding(utf8Bom);
            value.InnerText=resolved;
            byte[] replacement;
            using(var output=new MemoryStream())
            {
                using(var writer=XmlWriter.Create(output,new XmlWriterSettings {
                    Encoding=encoding,Indent=false,NewLineHandling=NewLineHandling.None,
                    OmitXmlDeclaration=declaration==null,CloseOutput=false}))
                    document.Save(writer);
                replacement=output.ToArray();
            }
            string staging=config+"."+Guid.NewGuid().ToString("N")+".tmp";
            string backup=config+".before-extension-path-"+Guid.NewGuid().ToString("N")+".bak";
            try
            {
                using(var output=new FileStream(staging,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {output.Write(replacement,0,replacement.Length);output.Flush(true);}
                // Detect another editor before publishing and keep the original with its file ACL.
                using(var input=new FileStream(config,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
                {
                    if(input.Length!=original.Length)throw new IOException("Конфиг HttpService изменился. Повтори действие.");
                    for(int i=0;i<original.Length;i++)
                        if(input.ReadByte()!=original[i])throw new IOException("Конфиг HttpService изменился. Повтори действие.");
                    File.Replace(staging,config,backup);
                }
                return true;
            }
            finally
            {
                try{if(File.Exists(staging))File.Delete(staging);}
                catch(IOException){}
                catch(UnauthorizedAccessException){}
            }
        }

        internal static bool IsAbsolute(string path)
        {
            return path.StartsWith(@"\\",StringComparison.Ordinal)||
                (path.Length>=3&&Char.IsLetter(path[0])&&path[1]==':'&&(path[2]=='\\'||path[2]=='/'));
        }
    }
}
