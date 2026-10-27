# Validate generated DISM commands against the installed cmdlet metadata. Never execute them.
param([Parameter(Mandatory=$true)][string]$AssemblyPath)
$ErrorActionPreference='Stop'
$assembly=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $AssemblyPath)))
$type=$assembly.GetType('ServiceTool.WindowsComponents',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$count=0
function Assert-FeatureCommand([string]$script,[string]$expected,[bool]$enable) {
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseInput($script,[ref]$tokens,[ref]$errors)
    if($errors.Count){throw 'Generated script has PowerShell syntax errors'}
    $commands=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq $expected},$true))
    if($commands.Count -ne 1){throw 'Expected exactly one feature command'}
    $metadata=Get-Command -Name $expected -CommandType Cmdlet -ErrorAction Stop
    $parameters=@($commands[0].CommandElements | Where-Object {$_ -is [Management.Automation.Language.CommandParameterAst]} | ForEach-Object {$_.ParameterName})
    foreach($name in $parameters){if(-not $metadata.Parameters.ContainsKey($name)){throw "Unsupported parameter $name for $expected"};$script:count++}
    if('NoRestart' -notin $parameters -or 'Online' -notin $parameters -or 'FeatureName' -notin $parameters){throw 'Required safety/profile parameters missing'}
    if($enable -ne ('All' -in $parameters)){throw 'All belongs only to feature enable'}
    $script:count++
}
foreach($ids in @('iis','telnet','tftp','iis,telnet,tftp')) {
    $enable=$type.GetMethod('EnableScript',$flags).Invoke($null,[object[]]@($ids))
    $disable=$type.GetMethod('DisableScript',$flags).Invoke($null,[object[]]@($ids))
    Assert-FeatureCommand $enable 'Enable-WindowsOptionalFeature' $true
    Assert-FeatureCommand $disable 'Disable-WindowsOptionalFeature' $false
}
$rejected=$false
try {Assert-FeatureCommand 'Disable-WindowsOptionalFeature -Online -FeatureName TelnetClient -All -NoRestart' 'Disable-WindowsOptionalFeature' $false} catch {$rejected=$true}
if(-not $rejected){throw 'Validator failed to reject the original unsupported parameter'}
$count++
Write-Output "PASS: $count feature-command metadata checks. Generated scripts were parsed, not executed."
