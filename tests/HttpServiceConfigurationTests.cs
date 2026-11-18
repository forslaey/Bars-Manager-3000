using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using ServiceTool;

internal static class HttpServiceConfigurationTests
{
    private static int checks;
    private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    private static string Config(string path)
    {
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<configuration>\r\n<!--keep comment-->\r\n"+
            "<applicationSettings><HttpService.Properties.Settings>"+
            "<setting name=\"ExtensionLibrary\" serializeAs=\"String\"><value>"+System.Security.SecurityElement.Escape(path)+"</value></setting>"+
            "<setting name=\"Port\"><value>54321</value></setting>"+
            "</HttpService.Properties.Settings></applicationSettings>"+
            "<connectionStrings><add name=\"test\" connectionString=\"Inert &amp; content\"/></connectionStrings></configuration>";
    }
    public static int Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"bars-http-config-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string folder=Directory.CreateDirectory(Path.Combine(root,"Папка & пробел")).FullName;
            string exe=Path.Combine(folder,"HttpService.exe"),file=exe+".config";
            File.WriteAllText(exe,"inert fixture");
            string dll=Path.Combine(Directory.CreateDirectory(Path.Combine(folder,"Extension")).FullName,"Custom.dll");
            File.WriteAllText(dll,"inert extension");
            var utf8=new UTF8Encoding(false);
            string original=Config(@"Extension\Custom.dll");
            File.WriteAllText(file,original,utf8);
            Check(HttpServiceConfiguration.Prepare(exe,true),"Read-only validation detects relative path");
            Check(File.ReadAllText(file)==original&&!Directory.GetFiles(folder,"*.bak").Any(),"Validation did not write files");
            Check(ServiceManagement.RunHelper(new[]{"--manage-service","prepare-http",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(exe)),"none"})==0,"Native-install preparation helper succeeds without SCM");
            var xml=new XmlDocument();xml.Load(file);
            Check(xml.SelectSingleNode("//setting[@name='ExtensionLibrary']/value").InnerText==dll,"Resolved from EXE directory");
            Check(xml.SelectSingleNode("//setting[@name='Port']/value").InnerText=="54321","Port preserved");
            Check(xml.SelectSingleNode("//connectionStrings/add").Attributes["connectionString"].Value=="Inert & content","Connection settings preserved");
            Check(File.ReadAllText(file).Contains("<!--keep comment-->"),"Comments preserved");
            var backups=Directory.GetFiles(folder,"*.bak");
            Check(backups.Length==1&&File.ReadAllText(backups[0])==original,"Exact original backed up");
            byte[] fixedBytes=File.ReadAllBytes(file);
            Check(!HttpServiceConfiguration.Prepare(exe),"Repeated preparation is a no-op");
            Check(fixedBytes.SequenceEqual(File.ReadAllBytes(file))&&Directory.GetFiles(folder,"*.bak").Length==1,"No repeat churn or extra backups");
            Check(!(fixedBytes[0]==239&&fixedBytes[1]==187&&fixedBytes[2]==191),"UTF8 without BOM preserved");
            string absolute=Config(dll);
            File.WriteAllText(file,absolute,utf8);
            Check(!HttpServiceConfiguration.Prepare(exe)&&File.ReadAllText(file)==absolute,"Absolute path unchanged");
            File.WriteAllText(file,Config(@"Extension\Missing.dll"),utf8);
            string missing=File.ReadAllText(file);bool rejected=false;
            try{HttpServiceConfiguration.Prepare(exe);}catch(FileNotFoundException){rejected=true;}
            Check(rejected&&File.ReadAllText(file)==missing&&Directory.GetFiles(folder,"*.bak").Length==1,"Missing DLL preserves config");
            File.WriteAllText(file,Config(@"Extension\Custom.dll"),new UTF8Encoding(true));
            Check(HttpServiceConfiguration.Prepare(exe),"UTF8 BOM prepared");
            Check(File.ReadAllBytes(file).Take(3).SequenceEqual(new byte[]{239,187,191}),"UTF8 BOM preserved");
            File.WriteAllText(file,Config(@"Extension\Custom.dll").Replace("utf-8","utf-16"),Encoding.Unicode);
            Check(HttpServiceConfiguration.Prepare(exe),"UTF16 prepared");
            Check(File.ReadAllBytes(file).Take(2).SequenceEqual(new byte[]{255,254}),"UTF16 preserved");
            File.WriteAllText(file,"<configuration/>",utf8);
            Check(!HttpServiceConfiguration.Prepare(exe),"No setting unchanged");
            File.WriteAllText(file,Config(""),utf8);
            Check(!HttpServiceConfiguration.Prepare(exe),"Empty extension unchanged");
            Check(!HttpServiceConfiguration.Prepare(Path.Combine(folder,"OtherService.exe")),"Other services ignored");
            File.WriteAllText(file,"<configuration><",utf8);bool malformed=false;
            try{HttpServiceConfiguration.Prepare(exe);}catch(XmlException){malformed=true;}
            Check(malformed&&File.ReadAllText(file)=="<configuration><","Malformed XML unchanged");
            Check(!Directory.GetFiles(folder,"*.tmp").Any(),"No temporary files");
            Check(ServiceManagement.RunHelper(new[]{"--manage-service","prepare-http",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(@"C:HttpService.exe")),"none"})==87,"Drive-relative target rejected");
            Console.WriteLine("PASS: "+checks+" HttpService config/helper/backup/encoding checks. Inert fixtures only.");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{Directory.Delete(root,true);}
    }
}
