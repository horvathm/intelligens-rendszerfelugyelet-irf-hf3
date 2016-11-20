#Creation of the runnable form of the solution.
#----------------------------------------------------------------------------------------------------

$NET_CSC = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"

$invocation = (Get-Variable MyInvocation).Value 
$scriptPath = Split-Path $invocation.MyCommand.Path 
$ROOT = Split-Path $scriptPath -Parent
Write-Output $ROOT

& $NET_CSC /t:exe /out:"$ROOT\dist\compiled.exe" $ROOT\src\Grapevine2\Grapevine.Example\Program.cs $ROOT\src\Grapevine2\Grapevine\Server\*.cs $ROOT\src\Grapevine2\Grapevine\Client\*.cs $ROOT\src\Grapevine2\Grapevine\Util\*.cs