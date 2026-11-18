using System;
using System.Collections.Generic;

namespace ServiceTool
{
    internal static class ServiceRemovalRecoveryTests
    {
        private static int checks;
        private static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
        private static void Refuses(Action action,string message)
        {checks++;try{action();}catch(InvalidOperationException){return;}throw new Exception(message);}
        private static ServiceEntry Entry(string name,string path)
        {return new ServiceEntry{Name=name,BinaryPath=path};}
        private static ServiceSnapshot Snapshot(params ServiceEntry[] entries)
        {return new ServiceSnapshot{Complete=true,Entries=new List<ServiceEntry>(entries)};}
        private static int Main()
        {
            try
            {
                string exe=@"C:\Bars folder\HttpService.exe";
                var custom=Entry("Manual Http name","\""+exe+"\" r");
                var unrelated=Entry("HttpService",@"C:\Other\HttpService.exe");
                var snapshot=Snapshot(unrelated,custom);
                Check(ServiceRemovalRecovery.Resolve(snapshot,exe,new[]{custom.Name},null)==custom,"Manual name is resolved by exact executable, not familiar name");
                Check(ServiceRemovalRecovery.Resolve(snapshot,exe,new[]{"MANUAL HTTP NAME"},null)==custom,"Name comparison is case insensitive");
                Check(ServiceRemovalRecovery.Resolve(Snapshot(unrelated),exe,new[]{custom.Name},null)==null,"Already removed target requires no deletion");
                Refuses(()=>ServiceRemovalRecovery.Resolve(snapshot,exe,new[]{"Other"},null),"New registration must not be deleted");
                snapshot.Complete=false;
                Refuses(()=>ServiceRemovalRecovery.Resolve(snapshot,exe,new[]{custom.Name},null),"Incomplete catalog must block deletion");
                Check(!ServiceRemovalRecovery.Removed(snapshot,exe,custom.Name),"Incomplete catalog cannot prove success");
                var second=Entry("Second manual name",exe);
                snapshot=Snapshot(custom,second,unrelated);
                Refuses(()=>ServiceRemovalRecovery.Resolve(snapshot,exe,new[]{custom.Name,second.Name},null),"Ambiguous registrations need explicit selection");
                Refuses(()=>ServiceRemovalRecovery.Resolve(snapshot,exe,new[]{custom.Name,second.Name},unrelated.Name),"Unrelated selected row is refused");
                Check(ServiceRemovalRecovery.Resolve(snapshot,exe,new[]{custom.Name,second.Name},second.Name)==second,"Explicit matching selection is honored");
                Check(!ServiceRemovalRecovery.Removed(snapshot,exe,second.Name),"Remaining target is not success");
                Check(ServiceRemovalRecovery.Removed(Snapshot(custom,unrelated),exe,second.Name),"Target deletion succeeds with sibling preserved");
                Check(!ServiceRemovalRecovery.Removed(Snapshot(custom),exe,null),"Ordinary uninstall must remove all matching registrations");
                Check(ServiceRemovalRecovery.Removed(Snapshot(unrelated),exe,null),"Unrelated service is preserved");
                ServiceManagement.VerifyRemovalExecutable(custom.BinaryPath,exe);
                checks++;
                Refuses(()=>ServiceManagement.VerifyRemovalExecutable(unrelated.BinaryPath,exe),"Retargeted registration is refused before SCM mutation");
                string encoded=ServiceManagement.EncodeExpectedExecutable(exe);
                ServiceManagement.ManagementRequest request;
                string name=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(custom.Name));
                Check(ServiceManagement.TryParseRequest(new[]{"--manage-service","delete-matching",name,encoded},out request),"Guarded helper protocol round trips paths with spaces");
                foreach(string invalid in new[]{"none","",Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("relative.exe")),"/w==",encoded+" "})
                    Check(!ServiceManagement.TryParseRequest(new[]{"--manage-service","delete-matching",name,invalid},out request),"Invalid expected path must be rejected");
                Console.WriteLine("PASS: "+checks+" service removal recovery checks.");
                return 0;
            }
            catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
    }
}
