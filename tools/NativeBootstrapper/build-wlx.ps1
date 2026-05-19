# Build and deploy MdViewerWlx WLX plugin
#
# This script:
#   1. Publishes the managed MdViewerWlx assembly (dotnet publish)
#   2. Compiles the native wlx_bootstrapper.c -> MdViewerWlx.wlx
#   3. Copies the app host (MdViewerWlx.exe) from build output
#   4. Assembles everything into deploy/wlx/
#
# Prerequisites:
#   - ScopeCppSDK at C:\Program Files\Microsoft Visual Studio\18\Insiders\SDK\ScopeCppSDK\vc15
#   - .NET 10 SDK
#

$ErrorActionPreference = "Stop"
$ProjectRoot = Resolve-Path "$PSScriptRoot\..\.."
$NativeDir = "$ProjectRoot\tools\NativeBootstrapper"
$ManagedProject = "$ProjectRoot\MdViewerWlx\MdViewerWlx.csproj"
$SdkRoot = "C:\Program Files\Microsoft Visual Studio\18\Insiders\SDK\ScopeCppSDK\vc15"

# ---- Target directories ----
$ReleaseRoot = "$ProjectRoot\MdViewerWlx\bin\Release\net10.0-windows"
$PublishDir = "$ProjectRoot\MdViewerWlx\bin\Release\net10.0-windows\win-x64\publish"
$DeployDir = "$ProjectRoot\deploy\wlx"
$LegacyDirs = @(
    "$ReleaseRoot\publish",
    "$ReleaseRoot\runtimes"
)

Write-Host "=== MdViewerWlx Build+Deploy ===" -ForegroundColor Cyan
Write-Host ""

# ---- Step 0: Remove stale legacy outputs ----
Write-Host "==> Step 0/4: Cleaning stale legacy outputs..." -ForegroundColor Yellow
foreach ($dir in $LegacyDirs) {
    if (Test-Path $dir) {
        Remove-Item $dir -Recurse -Force
        Write-Host "    - removed $dir" -ForegroundColor Gray
    }
}
Write-Host "  (ok) Legacy cleanup complete" -ForegroundColor Green
Write-Host ""

# ---- Step 1: Publish managed assembly ----
Write-Host "==> Step 1/4: Publishing managed assembly..." -ForegroundColor Yellow
& dotnet publish $ManagedProject -c Release --no-self-contained -r win-x64
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Write-Host "  (ok) Publish complete" -ForegroundColor Green
Write-Host ""

# ---- Step 2: Compile native bootstrapper (.wlx) ----
Write-Host "==> Step 2/4: Compiling native bootstrapper..." -ForegroundColor Yellow
$ClExe = "$SdkRoot\VC\bin\cl.exe"
if (-not (Test-Path $ClExe)) {
    throw "ScopeCppSDK cl.exe not found at: $ClExe"
}

$NativeOutput = $PublishDir  # compile directly into publish dir so wlx is alongside
# ScopeCppSDK has nested structure: vc15/SDK/ (Windows SDK) and vc15/VC/ (compiler + CRT)
$SdkInclude = "$SdkRoot\SDK\include"
$VcInclude  = "$SdkRoot\VC\include"
$SdkLib     = "$SdkRoot\SDK\lib"
$VcLib      = "$SdkRoot\VC\lib"

& "$ClExe" /nologo `
    /I"$SdkInclude\um" `
    /I"$SdkInclude\shared" `
    /I"$SdkInclude\ucrt" `
    /I"$VcInclude" `
    /LD /Fe:"$NativeOutput\MdViewerWlx.wlx" "$NativeDir\wlx_bootstrapper.c" `
    /link `
    /LIBPATH:"$SdkLib" `
    /LIBPATH:"$VcLib" `
    kernel32.lib user32.lib
if ($LASTEXITCODE -ne 0) { throw "cl.exe compile failed" }

# Duplicate the x64 binary as .wlx64 to match common TC x64 plugin layout.
Copy-Item "$NativeOutput\MdViewerWlx.wlx" "$NativeOutput\MdViewerWlx.wlx64" -Force

Write-Host "  (ok) Native bootstrapper compiled" -ForegroundColor Green
Write-Host ""

# ---- Step 3: Copy app host (MdViewerWlx.exe) ----
Write-Host "==> Step 3/4: Copying app host binary..." -ForegroundColor Yellow
# The app host is produced by the build in the obj directory or build output.
# After publish, it should exist in the publish dir, but let's check.
$AppHostSource = "$NativeDir\MdViewerWlx.exe"
$AppHostDest = "$PublishDir\MdViewerWlx.exe"

if (Test-Path $AppHostDest) {
    Write-Host "  (ok) App host already present in publish dir" -ForegroundColor Green
} elseif (Test-Path $AppHostSource) {
    Copy-Item $AppHostSource $AppHostDest -Force
    Write-Host "  (ok) App host copied from NativeBootstrapper" -ForegroundColor Green
} else {
    # Fall back: look in build output
    $BuildExe = "$ProjectRoot\MdViewerWlx\bin\Release\net10.0-windows\win-x64\MdViewerWlx.exe"
    if (-not (Test-Path $BuildExe)) {
        # Try non-RID-specific path
        $BuildExe = "$ProjectRoot\MdViewerWlx\bin\Release\net10.0-windows\MdViewerWlx.exe"
    }
    if (Test-Path $BuildExe) {
        Copy-Item $BuildExe $AppHostDest -Force
        Write-Host "  (ok) App host copied from build output" -ForegroundColor Green
    } else {
        Write-Host "  (!) WARNING: MdViewerWlx.exe not found. hostfxr app mode needs it." -ForegroundColor Yellow
    }
}
Write-Host ""

# ---- Step 4: Assemble deploy directory ----
Write-Host "==> Step 4/4: Assembling deploy directory..." -ForegroundColor Yellow

# Create clean deploy dir
if (Test-Path $DeployDir) {
    Remove-Item "$DeployDir\*" -Recurse -Force
} else {
    New-Item -ItemType Directory -Path $DeployDir -Force | Out-Null
}

# Files needed for WLX plugin deployment:
$NeededFiles = @(
    "MdViewerWlx.wlx",           # Native bootstrapper (TC entry point)
    "MdViewerWlx.wlx64",         # Native bootstrapper (explicit x64 name for TC x64)
    "MdViewerWlx.dll",           # Managed assembly
    "MdViewerWlx.deps.json",     # Dependency manifest
    "MdViewerWlx.runtimeconfig.json",  # Runtime config
    "MdViewerWlx.exe",           # App host (argv[0] for hostfxr)
    "MdViewerWlx.pdb",           # Symbols (optional, helpful for debugging)
    "MdViewer.Shared.dll",       # Shared library
    "MdViewer.Shared.pdb",
    "Markdig.dll",               # Markdown parser
    "Microsoft.Web.WebView2.Core.dll",
    "Microsoft.Web.WebView2.Wpf.dll",
    "WebView2Loader.dll"         # Native WebView2 loader
)

# Copy root files
foreach ($file in $NeededFiles) {
    $src = "$PublishDir\$file"
    if (Test-Path $src) {
        Copy-Item $src "$DeployDir\" -Force
        Write-Host "    + $file" -ForegroundColor Gray
    }
}

# Copy pluginst.inf (TC installation descriptor)
$PluginInf = "$NativeDir\pluginst.inf"
if (Test-Path $PluginInf) {
    Copy-Item $PluginInf "$DeployDir\" -Force
    Write-Host "    + pluginst.inf" -ForegroundColor Gray
}

# Copy runtimes/ folder (needed for WebView2 native loader per-platform)
$RuntimesSrc = "$PublishDir\runtimes"
if (Test-Path $RuntimesSrc) {
    Copy-Item $RuntimesSrc "$DeployDir\" -Recurse -Force
    Write-Host "    + runtimes/" -ForegroundColor Gray
}

# Show result
$wlxFile = Get-Item "$DeployDir\MdViewerWlx.wlx"
$totalSize = (Get-ChildItem $DeployDir -Recurse | Measure-Object Length -Sum).Sum
Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Cyan
Write-Host "  WLX plugin deployed to: $DeployDir" -ForegroundColor Green
Write-Host "  Native bootstrapper: $([int]($wlxFile.Length / 1024)) KB ($($wlxFile.LastWriteTime))" -ForegroundColor Green
Write-Host "  Total size: $([math]::Round($totalSize / 1MB, 2)) MB" -ForegroundColor Green
Write-Host ""
Write-Host "To install in Total Commander:" -ForegroundColor White
Write-Host "  1. Configuration -> Plugins -> Install plugin" -ForegroundColor White
Write-Host "     -> Browse to deploy\wlx\pluginst.inf" -ForegroundColor White
Write-Host "  2. TC installs to %COMMANDER_PATH%\Plugins\wlx_mdviewer\ automatically" -ForegroundColor White
Write-Host "  (or manually: copy deploy\wlx to %COMMANDER_PATH%\Plugins\wlx\MdViewerWlx + register .wlx)" -ForegroundColor White
