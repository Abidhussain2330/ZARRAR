<#
.SYNOPSIS
  Packs ZARRAR into a small, clean source zip, and optionally frees disk space here.

.DESCRIPTION
  The working folder grows to several GB because of things that are rebuilt automatically:
  .NET build output (bin/obj, ~1 GB+ with native image libraries for every OS), the frontend's
  node_modules and build folders, and runtime data such as screen recordings and logs. None of it
  belongs in a copy you hand to someone else; `dotnet build` and `pnpm install` recreate it.

  This script zips only the source (about 10-20 MB) into the folder ABOVE this one, so the zip
  never ends up inside itself. Your working folder is not changed unless you pass a clean switch.

  Left out of the zip:
    - bin, obj, node_modules, ClientApp\build, wwwroot\admin, .vs/.idea/.vscode, TestResults
    - src\BURAQ\files (recordings, logs, uploads made while testing)
    - secrets: .env files (the .example templates are kept), *.key, *.pfx, *.pem, *.user
    - _zarrar_backup_originals*.zip, earlier ZARRAR-*.zip packages, *.ivf, *.log
    - upstream-only material: docs\engineering (BURAQ build-phase notes) and .github

.PARAMETER Output
  Where to write the zip. Default: ..\ZARRAR-<date>-<time>.zip next to this folder.

.PARAMETER Clean
  Also delete bin, obj and ClientApp\build here to free space (the next build recreates them).
  Stop the AppHost first, or Windows will refuse to delete the files it is using.

.PARAMETER CleanAll
  -Clean, plus delete ClientApp\node_modules (run `pnpm install` before the next `pnpm dev`)
  and the test recordings and logs under src\BURAQ\files.

.PARAMETER IncludeRecordings
  Put src\BURAQ\files\zarrar-recordings into the zip (they are large; off by default).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\zarrar-package.ps1

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\zarrar-package.ps1 -Clean
#>
param(
  [string]$Output,
  [switch]$Clean,
  [switch]$CleanAll,
  [switch]$IncludeRecordings
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $PSScriptRoot).Path.TrimEnd('\')

if (-not (Test-Path -LiteralPath (Join-Path $root 'BURAQ.sln'))) {
  throw "Run this script from the ZARRAR folder (the one with BURAQ.sln). It is in: $root"
}

if (-not $Output) {
  $stamp = Get-Date -Format 'yyyyMMdd-HHmm'
  $Output = Join-Path (Split-Path $root -Parent) "ZARRAR-$stamp.zip"
}
$Output = [System.IO.Path]::GetFullPath($Output)

# Folder names skipped wherever they appear.
$skipDirNames = @('bin', 'obj', 'node_modules', '.vs', '.vscode', '.idea', 'TestResults', 'coverage', '.cache', '.git', '.worktrees')

# Paths (relative to the root, forward slashes) skipped as a whole.
$skipPaths = @(
  'src/BURAQ/ClientApp/build',
  'src/BURAQ/wwwroot/admin',
  'docs/engineering',
  '.github',
  '.buraq-runtime',
  'release'
)
if ($IncludeRecordings) {
  $skipPaths += @('src/BURAQ/files/logs')
} else {
  $skipPaths += @('src/BURAQ/files')
}

function Test-SkipFile([string]$rel, [string]$name) {
  if ($name -like '.env*' -and $name -notlike '*.example') { return $true }
  foreach ($pattern in @('*.key', '*.pfx', '*.pem', '*.user', '*.suo', '*.log', '_zarrar_backup_originals*.zip', 'ZARRAR-*.zip')) {
    if ($name -like $pattern) { return $true }
  }
  if ($name -like '*.ivf' -and -not $IncludeRecordings) { return $true }
  return $false
}

function Get-PackFiles([string]$dir) {
  foreach ($item in Get-ChildItem -LiteralPath $dir -Force) {
    $rel = $item.FullName.Substring($root.Length + 1).Replace('\', '/')
    if ($item.PSIsContainer) {
      if ($skipDirNames -contains $item.Name) { continue }
      if ($skipPaths -contains $rel) { continue }
      Get-PackFiles $item.FullName
    } elseif (-not (Test-SkipFile $rel $item.Name) -and $item.FullName -ne $Output) {
      $item
    }
  }
}

Write-Host "Collecting source files from $root ..."
$files = @(Get-PackFiles $root)
$bytes = ($files | Measure-Object -Property Length -Sum).Sum

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (Test-Path -LiteralPath $Output) { Remove-Item -LiteralPath $Output -Force }
$zip = [System.IO.Compression.ZipFile]::Open($Output, [System.IO.Compression.ZipArchiveMode]::Create)
try {
  foreach ($file in $files) {
    $entry = 'ZARRAR/' + $file.FullName.Substring($root.Length + 1).Replace('\', '/')
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
      $zip, $file.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal)
  }
} finally {
  $zip.Dispose()
}

$zipMb = [math]::Round((Get-Item -LiteralPath $Output).Length / 1MB, 1)
$srcMb = [math]::Round($bytes / 1MB, 1)
Write-Host ("Packed {0} files ({1} MB) into {2} ({3} MB)." -f $files.Count, $srcMb, $Output, $zipMb) -ForegroundColor Green

if ($Clean -or $CleanAll) {
  $targets = @()
  foreach ($project in @('src/BURAQ', 'src/BURAQ.AppHost', 'src/BURAQ.Test')) {
    $targets += (Join-Path $root "$project/bin"), (Join-Path $root "$project/obj")
  }
  $targets += (Join-Path $root 'src/BURAQ/ClientApp/build')
  if ($CleanAll) {
    $targets += (Join-Path $root 'src/BURAQ/ClientApp/node_modules')
    $targets += (Join-Path $root 'src/BURAQ/files/zarrar-recordings')
    $targets += (Join-Path $root 'src/BURAQ/files/logs')
  }

  foreach ($target in $targets) {
    if (-not (Test-Path -LiteralPath $target)) { continue }
    try {
      Remove-Item -LiteralPath $target -Recurse -Force
      Write-Host "Deleted $target"
    } catch {
      Write-Warning "Could not delete $target (is the AppHost or pnpm dev still running?): $($_.Exception.Message)"
    }
  }

  Write-Host 'Cleaned. The next `dotnet build` recreates bin/obj.' -ForegroundColor Green
  if ($CleanAll) {
    Write-Host 'Run `pnpm install` in src\BURAQ\ClientApp before the next `pnpm dev`.' -ForegroundColor Yellow
  }
}
