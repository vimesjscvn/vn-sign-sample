<#
.SYNOPSIS
    Commit pending changes, tag, and push — triggers the GitHub Actions WinForms release workflow.

.PARAMETER Version
    Release version, e.g. 1.0.19

.PARAMETER SdkVersion
    NuGet SDK version to reference (default: same as Version).
    Set explicitly if the SDK version differs from the sample version.

.PARAMETER Message
    Optional extra detail appended to the commit message body.

.EXAMPLE
    .\publish.ps1 1.0.19
    .\publish.ps1 1.0.19 -SdkVersion 1.0.19 -Message "Add ECDSA selector for BCY"
#>
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$SdkVersion = $Version,

    [string]$Message = ""
)

$ErrorActionPreference = "Stop"
$repo    = $PSScriptRoot
$tag     = "v$Version"
$csproj  = Join-Path $repo "WinFormsSample\WinFormsSample.csproj"

# ── Guard: tag must not already exist ────────────────────────────────────────
if (git -C $repo tag --list $tag) {
    Write-Error "Tag $tag already exists. Aborting."
    exit 1
}

# ── Bump SdkPkgVersion in WinFormsSample.csproj if needed ────────────────────
$raw     = Get-Content $csproj -Raw
$current = ([regex]"<SdkPkgVersion>(.+?)</SdkPkgVersion>").Match($raw).Groups[1].Value

if ($current -ne $SdkVersion) {
    $raw = $raw -replace "<SdkPkgVersion>.+?</SdkPkgVersion>", "<SdkPkgVersion>$SdkVersion</SdkPkgVersion>"
    Set-Content $csproj $raw -Encoding utf8 -NoNewline
    Write-Host "SdkPkgVersion: $current → $SdkVersion" -ForegroundColor Cyan
}

# ── Stage everything and commit (skip if working tree is clean) ───────────────
$dirty = git -C $repo status --porcelain
if ($dirty) {
    $body = if ($Message) { "`n`n$Message" } else { "" }
    git -C $repo add -A
    git -C $repo commit -m "chore: release $tag$body"
    Write-Host "Committed pending changes." -ForegroundColor Green
} else {
    Write-Host "Working tree clean — no commit needed." -ForegroundColor DarkGray
}

# ── Tag and push ──────────────────────────────────────────────────────────────
$branch = git -C $repo branch --show-current
git -C $repo tag $tag
git -C $repo push origin $branch --tags

Write-Host ""
Write-Host "Pushed $tag — GitHub Actions will build and create the GitHub Release." -ForegroundColor Green
Write-Host "https://github.com/vimesjscvn/vn-sign-sample/actions" -ForegroundColor DarkGray
