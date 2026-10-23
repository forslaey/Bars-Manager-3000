using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
namespace ServiceTool
{
    internal static class AssociationProfileTests
    {
        static int count;
        static void Check(bool value,string message){if(!value)throw new Exception(message);count++;}
        static int Main()
        {
            string fixture=@"Software\BARS\Tests\Assoc-"+Guid.NewGuid().ToString("N");
            try
            {
                using(var root=Registry.CurrentUser.CreateSubKey(fixture))
                {
                    using(var apps=root.CreateSubKey(@"Software\RegisteredApplications"))apps.SetValue("Google Chrome",@"Software\FixtureChrome\Capabilities");
                    foreach(string name in new[]{".svg",".html",".pdf","http","https"})
                    {
                        string prog=name==".pdf"?"ChromePDF":"ChromeHTML";
                        using(var map=root.CreateSubKey(@"Software\FixtureChrome\Capabilities\"+(name.StartsWith(".")?"FileAssociations":"URLAssociations")))map.SetValue(name,prog);
                        using(var command=root.CreateSubKey(@"Software\Classes\"+prog+@"\shell\open\command"))command.SetValue("","\"C:\\Fixture\\chrome.exe\" --single-argument \"%1\"");
                    }
                    var native=SoftwareDefaults.ChromeMappings(root);Check(native.Count==5,"All registered types and protocols");
                    Check(native[".svg"]=="ChromeHTML"&&native[".pdf"]=="ChromePDF","Distinct native Chrome ProgIDs preserved");
                    string original="<DefaultAssociations><Association Identifier=\".wav\" ProgId=\"Existing.Player\" ApplicationName=\"Player\"/><Association Identifier=\".svg\" ProgId=\"Old.Viewer\" ApplicationName=\"Viewer\"/></DefaultAssociations>";
                    var full=SoftwareDefaults.Merge(original,new[]{"chrome","notepad","7zip"},native);
                    Check(full.SelectSingleNode("/DefaultAssociations/Association[@Identifier='.wav']").Attributes["ProgId"].Value=="Existing.Player","Image profile keeps unrelated associations");
                    Check(full.SelectSingleNode("/DefaultAssociations/Association[@Identifier='.svg']").Attributes["ProgId"].Value=="ChromeHTML","SVG uses native Chrome registration");
                    var scoped=SoftwareDefaults.Merge("<DefaultAssociations/>",new[]{"chrome"},native);
                    Check(scoped.DocumentElement.ChildNodes.Count==5&&scoped.SelectSingleNode("/DefaultAssociations/Association[@Identifier='.wav']")==null,"Sign-in profile only includes requested types");
                    Check(SoftwareDefaults.Merge(scoped.OuterXml,new[]{"chrome"},native).OuterXml==scoped.OuterXml,"Repeated application idempotent");
                    SoftwareDefaults.Register(root,"chrome",@"C:\Fixture\chrome.exe");using(var custom=root.OpenSubKey(@"Software\Classes\BARS.Chrome.Html"))Check(custom==null,"No synthetic Chrome HTML/URL handler");
                    var removed=SoftwareDefaults.RemoveApp(full.OuterXml,"chrome");Check(removed.SelectSingleNode("/DefaultAssociations/Association[@Identifier='.wav']")!=null,"Uninstall preserves unrelated image associations");Check(removed.SelectSingleNode("/DefaultAssociations/Association[@Identifier='.svg']")==null,"Uninstall removes owned native Chrome profile entries");
                    var legacy=SoftwareDefaults.Merge(original,new[]{"notepad","7zip"},native);var ids=SoftwareDefaults.ManagedIds(legacy.OuterXml,"chrome\r\nunknown\r\n");Check(ids.SequenceEqual(new[]{"chrome","notepad","7zip"}),"Legacy metadata migrated, invalid IDs excluded");
                }
                Console.WriteLine("AssociationProfileTests: "+count+" passed; XML and isolated HKCU registry fixture only.");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
            finally{Registry.CurrentUser.DeleteSubKeyTree(fixture,false);}
        }
    }
}
