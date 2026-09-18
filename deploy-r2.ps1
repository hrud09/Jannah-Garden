#Requires -Version 5.1
<#
.SYNOPSIS
    Uploads built Addressables bundles for one platform from ServerData to Cloudflare R2.

.DESCRIPTION
    Replaces the old `firebase deploy --only hosting` step. Called automatically by
    AddressablesBatchBuild.Deploy() before every Android/iOS player build, and safe to run by hand to
    retry a deploy that failed without reopening Unity:

        .\deploy-r2.ps1 -BuildTarget Android

    Uploads with `rclone copy`, which is additive: objects already in the bucket are never deleted.
    Addressables bundle filenames carry a content hash, so app versions already in the wild keep
    resolving the exact bundles they were built against. Use -Prune to switch to `rclone sync`, which
    mirrors deletions, but only once the app versions referencing the older bundles have rolled off.

.PARAMETER BuildTarget
    Android or iOS. Selects ServerData/<BuildTarget> as the source and <BuildTarget>/ as the key prefix
    in the bucket, matching the [BuildTarget] token in the Addressables Remote.LoadPath.

.PARAMETER Prune
    Use `rclone sync` instead of `rclone copy`, deleting bucket objects that no longer exist locally.
    Breaks any installed app version still referencing them - see above.

.NOTES
    One-time setup:
      1. Create an R2 bucket in the Cloudflare dashboard.
      2. Enable public access on it and copy the https://pub-<hash>.r2.dev URL.
      3. Create an R2 API token with Object Read & Write scoped to that bucket.
      4. winget install Rclone.Rclone
      5. rclone config -> new remote named "r2", type s3, provider Cloudflare, region auto,
         endpoint https://<account-id>.r2.cloudflarestorage.com, ACL left blank.

    Credentials live in rclone's own config (%APPDATA%\rclone\rclone.conf), never in this repo.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Android', 'iOS')]
    [string]$BuildTarget,

    # Fill these two in once the bucket exists. Neither is a secret, so they live in the repo; the
    # PublicUrlBase must match the Addressables profile's Remote.LoadPath or the check below will fail.
    [string]$Bucket        = 'jannah-garden-assets',
    [string]$PublicUrlBase = 'https://pub-75d7b134df7e4a8b9c0d8b53a01dd824.r2.dev',

    [string]$RcloneRemote = 'r2',
    [switch]$Prune
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Fail([string]$message) {
    Write-Output "[deploy-r2] ERROR: $message"
    exit 1
}

# --- Configuration guard ---------------------------------------------------------------------------
# Running against a placeholder bucket would fail in a way the build could mistake for success - the
# same shape of silent failure that hid the Firebase outage. Refuse to start instead.
if ($Bucket -like '*REPLACE_WITH*' -or $PublicUrlBase -like '*REPLACE_WITH*') {
    Fail "R2 is not configured yet. Set the -Bucket and -PublicUrlBase defaults at the top of this script to the real bucket name and its https://pub-<hash>.r2.dev URL, and point the Addressables profile's Remote.LoadPath at that same URL followed by /[BuildTarget]."
}

# --- Source ----------------------------------------------------------------------------------------
$sourceDir = Join-Path $PSScriptRoot "ServerData\$BuildTarget"
if (-not (Test-Path -LiteralPath $sourceDir)) {
    Fail "No built content at $sourceDir. Build Addressables for $BuildTarget first."
}

$files = @(Get-ChildItem -LiteralPath $sourceDir -File -Recurse)
if ($files.Count -eq 0) {
    Fail "$sourceDir is empty - nothing to upload."
}

$totalBytes = ($files | Measure-Object -Property Length -Sum).Sum
Write-Output ("[deploy-r2] {0}: {1} file(s), {2:N1} MB -> {3}:{4}/{0}" -f $BuildTarget, $files.Count, ($totalBytes / 1MB), $RcloneRemote, $Bucket)

# --- Locate rclone -----------------------------------------------------------------------------------
# Unity hands this script whatever PATH the editor inherited when it launched, so an rclone installed
# after the editor started is invisible to it. Rather than require an editor restart, fall back to the
# usual install locations before giving up.
$found = Get-Command rclone -ErrorAction SilentlyContinue
if ($found) {
    $rclone = $found.Source
} else {
    $rclone = @(
        'C:\Tools\rclone\rclone.exe'
        "$env:LOCALAPPDATA\Microsoft\WinGet\Links\rclone.exe"
        "$env:ProgramFiles\rclone\rclone.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $rclone) {
    Fail "rclone not found on PATH or in any known install location. Install it with: winget install Rclone.Rclone"
}
Write-Output "[deploy-r2] Using rclone: $rclone"

$remotes = @(& $rclone listremotes)
if ($LASTEXITCODE -ne 0) {
    Fail "'rclone listremotes' exited with code $LASTEXITCODE."
}
if ($remotes -notcontains "${RcloneRemote}:") {
    Fail "rclone has no remote named '$RcloneRemote'. Run 'rclone config' and create an s3 remote (provider Cloudflare) with that name. Found: $($remotes -join ' ')"
}

# --- Upload ----------------------------------------------------------------------------------------
$verb = if ($Prune) { 'sync' } else { 'copy' }
if ($Prune) {
    Write-Output "[deploy-r2] PRUNE MODE - 'rclone sync' will DELETE bucket objects that are missing locally."
}

# Tuned for this project's shape: ~80 objects per platform averaging 15 MB, not many small files.
$rcloneArgs = @(
    $verb
    $sourceDir
    "${RcloneRemote}:$Bucket/$BuildTarget"
    '--transfers', '8'
    '--checkers', '16'
    '--s3-chunk-size', '32M'
    '--s3-upload-concurrency', '4'
    # A bucket-scoped API token is not permitted to HeadBucket, which rclone otherwise does on startup.
    '--s3-no-check-bucket'
    '--low-level-retries', '10'
    '--stats', '10s'
    '--stats-one-line'
    '--log-level', 'INFO'
)

& $rclone @rcloneArgs
if ($LASTEXITCODE -ne 0) {
    Fail "rclone $verb exited with code $LASTEXITCODE."
}

# --- Verify the objects are actually publicly readable ----------------------------------------------
# rclone succeeding only proves the write path works. Public access on r2.dev is a separate toggle, and
# without it every bundle 404s on device while the deploy still looks green.
$probe = $files | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
$relative = $probe.FullName.Substring($sourceDir.Length + 1).Replace('\', '/')
$probeUrl = "$($PublicUrlBase.TrimEnd('/'))/$BuildTarget/$relative"

Write-Output "[deploy-r2] Verifying public read: $probeUrl"
try {
    $response = Invoke-WebRequest -Uri $probeUrl -Method Head -UseBasicParsing -TimeoutSec 30
} catch {
    Fail "Public read check failed for $probeUrl - $($_.Exception.Message). A 404 or 401 here usually means public access (r2.dev) is not enabled on the bucket, or PublicUrlBase is wrong."
}

$served = [int64]@($response.Headers['Content-Length'])[0]
if ($served -ne $probe.Length) {
    Fail "Public read check served $served bytes for $probeUrl but the local file is $($probe.Length) bytes."
}

Write-Output "[deploy-r2] OK - $($files.Count) file(s) uploaded to $Bucket/$BuildTarget and publicly readable."
exit 0
