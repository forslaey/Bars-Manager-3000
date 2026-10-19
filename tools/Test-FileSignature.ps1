[CmdletBinding()]
param([Parameter(Mandatory = $true)] [string] $FilePath, [string] $CertificateThumbprint)
$ErrorActionPreference = 'Stop'
if (-not ('BarsSigning.NativeTrust' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace BarsSigning {
 public static class NativeTrust {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct FileInfo {
   public uint Size; public string Path; public IntPtr File, Subject;
  }
  [StructLayout(LayoutKind.Sequential)] struct TrustData {
   public uint Size; public IntPtr Policy, SIP; public uint UI, Revocation, Choice;
   public IntPtr File; public uint StateAction; public IntPtr State, URL;
   public uint Flags, Context; public IntPtr Settings;
  }
  [DllImport("wintrust.dll", ExactSpelling=true, PreserveSig=true)]
  static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
  public static int Verify(string path) {
   var file=new FileInfo { Size=(uint)Marshal.SizeOf(typeof(FileInfo)), Path=path };
   IntPtr memory=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(FileInfo)));
   Marshal.StructureToPtr(file,memory,false);
   var action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
   var data=new TrustData {Size=(uint)Marshal.SizeOf(typeof(TrustData)), UI=2, Choice=1,
    File=memory, StateAction=1, Flags=0x1010};
   try { return WinVerifyTrust(new IntPtr(-1),ref action,ref data); }
   finally {
    data.StateAction=2; WinVerifyTrust(new IntPtr(-1),ref action,ref data);
    Marshal.DestroyStructure(memory,typeof(FileInfo)); Marshal.FreeHGlobal(memory);
   }
  }
 }
}
'@
}
$taskFile = (Get-Item -LiteralPath $FilePath).FullName
$taskSignature = Get-AuthenticodeSignature -LiteralPath $taskFile
if (-not $taskSignature.SignerCertificate) { throw 'Файл не содержит читаемой подписи Authenticode.' }
if ($CertificateThumbprint -and $taskSignature.SignerCertificate.Thumbprint -ne ($CertificateThumbprint -replace '\s', '')) { throw 'Файл подписан другим сертификатом.' }
$taskResult = [BarsSigning.NativeTrust]::Verify($taskFile)
$taskSelfSigned = $taskSignature.SignerCertificate.Subject -eq $taskSignature.SignerCertificate.Issuer
$taskUntrustedRoot = $taskResult -eq -2146762487 # CERT_E_UNTRUSTEDROOT, 0x800B0109
if ($taskResult -ne 0 -and -not ($taskUntrustedRoot -and $taskSelfSigned)) {
    throw ('Проверка Authenticode не пройдена: 0x{0:X8}; {1}' -f $taskResult, $taskSignature.StatusMessage)
}
if ($taskSignature.Status -eq 'HashMismatch') { throw 'Содержимое файла изменено после подписи.' }
[PSCustomObject]@{
    FilePath = $taskFile
    SignatureChecked = $true
    TrustedByWindows = ($taskResult -eq 0)
    VerificationCode = ('0x{0:X8}' -f $taskResult)
    Publisher = $taskSignature.SignerCertificate.Subject
    Thumbprint = $taskSignature.SignerCertificate.Thumbprint
    Timestamped = [bool]$taskSignature.TimeStamperCertificate
}
