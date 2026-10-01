#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$KubeConfig = (Join-Path (Split-Path $PSScriptRoot -Parent) 'kube.config'),
    [string]$Context,
    [string]$Namespace = 'hermes',
    [string]$Release = 'hermes',
    [ValidateRange(1024,65535)][int]$Port = 3000,
    [switch]$CopyAdminPassword,
    [switch]$NoBrowser
)
. "$PSScriptRoot/Common.ps1"
Initialize-Cluster $KubeConfig $Context
if ($CopyAdminPassword) {
    $workload = (Invoke-Kube @('get','deployment',"$Release-webui",'-n',$Namespace,'-o','json')).Output | ConvertFrom-Json
    $secretName = ($workload.spec.template.spec.containers[0].env | Where-Object name -eq WEBUI_ADMIN_PASSWORD).valueFrom.secretKeyRef.name
    $secret = (Invoke-Kube @('get','secret',$secretName,'-n',$Namespace,'-o','json')).Output | ConvertFrom-Json
    $email = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($secret.data.'admin-email'))
    Set-Clipboard ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($secret.data.'admin-password')))
    Write-Host "Administrator email: $email. Password copied to clipboard; clear your clipboard after signing in."
}
$info = [Diagnostics.ProcessStartInfo]::new((Get-Command kubectl).Source)
$info.UseShellExecute=$false; $info.CreateNoWindow=$true
foreach ($arg in ($script:ClusterArgs + @('-n',$Namespace,'port-forward',"service/$Release-webui","${Port}:8080",'--address','127.0.0.1'))) { $info.ArgumentList.Add($arg) }
if ($IsWindows) { $info.Environment['PSModulePath']="$env:SystemRoot\System32\WindowsPowerShell\v1.0\Modules;$env:ProgramFiles\WindowsPowerShell\Modules" }
$process = [Diagnostics.Process]::new(); $process.StartInfo=$info
try {
    [void]$process.Start()
    $ready=$false
    for ($i=0; $i -lt 60; $i++) {
        if ($process.HasExited) { throw "Port-forward exited: $($process.ExitCode)" }
        try { $null=Invoke-WebRequest "http://127.0.0.1:$Port/health" -TimeoutSec 2; $ready=$true; break } catch { Start-Sleep 1 }
    }
    if (-not $ready) { throw 'Local port-forward did not become ready.' }
    Write-Host "Web UI: http://127.0.0.1:$Port. Keep this window running; Ctrl+C stops the tunnel."
    if (-not $NoBrowser) { Start-Process "http://127.0.0.1:$Port" }
    while (-not $process.HasExited) { Start-Sleep -Milliseconds 250 }
    if ($process.ExitCode -ne 0) { throw "Port-forward failed: $($process.ExitCode)" }
} finally { if (-not $process.HasExited) { $process.Kill() }; $process.Dispose() }
