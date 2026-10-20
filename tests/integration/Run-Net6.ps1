$ErrorActionPreference='Stop'
if($env:COMPUTERNAME -ne 'BARS-LAB-01'){throw 'Not the dedicated test guest.'}
$files=Get-ChildItem -LiteralPath 'C:\Lab\Input' -Filter '*6.0.36*.exe'
if($files.Count -ne 4){throw 'Four installers required.'}
foreach($file in $files){
 $signature=Get-AuthenticodeSignature -LiteralPath $file.FullName
 if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*Microsoft Corporation*'){throw ('Microsoft signature was not verified: '+$file.Name+' '+$signature.Status)}
 [PSCustomObject]@{File=$file.Name;Hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash;Signature=$signature.Status}
}
& 'C:\Lab\Input\Net6-Integration.exe'
$code=$LASTEXITCODE
[PSCustomObject]@{IntegrationExitCode=$code;Report='C:\Lab\Reports\net6-integration.log'}
if($code -ne 0){throw 'Real integration test failed; inspect guest report.'}
