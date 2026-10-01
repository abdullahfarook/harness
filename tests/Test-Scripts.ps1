#Requires -Version 7.0
$ErrorActionPreference='Stop'
. "$(Split-Path $PSScriptRoot -Parent)/scripts/Common.ps1"
function Assert($Condition,$Message) { if (-not $Condition) { throw $Message }; Write-Host "PASS $Message" }
Assert ((Convert-Cpu '1000m') -eq 1000) 'Millicore quantity'
Assert ((Convert-Cpu '2') -eq 2000) 'Whole-core quantity'
Assert ((Convert-Memory '2Gi') -eq 2147483648) 'Memory quantity'
$pod = @{containers=@(@{resources=@{requests=@{cpu='100m';memory='64Mi'}}});initContainers=@(@{resources=@{requests=@{cpu='200m';memory='128Mi'}}})}
$request=Get-PodRequest $pod
Assert ($request.Cpu -eq 200 -and $request.Memory -eq 134217728) 'Init container scheduling maximum'
$request=Get-PodRequest @{containers=@(@{resources=@{}})}
Assert ($request.Cpu -eq 0 -and $request.Memory -eq 0) 'Missing optional request fields'
$request=Get-PodRequest (Get-WorkloadPodSpec @{kind='Pod';spec=@{containers=@(@{resources=@{requests=@{cpu='50m';memory='64Mi'}}})}})
Assert ($request.Cpu -eq 50 -and $request.Memory -eq 67108864) 'Mandatory Helm hook capacity accounted'
try { $null=Invoke-Native pwsh @('-NoProfile','-Command','exit 7'); throw 'Expected exit failure not raised' } catch { Assert ($_.Exception.Message -match 'exit 7') 'Native nonzero exit fails fast' }
$r=Invoke-Native pwsh @('-NoProfile','-Command','exit 7') -AllowFailure
Assert ($r.ExitCode -eq 7) 'Explicit failure capture preserves exit code'
try { $null=Invoke-Native 'hermes-intentionally-missing-command'; throw 'Expected missing command not raised' } catch { Assert ($_.Exception.Message -match 'not recognized') 'Missing prerequisites fail fast' }
try { Initialize-Cluster "$PSScriptRoot/intentionally-missing.kubeconfig" ''; throw 'Expected missing kubeconfig not raised' } catch { Assert ($_.Exception.Message -match 'does not exist') 'Missing kubeconfig rejected' }
$secret=New-RandomSecret
Assert ($secret -match '^[a-f0-9]{64}$' -and $secret -ne (New-RandomSecret)) 'Cryptographically random independent credentials'
Write-Host 'Script regression checks passed.'
