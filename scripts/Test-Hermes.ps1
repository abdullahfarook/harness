#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$KubeConfig = (Join-Path (Split-Path $PSScriptRoot -Parent) 'kube.config'),
    [string]$Context,
    [string]$Namespace = 'hermes',
    [string]$Release = 'hermes',
    [string]$Timeout = '30m',
    [switch]$SkipPersistence,
    [string]$ReportPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/deployment-results.json')
)
. "$PSScriptRoot/Common.ps1"
Initialize-Cluster $KubeConfig $Context
$report = @{timestamp_utc=[DateTime]::UtcNow.ToString('o'); context=$script:ClusterContext; namespace=$Namespace; release=$Release; checks=@(); passed=$false}
function Add-Check([string]$Name, [scriptblock]$Action) {
    & $Action
    $report.checks += @{name=$Name; passed=$true}
    Write-Host "PASS $Name"
}
function Invoke-PodPython([string]$Component, [string]$Code) {
    $target = if ($Component -eq 'hermes') { "statefulset/$Release-hermes" } else { "deployment/$Release-$Component" }
    (Invoke-Kube -Arguments @('exec','-i','-n',$Namespace,$target,'--','python','-') -InputText $Code).Output
}
try {
    foreach ($workload in "deployment/$Release-llm","statefulset/$Release-hermes","deployment/$Release-webui") { $null = Invoke-Kube @('rollout','status','-n',$Namespace,$workload,"--timeout=$Timeout") }
    Add-Check 'three_bound_pvcs' {
        $pvcs = ((Invoke-Kube @('get','pvc','-n',$Namespace,'-l',"app.kubernetes.io/instance=$Release",'-o','json')).Output | ConvertFrom-Json).items
        if (@($pvcs).Count -ne 3 -or @($pvcs | Where-Object { $_.status.phase -ne 'Bound' }).Count) { throw 'Expected three Bound PVCs.' }
    }
    Add-Check 'hermes_native_version' { $version = Invoke-Kube @('exec','-n',$Namespace,"statefulset/$Release-hermes",'--','hermes','--version'); $report.hermes_version = $version.Output.Trim() }
    Add-Check 'hermes_truthful_context_configuration' {
        $null = Invoke-PodPython hermes "import yaml; from pathlib import Path; from agent.model_metadata import MINIMUM_CONTEXT_LENGTH; c=yaml.safe_load(Path('/opt/data/config.yaml').read_text()); assert c['model']['context_length'] >= MINIMUM_CONTEXT_LENGTH; assert c['model']['provider']=='custom'; print(c['model']['context_length'])"
    }
    Add-Check 'hermes_doctor' {
        $doctor = Invoke-Kube @('exec','-n',$Namespace,"statefulset/$Release-hermes",'--','hermes','doctor')
        $report.doctor_output = $doctor.Output
    }
    $helm = Invoke-Helm @('test',$Release,'--namespace',$Namespace,'--timeout',$Timeout) -AllowFailure
    $logs = (Invoke-Kube @('logs','-n',$Namespace,"$Release-smoke")).Output
    Write-Host ($logs -split "`n" | Where-Object { $_ -match '^(PASS|FAIL) ' })
    $line = @($logs -split "`n" | Where-Object { $_.StartsWith('REPORT_JSON=') }) | Select-Object -Last 1
    if ($line) { $report.protocol = $line.Substring(12) | ConvertFrom-Json -AsHashtable }
    if (-not $line -or $helm.ExitCode -ne 0 -or -not $report.protocol.passed) { throw 'Helm live protocol tests failed; inspect smoke pod logs and report.' }
    Add-Check 'hermes_tool_verified_on_disk' {
        $marker = $report.protocol.tool_marker
        if ($marker.path -notmatch '^/opt/data/hermes-smoke-[a-f0-9]{32}\.txt$' -or $marker.value -notmatch '^[a-f0-9]{32}$') { throw 'Unsafe test marker.' }
        $code = "from pathlib import Path; assert Path('$($marker.path)').read_text() == '$($marker.value)'; print('marker verified')"
        $null = Invoke-PodPython hermes $code
    }
    if (-not $SkipPersistence) {
        $id = [Guid]::NewGuid().ToString('N')
        $paths = @{hermes="/opt/data/persistence-$id.txt"; webui="/app/backend/data/persistence-$id.txt"; llm="/models/persistence-$id.txt"}
        foreach ($component in 'hermes','webui') { $null = Invoke-PodPython $component "from pathlib import Path; Path('$($paths[$component])').write_text('$id')" }
        # llama.cpp image has curl/sh, not Python. Keep this test dependency-free.
        $null = Invoke-Kube @('exec','-n',$Namespace,"deployment/$Release-llm",'--','sh','-c',"printf '%s' '$id' > '$($paths.llm)'")
        foreach ($component in 'llm','hermes','webui') {
            $kind = if ($component -eq 'hermes') { 'statefulset' } else { 'deployment' }
            $target = "$kind/$Release-$component"
            $null = Invoke-Kube @('rollout','restart','-n',$Namespace,$target)
            $null = Invoke-Kube @('rollout','status','-n',$Namespace,$target,"--timeout=$Timeout")
            Add-Check "$component`_restart_persistence" {
                if ($component -eq 'llm') { $text = (Invoke-Kube @('exec','-n',$Namespace,$target,'--','cat',$paths.llm)).Output; if ($text -ne $id) { throw 'Model cache marker lost.' } }
                else { $null = Invoke-PodPython $component "from pathlib import Path; assert Path('$($paths[$component])').read_text() == '$id'" }
            }
        }
        foreach ($component in 'hermes','webui') { $null = Invoke-PodPython $component "from pathlib import Path; Path('$($paths[$component])').unlink()" }
        $null = Invoke-Kube @('exec','-n',$Namespace,"deployment/$Release-llm",'--','rm',$paths.llm)
    }
    $null = Invoke-PodPython hermes "from pathlib import Path; Path('$($report.protocol.tool_marker.path)').unlink()"
    $report.workloads = ((Invoke-Kube @('get','pods','-n',$Namespace,'-l',"app.kubernetes.io/instance=$Release",'-o','json')).Output | ConvertFrom-Json -AsHashtable).items | ForEach-Object { @{name=$_.metadata.name; phase=$_.status.phase; imageIDs=$_.status.containerStatuses.imageID} }
    $report.passed = $true
} catch {
    $report.error = $_.Exception.Message
    Write-Diagnostics $Namespace $Release
    throw
} finally {
    $directory = Split-Path $ReportPath -Parent
    if ($directory) { [void](New-Item -ItemType Directory -Force -Path $directory) }
    $report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $ReportPath -Encoding utf8
    Write-Host "Test report: $ReportPath"
}
