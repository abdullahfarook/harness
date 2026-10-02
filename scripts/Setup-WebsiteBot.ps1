param([string]$ModelRoot = "$PSScriptRoot/../.local/website-models", [switch]$SkipBrowser, [ValidateSet('qwen3','qwen','gemma','lfm','all')][string]$Brain='qwen3')
$ErrorActionPreference = 'Stop'
$ModelRoot = [IO.Path]::GetFullPath($ModelRoot)
$bundles = @(
    @{ Name='qwen3'; Repo='onnx-community/Qwen3-1.7B-ONNX'; Revision='cc6a06a21d614e9b8e92a6adfab1074d4e7d2438'; Files=@('onnx/model_q4.onnx','config.json','generation_config.json','tokenizer.json','tokenizer_config.json') },
    @{ Name='qwen'; Repo='onnx-community/Qwen2.5-1.5B-Instruct'; Revision='6287331f475a3e20e8c879be8fd4bf3551ad9d34'; Files=@('onnx/model_q4.onnx','config.json','generation_config.json','tokenizer.json','tokenizer_config.json') },
    @{ Name='gemma'; Repo='onnx-community/gemma-4-E2B-it-ONNX'; Revision='9f4bef82ea6e296bc69f8a2f5939f73af81b07a6'; Files=@('onnx/decoder_model_merged_q4.onnx','onnx/decoder_model_merged_q4.onnx_data','onnx/embed_tokens_q4.onnx','onnx/embed_tokens_q4.onnx_data','config.json','generation_config.json','tokenizer.json','tokenizer_config.json') },
    @{ Name='lfm'; Repo='LiquidAI/LFM2.5-1.2B-Thinking-ONNX'; Revision='e7fe61974e3a167dff77c5722db9a1cb7b57140f'; Files=@('onnx/model_q4.onnx','onnx/model_q4.onnx_data','config.json','generation_config.json','tokenizer.json','tokenizer_config.json') },
    @{ Name='laya'; Repo='receptron/laya-onnx'; Revision='68f27dfe5a27a54fb2b1fefc432f43f972e90868'; Files=@('laya.onnx','laya.onnx.data','laya_config.json','tokenizer/tokenizer.json','tokenizer/tokenizer_config.json') }
)
foreach ($bundle in $bundles) {
    if ($bundle.Name -ne 'laya' -and $Brain -ne 'all' -and $bundle.Name -ne $Brain) { continue }
    $records = @()
    foreach ($file in $bundle.Files) {
        $target = Join-Path (Join-Path $ModelRoot $bundle.Name) $file
        New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($target)) | Out-Null
        $url = "https://huggingface.co/$($bundle.Repo)/resolve/$($bundle.Revision)/$file"
        if (-not (Test-Path -LiteralPath $target)) {
            Write-Host "Downloading $($bundle.Name)/$file"
            & curl.exe --fail --location --http1.1 --continue-at - --speed-time 60 --speed-limit 1024 --connect-timeout 30 --retry 5 --retry-delay 3 --output "$target.partial" "$url`?download=true"
            if ($LASTEXITCODE -ne 0) { throw "Download failed: $url (partial preserved for diagnosis)" }
            Move-Item -LiteralPath "$target.partial" -Destination $target
        }
        $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
        $headers = & curl.exe --fail --silent --head $url
        if ($LASTEXITCODE -ne 0) { throw "Cannot verify remote metadata: $url" }
        $match = $headers | Select-String '^x-linked-etag:\s*"?([a-fA-F0-9]{64})"?' | Select-Object -First 1
        $remoteHash = if ($match) { $match.Matches.Groups[1].Value } else { '' }
        if ($remoteHash -match '^[a-fA-F0-9]{64}$' -and $remoteHash -ne $hash) { throw "Checksum mismatch: $target" }
        $records += @{ path=$file; bytes=(Get-Item -LiteralPath $target).Length; sha256=$hash }
    }
    @{ name=$bundle.Name; repo=$bundle.Repo; revision=$bundle.Revision; files=$records } | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path (Join-Path $ModelRoot $bundle.Name) 'manifest.json')
}
if (-not $SkipBrowser) {
    dotnet build "$PSScriptRoot/../harness/Harness.csproj"
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    & "$PSScriptRoot/../harness/bin/Debug/net10.0/playwright.ps1" install chromium
    if ($LASTEXITCODE -ne 0) { throw 'Chromium installation failed' }
}
Write-Host "MODEL_ROOT=$ModelRoot"
