#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$KubeConfig = (Join-Path (Split-Path $PSScriptRoot -Parent) 'kube.config'),
    [string]$Context,
    [ValidatePattern('^[a-z0-9][a-z0-9-]*$')][string]$Namespace = 'hermes',
    [ValidatePattern('^[a-z0-9][a-z0-9-]*$')][string]$Release = 'hermes',
    [string[]]$ValuesFile = @(),
    [string]$Timeout = '30m',
    [switch]$ValidateOnly,
    [switch]$SkipTests,
    [switch]$SkipPersistenceTests
)
. "$PSScriptRoot/Common.ps1"
$root = Split-Path $PSScriptRoot -Parent
$chart = Join-Path $root 'helm/hermes-stack'
$valueArgs = @(); foreach ($file in $ValuesFile) { $valueArgs += @('-f',(Resolve-Path -LiteralPath $file).Path) }
foreach ($tool in 'kubectl','helm','docker') { [void](Get-Command $tool -ErrorAction Stop) }
Initialize-Cluster $KubeConfig $Context
Write-Host "Preflight context=$script:ClusterContext namespace=$Namespace release=$Release"
$null = Invoke-Native helm (@('lint',$chart) + $valueArgs)
$render = (Invoke-Native helm (@('template',$Release,$chart,'--namespace',$Namespace) + $valueArgs)).Output
# Convert the actual rendered chart; preflight must use overrides, not hardcoded requests.
$objects = ('[' + ((Invoke-Kube -Arguments @('create','--dry-run=client','--validate=false','-f','-','-o','json') -InputText $render).Output -replace '(?m)^\}\r?\n\{', '},{') + ']' | ConvertFrom-Json -AsHashtable)
$images = @($objects | Where-Object { $_.kind -in 'Deployment','StatefulSet','Pod' } | ForEach-Object { $spec=Get-WorkloadPodSpec $_; $spec.containers.image; $spec.initContainers.image } | Where-Object { $_ } | Select-Object -Unique)
foreach ($image in $images) {
    if ($image -notmatch '@sha256:[a-f0-9]{64}$') { throw "Image must be pinned to an immutable digest: $image" }
    $imageInfo = (Invoke-Native docker @('buildx','imagetools','inspect',$image,'--format','{{json .Image}}')).Output | ConvertFrom-Json -AsHashtable
    $configs = if ($imageInfo.architecture) { @($imageInfo) } else { @($imageInfo.Values) }
    if (-not @($configs | Where-Object { $_.architecture -eq 'arm64' -and $_.os -eq 'linux' }).Count) { throw "Image has no Linux ARM64 configuration: $image" }
    Write-Host "ARM64 image verified: $($image.Split('@')[0])"
}
$nodes = ((Invoke-Kube @('get','nodes','-o','json')).Output | ConvertFrom-Json -AsHashtable).items
$pods = ((Invoke-Kube @('get','pods','-A','-o','json')).Output | ConvertFrom-Json -AsHashtable).items
$ready = @($nodes | Where-Object { $_.status.nodeInfo.architecture -eq 'arm64' -and -not $_.spec.unschedulable -and @($_.status.conditions | Where-Object { $_.type -eq 'Ready' -and $_.status -eq 'True' }).Count -gt 0 -and @($_.spec.taints | Where-Object { $_.effect -in 'NoSchedule','NoExecute' }).Count -eq 0 })
if (-not $ready.Count) { throw 'No Ready, schedulable ARM64 nodes.' }
$capacity = @($ready | ForEach-Object { @{Name=$_.metadata.name; Cpu=(Convert-Cpu $_.status.allocatable.cpu); Memory=(Convert-Memory $_.status.allocatable.memory)} })
foreach ($pod in $pods) {
    if ($pod.status.phase -in 'Succeeded','Failed') { continue }
    if ($pod.metadata.namespace -eq $Namespace -and $pod.metadata.labels['app.kubernetes.io/instance'] -eq $Release) { continue }
    $node = $capacity | Where-Object Name -eq $pod.spec.nodeName
    if ($node) { $request = Get-PodRequest $pod.spec; $node.Cpu -= $request.Cpu; $node.Memory -= $request.Memory }
}
foreach ($workload in @($objects | Where-Object { $_.kind -in 'Deployment','StatefulSet','Pod' } | Sort-Object { -(Get-PodRequest (Get-WorkloadPodSpec $_)).Cpu })) {
    $request = Get-PodRequest (Get-WorkloadPodSpec $workload)
    $fit = @($capacity | Where-Object { $_.Cpu -ge $request.Cpu -and $_.Memory -ge $request.Memory } | Sort-Object Cpu -Descending)
    if (-not $fit.Count) { throw "Insufficient schedulable CPU/memory for $($workload.metadata.name)." }
    $fit[0].Cpu -= $request.Cpu; $fit[0].Memory -= $request.Memory
    Write-Host "Capacity verified: $($workload.metadata.name) requests $($request.Cpu)m CPU"
}
$pvcs = @($objects | Where-Object kind -eq PersistentVolumeClaim)
foreach ($storage in @($pvcs.spec.storageClassName | Select-Object -Unique)) { $null = Invoke-Kube @('get','storageclass',$storage) }
$hermes = $objects | Where-Object kind -eq StatefulSet
$secretName = ($hermes.spec.template.spec.containers[0].env | Where-Object name -eq API_SERVER_KEY).valueFrom.secretKeyRef.name
if ($ValidateOnly) { Write-Host 'Validation passed; no mutations performed.'; return }
try {
    $namespaceResult = Invoke-Kube @('get','namespace',$Namespace,'--ignore-not-found','-o','name')
    if (-not $namespaceResult.Output.Trim()) { $null = Invoke-Kube @('create','namespace',$Namespace) }
    $existing = Invoke-Kube @('get','secret',$secretName,'-n',$Namespace,'--ignore-not-found','-o','json')
    if (-not $existing.Output.Trim()) {
        $secret = @{apiVersion='v1'; kind='Secret'; metadata=@{name=$secretName; namespace=$Namespace}; type='Opaque'; stringData=@{'api-key'=(New-RandomSecret); 'webui-secret'=(New-RandomSecret); 'admin-email'='hermes-admin@localhost.local'; 'admin-password'=(New-RandomSecret)}}
        $null = Invoke-Kube -Arguments @('create','-f','-') -InputText ($secret | ConvertTo-Json -Depth 8)
        Write-Host 'Created random credentials in Kubernetes Secret (not printed or stored in Helm values).'
    } else {
        $data = ($existing.Output | ConvertFrom-Json -AsHashtable).data
        foreach ($key in 'api-key','webui-secret','admin-email','admin-password') { if (-not $data[$key]) { throw "Existing secret missing required key: $key" } }
        Write-Host 'Preserving existing credentials.'
    }
    $null = Invoke-Helm (@('upgrade','--install',$Release,$chart,'--namespace',$Namespace,'--wait','--timeout',$Timeout,'--history-max','5') + $valueArgs)
    Write-Host 'Helm workloads ready.'
    if (-not $SkipTests) { & "$PSScriptRoot/Test-Hermes.ps1" -KubeConfig $script:KubeConfigPath -Context $script:ClusterContext -Namespace $Namespace -Release $Release -Timeout $Timeout -SkipPersistence:$SkipPersistenceTests }
    Write-Host "Access UI: kubectl --kubeconfig `"$script:KubeConfigPath`" --context `"$script:ClusterContext`" -n $Namespace port-forward service/$Release-webui 3000:8080 --address 127.0.0.1"
} catch { Write-Diagnostics $Namespace $Release; throw }
