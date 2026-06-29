#Requires -Version 5.1
param(
    [string]$Version  = "latest",
    [string]$CacheDir,
    [string]$OutDir,
    [string]$Token
)

$repo = "tamnguyendev/vmsign-agent-dist"
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$headers = @{ "User-Agent" = "WinFormsSample-Build" }

try {
    if ($Version -eq "latest") {
        $rel = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers $headers
    } else {
        $rel = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/tags/$Version" -Headers $headers
    }
    $tag = $rel.tag_name
} catch {
    Write-Host ("[USB Agent] Cannot reach GitHub ({0}). Skipping download." -f $_.Exception.Message)
    exit 0
}

$versionDir = Join-Path $CacheDir $tag
$agentExe   = Join-Path $versionDir "VMSignAgent.exe"

if (-not (Test-Path $agentExe)) {
    $asset = $rel.assets | Where-Object { $_.name -like "VMSignAgent-*.zip" } | Select-Object -First 1
    if (-not $asset) {
        Write-Host "[USB Agent] No zip asset found in release $tag - skipping."
        exit 0
    }

    $sizeKb = [int]($asset.size / 1024)
    Write-Host ("[USB Agent] Downloading {0} ({1} KB)..." -f $asset.name, $sizeKb)
    $null = New-Item -ItemType Directory -Force $versionDir
    $zipPath = Join-Path $versionDir "agent.zip"

    Invoke-WebRequest $asset.browser_download_url -Headers $headers -OutFile $zipPath -UseBasicParsing

    Expand-Archive $zipPath -DestinationPath $versionDir -Force
    Remove-Item $zipPath
    Write-Host "[USB Agent] Cached at: $versionDir"
} else {
    Write-Host "[USB Agent] Using cached $tag"
}

if ($OutDir) {
    $null = New-Item -ItemType Directory -Force $OutDir
    Get-ChildItem $versionDir -File |
        Where-Object { $_.Extension -notin @('.pdb', '.xml') } |
        ForEach-Object {
            $dest = Join-Path $OutDir $_.Name
            if (-not (Test-Path $dest) -or $_.LastWriteTime -gt (Get-Item $dest).LastWriteTime) {
                try {
                    Copy-Item $_.FullName $dest -Force -ErrorAction Stop
                    Write-Host "[USB Agent] Copied: $($_.Name)"
                } catch {
                    Write-Host ("[USB Agent] Skipped locked file: " + $_.Name)
                }
            }
        }
}
