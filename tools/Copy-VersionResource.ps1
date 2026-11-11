param([string]$Source,[string]$Target)
if (-not ('BarsVersionResource' -as [type])) {
Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class BarsVersionResource {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibraryEx(string p,IntPtr f,uint flags);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool FreeLibrary(IntPtr m);
 [DllImport("kernel32.dll",EntryPoint="FindResourceW",SetLastError=true)] static extern IntPtr FindResource(IntPtr m,IntPtr name,IntPtr type);
 [DllImport("kernel32.dll",SetLastError=true)] static extern uint SizeofResource(IntPtr m,IntPtr r);
 [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr LoadResource(IntPtr m,IntPtr r);
 [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr r);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr BeginUpdateResource(string p,bool remove);
 [DllImport("kernel32.dll",EntryPoint="UpdateResourceW",SetLastError=true)] static extern bool UpdateResource(IntPtr h,IntPtr type,IntPtr name,ushort lang,byte[] data,uint size);
 [DllImport("kernel32.dll",EntryPoint="EndUpdateResourceW",SetLastError=true)] static extern bool EndUpdateResource(IntPtr h,bool discard);
 public static void Copy(string source,string target) {
  IntPtr module=LoadLibraryEx(source,IntPtr.Zero,2), name=(IntPtr)1, type=(IntPtr)16;
  if(module==IntPtr.Zero) throw new Win32Exception();
  try {
   IntPtr resource=FindResource(module,name,type);
   if(resource==IntPtr.Zero) throw new Win32Exception();
   uint size=SizeofResource(module,resource);
   IntPtr data=LockResource(LoadResource(module,resource));
   if(size==0 || data==IntPtr.Zero) throw new Win32Exception();
   byte[] bytes=new byte[size];
   Marshal.Copy(data,bytes,0,bytes.Length);
   IntPtr update=BeginUpdateResource(target,false);
   if(update==IntPtr.Zero) throw new Win32Exception();
   if(!UpdateResource(update,type,name,1033,bytes,size)){
    int error=Marshal.GetLastWin32Error(); EndUpdateResource(update,true); throw new Win32Exception(error);
   }
   if(!EndUpdateResource(update,false)) throw new Win32Exception();
  } finally { FreeLibrary(module); }
 }
}
"@
}
[BarsVersionResource]::Copy($Source,$Target)