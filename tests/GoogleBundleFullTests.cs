using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace ServiceTool
{
 internal static class GoogleBundleFullTests
 {
  [STAThread]static int Main(){string proof=AppDomain.CurrentDomain.BaseDirectory;try{
   var watch=System.Diagnostics.Stopwatch.StartNew();var source=new BundleCloudSource();var files=source.Scan(BundleCloud.Google,CancellationToken.None,s=>File.AppendAllText(Path.Combine(proof,"google-full.log"),s+Environment.NewLine));
   File.WriteAllText(Path.Combine(proof,"google-full-status.txt"),"Catalog="+files.Count+" seconds="+watch.Elapsed.TotalSeconds);
   string root=Path.Combine(proof,"google-full-download");Directory.CreateDirectory(root);var errors=new System.Collections.Concurrent.ConcurrentBag<string>();long verified=0;int done=0;
   Parallel.ForEach(files,new ParallelOptions{MaxDegreeOfParallelism=3},file=>{try{
    if(!BundlePublication.Matches(root,file,CancellationToken.None))source.Transfer(BundleCloud.Google).Download(file,root,null,x=>{},CancellationToken.None);
    if(!BundlePublication.Matches(root,file,CancellationToken.None))throw new Exception("Final hash mismatch");Interlocked.Add(ref verified,file.Size);int completed=Interlocked.Increment(ref done);lock(errors)File.WriteAllText(Path.Combine(proof,"google-full-status.txt"),"Verified="+completed+"/"+files.Count+" bytes="+Interlocked.Read(ref verified)+" seconds="+watch.Elapsed.TotalSeconds);
   }catch(Exception ex){errors.Add(file.Path+": "+ex.Message);lock(errors)File.AppendAllText(Path.Combine(proof,"google-full.log"),file.Path+": "+ex+Environment.NewLine);}});
   File.WriteAllLines(Path.Combine(proof,"google-full-result.txt"),new[]{"Files="+files.Count,"Verified="+done,"Bytes="+verified,"Seconds="+watch.Elapsed.TotalSeconds,"Errors="+errors.Count,"Google only; no downloaded content executed."}.Concat(errors));return errors.IsEmpty?0:1;
  }catch(Exception ex){File.WriteAllText(Path.Combine(proof,"google-full-result.txt"),ex.ToString());return 1;}}
 }
}
