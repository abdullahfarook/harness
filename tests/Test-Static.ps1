#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Assert($condition, $message) { if (-not $condition) { throw $message }; Write-Host "PASS $message" }
$chart = Join-Path $root 'helm/hermes-stack'
Assert (-not ((Get-Content "$root/scripts/Open-Hermes.ps1" -Raw) -match '\$process.WaitForExit\(\)')) 'Tunnel wait permits Ctrl+C cleanup'
Assert (Test-Path "$chart/Chart.yaml") 'Helm chart exists'
$render = & helm template hermes $chart --namespace hermes
Assert ($LASTEXITCODE -eq 0) 'Chart renders'
$yaml = $render -join "`n"
Assert (($yaml | Select-String -Pattern 'kind: Deployment' -AllMatches).Matches.Count -eq 2) 'Two separate Deployments'
Assert (($yaml | Select-String -Pattern 'kind: StatefulSet' -AllMatches).Matches.Count -eq 1) 'Separate Hermes StatefulSet'
Assert (($yaml | Select-String -Pattern 'kind: PersistentVolumeClaim' -AllMatches).Matches.Count -eq 3) 'Three persistent PVCs'
Assert ($yaml.Contains('"8192"') -and $yaml.Contains('--parallel')) '8K context and slot configuration'
Assert ($yaml.Contains('http://hermes-hermes:8642/v1')) 'UI routes through Hermes'
Assert (-not ($yaml -match 'mountPath: /opt/hermes\s*(,|\n|\r|$)')) 'Application tree is never masked'
Assert ($yaml.Contains('MINIMUM_CONTEXT_LENGTH = 64_000') -and $yaml.Contains('ALLOW_SMALL_CONTEXT')) 'Explicit audited 8K compatibility patch'
Assert (-not ($yaml -match 'type: (LoadBalancer|NodePort)')) 'Services stay private'
Assert ($yaml.Contains('automountServiceAccountToken: false')) 'No Kubernetes token in workloads'
Assert ($yaml.Contains('WEBUI_AUTH') -and $yaml.Contains('ENABLE_SIGNUP')) 'UI authentication configured'
foreach ($file in Get-ChildItem "$root/scripts/*.ps1") {
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
    Assert ($errors.Count -eq 0) "PowerShell parses: $($file.Name)"
}
& helm lint $chart
Assert ($LASTEXITCODE -eq 0) 'Helm lint passes'
& helm template hermes $chart --set llm.context=0 2>$null | Out-Null
Assert ($LASTEXITCODE -ne 0) 'Invalid context rejected'
& helm template hermes $chart --set hermes.allowSmallContext=false 2>$null | Out-Null
Assert ($LASTEXITCODE -ne 0) '8K without compatibility patch rejected before deployment'
& helm template hermes $chart --set llm.context=64000 2>$null | Out-Null
Assert ($LASTEXITCODE -ne 0) '64K with small-context patch rejected before deployment'
Write-Host 'All static checks passed.'
