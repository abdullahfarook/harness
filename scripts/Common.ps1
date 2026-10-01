#Requires -Version 7.0
Set-StrictMode -Version 1.0
$ErrorActionPreference = 'Stop'

function Invoke-Native {
    param([Parameter(Mandatory)][string]$Command, [string[]]$Arguments = @(), [string]$InputText, [switch]$AllowFailure)
    $resolved = (Get-Command $Command -ErrorAction Stop).Source
    $info = [System.Diagnostics.ProcessStartInfo]::new($resolved)
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true; $info.RedirectStandardInput = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    # OCI's Windows launcher invokes Windows PowerShell; do not give it incompatible PS7 module paths.
    if ($IsWindows) { $info.Environment['PSModulePath'] = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\Modules;$env:ProgramFiles\WindowsPowerShell\Modules" }
    $process = [System.Diagnostics.Process]::new(); $process.StartInfo = $info
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        if ($PSBoundParameters.ContainsKey('InputText')) { $process.StandardInput.Write($InputText) }
        $process.StandardInput.Close(); $process.WaitForExit()
        $result = [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $stdout.GetAwaiter().GetResult(); Error = $stderr.GetAwaiter().GetResult() }
        if ($result.ExitCode -ne 0 -and -not $AllowFailure) { throw "$Command failed (exit $($result.ExitCode)): $($result.Error)" }
        return $result
    } finally { $process.Dispose() }
}

function Initialize-Cluster {
    param([string]$KubeConfig, [string]$Context)
    $script:KubeConfigPath = (Resolve-Path -LiteralPath $KubeConfig).Path
    $script:ClusterContext = if ($Context) { $Context } else { (Invoke-Native kubectl @('--kubeconfig', $script:KubeConfigPath, 'config', 'current-context')).Output.Trim() }
    $script:ClusterArgs = @('--kubeconfig', $script:KubeConfigPath, '--context', $script:ClusterContext)
}

function Invoke-Kube {
    param([string[]]$Arguments, [string]$InputText, [switch]$AllowFailure)
    $parameters = @{Command='kubectl'; Arguments=($script:ClusterArgs + $Arguments); AllowFailure=$AllowFailure}
    if ($PSBoundParameters.ContainsKey('InputText')) { $parameters.InputText = $InputText }
    Invoke-Native @parameters
}

function Invoke-Helm {
    param([string[]]$Arguments, [switch]$AllowFailure)
    Invoke-Native helm (@('--kubeconfig', $script:KubeConfigPath, '--kube-context', $script:ClusterContext) + $Arguments) -AllowFailure:$AllowFailure
}

function New-RandomSecret { [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant() }
function Convert-Cpu([string]$Quantity) { if ($Quantity.EndsWith('m')) { return [double]$Quantity.TrimEnd('m') }; if ($Quantity.EndsWith('n')) { return [double]$Quantity.TrimEnd('n') / 1000000 }; return [double]$Quantity * 1000 }
function Convert-Memory([string]$Quantity) {
    if ($Quantity -match '^([0-9.]+)(Ki|Mi|Gi|Ti|K|M|G|T)?$') {
        $scale = @{Ki=1024; Mi=1048576; Gi=1073741824; Ti=1099511627776; K=1000; M=1000000; G=1000000000; T=1000000000000}
        return [double]$Matches[1] * $(if ($Matches[2]) { $scale[$Matches[2]] } else { 1 })
    }
    throw "Unsupported memory quantity: $Quantity"
}

function Get-PodRequest($Spec) {
    $cpu=0.0; $memory=0.0
    foreach ($container in $Spec.containers) {
        if ($container.resources -and $container.resources.requests) {
            if ($container.resources.requests.cpu) { $cpu += Convert-Cpu $container.resources.requests.cpu }
            if ($container.resources.requests.memory) { $memory += Convert-Memory $container.resources.requests.memory }
        }
    }
    foreach ($container in @($Spec.initContainers)) {
        if ($container -and $container.resources -and $container.resources.requests) {
            if ($container.resources.requests.cpu) { $cpu = [Math]::Max($cpu, (Convert-Cpu $container.resources.requests.cpu)) }
            if ($container.resources.requests.memory) { $memory = [Math]::Max($memory, (Convert-Memory $container.resources.requests.memory)) }
        }
    }
    @{ Cpu=$cpu; Memory=$memory }
}

function Get-WorkloadPodSpec($Workload) { if ($Workload.kind -eq 'Pod') { return $Workload.spec }; return $Workload.spec.template.spec }

function Write-Diagnostics([string]$Namespace, [string]$Release) {
    foreach ($args in @(@('get','pods','-n',$Namespace,'-l',"app.kubernetes.io/instance=$Release",'-o','wide'), @('get','events','-n',$Namespace,'--sort-by=.lastTimestamp'))) {
        Write-Host (Invoke-Kube -Arguments $args -AllowFailure).Output
    }
}
