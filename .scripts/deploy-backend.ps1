<#
Deploy-TubesterBackend.ps1

Publishes Tubester (.NET API) locally and deploys it to your Hetzner server,
then restarts the API.

Requirements:
- Windows has OpenSSH (ssh, scp) available (Windows 10/11 usually does)
- You can SSH to the server (preferably via key auth)
- On server, you either:
  A) run a systemd service named "tubester-api", OR
  B) run the API manually (the script can pkill + start with nohup)

Usage examples:
  .\Deploy-TubesterBackend.ps1
  .\Deploy-TubesterBackend.ps1 -ServerHost 157.180.24.180 -ServerUser root
  .\Deploy-TubesterBackend.ps1 -RestartMode systemd
  .\Deploy-TubesterBackend.ps1 -RestartMode nohup

#>

[CmdletBinding()]
param(
  # Local repo root (defaults to folder of this script)
  [string]$RepoRoot = (Resolve-Path (Split-Path -Parent $PSCommandPath)).Path,

  # API project path relative to RepoRoot
  [string]$ApiProject = "../YouTubester.Api\YouTubester.Api.csproj",

  # Local publish output folder
  [string]$PublishOut = "../.artifacts\api",

  # Remote server connection
  [string]$ServerHost = "157.180.24.180",
  [string]$ServerUser = "root",

  # Remote deploy directory
  [string]$RemoteDir = "/opt/tubester/api",

  # How to restart: "systemd" (recommended) or "nohup"
  [ValidateSet("systemd","nohup")]
  [string]$RestartMode = "systemd",

  # systemd service name (used when RestartMode=systemd)
  [string]$ServiceName = "tubester-api",

  # Kestrel binding (used when RestartMode=nohup)
  [string]$KestrelUrls = "http://127.0.0.1:5000",

  # Environment (used when RestartMode=nohup)
  [string]$AspNetEnv = "Production"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Exec([string]$cmd) {
  Write-Host ">> $cmd" -ForegroundColor Cyan
  Invoke-Expression $cmd
  if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code ${LASTEXITCODE}: $cmd" }
}


# --- Paths ---
$repo = (Resolve-Path $RepoRoot).Path
$proj = Join-Path $repo $ApiProject
$out  = Join-Path $repo $PublishOut

if (!(Test-Path $proj)) {
  throw "API project not found: $proj"
}

# --- Publish ---
Write-Host "`n=== Publishing API ===" -ForegroundColor Green
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $out | Out-Null

# NOTE: Use -p:GenerateDocumentationFile=true if you want XML docs for Swagger.
Exec "dotnet publish `"$proj`" -c Release -o `"$out`""

# Sanity check
$apiDll = Join-Path $out "YouTubester.Api.dll"
if (!(Test-Path $apiDll)) {
  throw "Publish output missing YouTubester.Api.dll at: $apiDll"
}

# --- Upload ---
Write-Host "`n=== Uploading to server ===" -ForegroundColor Green

# Ensure remote dir exists
Exec "ssh tubester-prod `"mkdir -p $RemoteDir`""

# Upload to a temp dir, then atomically swap (prevents half-deploy)
$remoteTmp = "$RemoteDir.__tmp"
$remoteBak = "$RemoteDir.__bak"

Exec "ssh tubester-prod `"rm -rf $remoteTmp && mkdir -p $remoteTmp`""

# Copy whole publish folder to tmp (this creates: $remoteTmp/api/*)
Exec "scp -r `"$out`" tubester-prod:`"$remoteTmp/`""

# Move contents out of the nested folder (api/* -> .)
Exec "ssh tubester-prod `"sh -lc 'set -e; cd $remoteTmp; mv api/* .; rmdir api'`""


# Swap dirs: current -> bak, tmp -> current
# NOTE: This assumes $RemoteDir exists (we created it above).
Exec "ssh tubester-prod `"rm -rf $remoteBak; if [ -d $RemoteDir ]; then mv $RemoteDir $remoteBak; fi; mv $remoteTmp $RemoteDir`""

# --- Restart ---
Write-Host "`n=== Restarting API ===" -ForegroundColor Green
if ($RestartMode -eq "systemd") {
  Exec "ssh tubester-prod `"systemctl restart $ServiceName && systemctl --no-pager --full status $ServiceName`""
} else {
  # Kill running process (if any) and start via nohup
  $startCmd = "cd $RemoteDir && " +
              "export ASPNETCORE_URLS='$KestrelUrls'; " +
              "export ASPNETCORE_ENVIRONMENT='$AspNetEnv'; " +
              "nohup dotnet YouTubester.Api.dll > /var/log/tubester-api.log 2>&1 &"

  Exec "ssh tubester-prod `"pkill -f 'dotnet YouTubester.Api.dll' || true; $startCmd; sleep 1; ss -lntp | grep 5000 || true`""
  Write-Host "Server log: /var/log/tubester-api.log" -ForegroundColor Yellow
}

Write-Host "`n✅ Deploy finished." -ForegroundColor Green
Write-Host "Test locally on server:" -ForegroundColor Gray
Write-Host "  curl -I http://127.0.0.1:5000/api/auth/me" -ForegroundColor Gray
Write-Host "Test externally:" -ForegroundColor Gray
Write-Host "  https://tubester.app/api/auth/me" -ForegroundColor Gray
