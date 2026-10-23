using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
namespace ServiceTool
{
    internal static class FolderAuditTests
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        static string Query(string root)
        {
            string fixture="$auditDrive=[pscustomobject]@{IsReady=$true;DriveType=[IO.DriveType]::Fixed;DriveFormat='NTFS';RootDirectory=[pscustomobject]@{FullName='"+root.Replace("'","''")+"'}};\n";
            string script=fixture+BarsFolderPermissions.Script(false).Replace("foreach($drive in [IO.DriveInfo]::GetDrives())","foreach($drive in @($auditDrive))");
            return WindowsComponents.Run(script,delegate{}).Message;
        }
        public static int Main()
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"folder-audit-"+Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                foreach(string name in new[]{"DataKrat","Bars"})
                {
                    string path=Path.Combine(root,name);Directory.CreateDirectory(path);var acl=Directory.GetAccessControl(path);
                    foreach(string sid in new[]{"S-1-5-32-545","S-1-5-32-544","S-1-5-18"})acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
                    Directory.SetAccessControl(path,acl);Directory.CreateDirectory(Path.Combine(path,"nested"));File.WriteAllText(Path.Combine(path,"nested","file.txt"),"fixture");
                }
                var full=WindowsComponents.Parse(Query(root));Check(full["Count"]=="2"&&full["Errors"]=="0"&&full["Restricted"]=="0","Root and inherited children all verified");Check(Int32.Parse(full["Checked"])>=6,"Entire fixture tree audited");
                string child=Path.Combine(root,"DataKrat","nested","file.txt");var restricted=new FileSecurity();restricted.SetAccessRuleProtection(true,false);
                restricted.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,FileSystemRights.FullControl,AccessControlType.Allow));File.SetAccessControl(child,restricted);
                var partial=WindowsComponents.Parse(Query(root));Check(Int32.Parse(partial["Restricted"])>=1,"Restricted nested file detected even when roots have full access");
                Console.WriteLine("FolderAuditTests: 3 passed; PowerShell 5.1 read-only audit of temporary ACL fixtures only.");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
            finally{if(Path.GetFileName(root).StartsWith("folder-audit-",StringComparison.Ordinal)&&Directory.Exists(root))Directory.Delete(root,true);}
        }
    }
}
