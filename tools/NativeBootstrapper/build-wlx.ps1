# Build, package and deploy MdViewerWlx WLX plugin
#
# This script:
#   1. Publishes the managed MdViewerWlx assembly (dotnet publish)
#   2. Compiles the native wlx_bootstrapper.c -> MdViewerWlx.wlx
#   3. Copies the app host (MdViewerWlx.exe) from build output
#   4. Assembles everything into deploy/wlx/
#   5. Creates a Total Commander installation ZIP with pluginst.inf at ZIP root
#
# Prerequisites:
#   - Either:
#       - an initialized MSVC developer environment with cl.exe on PATH
#       - or ScopeCppSDK at C:\Program Files\Microsoft Visual Studio\18\Insiders\SDK\ScopeCppSDK\vc15
#   - .NET 10 SDK
#

$ErrorActionPreference = "Stop"
$ProjectRoot = Resolve-Path "$PSScriptRoot\..\.."
$NativeDir = "$ProjectRoot\tools\NativeBootstrapper"
$ManagedProject = "$ProjectRoot\MdViewerWlx\MdViewerWlx.csproj"
$SdkRoot = "C:\Program Files\Microsoft Visual Studio\18\Insiders\SDK\ScopeCppSDK\vc15"
$PluginInfPath = "$NativeDir\pluginst.inf"

if (-not (Test-Path $PluginInfPath)) {
    throw "pluginst.inf not found at: $PluginInfPath"
}

$PluginVersionLine = Get-Content -LiteralPath $PluginInfPath | Where-Object { $_ -match '^version=' } | Select-Object -First 1
if (-not $PluginVersionLine) {
    throw "version=... entry not found in: $PluginInfPath"
}

$PluginVersion = ($PluginVersionLine -replace '^version=', '').Trim()
if ([string]::IsNullOrWhiteSpace($PluginVersion)) {
    throw "Plugin version is empty in: $PluginInfPath"
}

# ---- Target directories ----
$ReleaseRoot = "$ProjectRoot\MdViewerWlx\bin\Release\net10.0-windows"
$PublishDir = "$ProjectRoot\MdViewerWlx\bin\Release\net10.0-windows\win-x64\publish"
$DeployDir = "$ProjectRoot\deploy\wlx"
$ZipPath = "$ProjectRoot\deploy\MdViewerWlx-$PluginVersion-tc.zip"
$NativeOutput = $PublishDir
$NativeObj = "$NativeOutput\wlx_bootstrapper.obj"
$NativeLib = "$NativeOutput\MdViewerWlx.lib"
$NativeExp = "$NativeOutput\MdViewerWlx.exp"
$NativeDirArtifacts = @(
    "$NativeDir\Log",
    "$NativeDir\runtimes",
    "$NativeDir\corehost_trace.log",
    "$NativeDir\trace_output.txt",
    "$NativeDir\Markdig.dll",
    "$NativeDir\MdViewer.Shared.dll",
    "$NativeDir\MdViewer.Shared.pdb",
    "$NativeDir\MdViewerWlx.deps.json",
    "$NativeDir\MdViewerWlx.dll",
    "$NativeDir\MdViewerWlx.exe",
    "$NativeDir\MdViewerWlx.pdb",
    "$NativeDir\MdViewerWlx.runtimeconfig.json",
    "$NativeDir\MdViewerWlx.wlx",
    "$NativeDir\Microsoft.Web.WebView2.Core.dll",
    "$NativeDir\Microsoft.Web.WebView2.Core.xml",
    "$NativeDir\Microsoft.Web.WebView2.WinForms.dll",
    "$NativeDir\Microsoft.Web.WebView2.WinForms.xml",
    "$NativeDir\Microsoft.Web.WebView2.Wpf.dll",
    "$NativeDir\Microsoft.Web.WebView2.Wpf.xml",
    "$NativeDir\WebView2Loader.dll",
    "$NativeDir\test_hostfxr_asm.exe",
    "$NativeDir\test_hostfxr.exe",
    "$NativeDir\test_wlx_bootstrapper.exe",
    "$NativeDir\test_wlx_hwnd.exe",
    "$NativeDir\test_wlx_native.exe"
)
$LegacyDirs = @(
    "$ReleaseRoot\publish",
    "$ReleaseRoot\runtimes"
)

Write-Host "=== MdViewerWlx Build+Deploy ===" -ForegroundColor Cyan
Write-Host ""

# ---- Step 0: Remove stale legacy outputs ----
Write-Host "==> Step 0/5: Cleaning stale legacy outputs..." -ForegroundColor Yellow
foreach ($dir in $LegacyDirs) {
    if (Test-Path $dir) {
        Remove-Item $dir -Recurse -Force
        Write-Host "    - removed $dir" -ForegroundColor Gray
    }
}

foreach ($file in @($NativeObj, $NativeLib, $NativeExp)) {
    if (Test-Path $file) {
        Remove-Item $file -Force
        Write-Host "    - removed $file" -ForegroundColor Gray
    }
}

foreach ($path in $NativeDirArtifacts) {
    if (Test-Path $path) {
        Remove-Item $path -Recurse -Force
        Write-Host "    - removed $path" -ForegroundColor Gray
    }
}

Write-Host "  (ok) Legacy cleanup complete" -ForegroundColor Green
Write-Host ""

# ---- Step 1: Publish managed assembly ----
Write-Host "==> Step 1/5: Publishing managed assembly..." -ForegroundColor Yellow
& dotnet publish $ManagedProject -c Release --no-self-contained -r win-x64
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Write-Host "  (ok) Publish complete" -ForegroundColor Green
Write-Host ""

# ---- Step 2: Compile native bootstrapper (.wlx) ----
Write-Host "==> Step 2/5: Compiling native bootstrapper..." -ForegroundColor Yellow
$ClCommand = Get-Command cl.exe -ErrorAction SilentlyContinue
$UseToolchainFromPath = $null -ne $ClCommand

if ($UseToolchainFromPath) {
    $ClExe = $ClCommand.Source
    Write-Host "  (ok) Using cl.exe from PATH: $ClExe" -ForegroundColor Green
} else {
    $ClExe = "$SdkRoot\VC\bin\cl.exe"
    if (-not (Test-Path $ClExe)) {
        throw "cl.exe not found on PATH and ScopeCppSDK cl.exe not found at: $ClExe"
    }

    # ScopeCppSDK has nested structure: vc15/SDK/ (Windows SDK) and vc15/VC/ (compiler + CRT)
    $SdkInclude = "$SdkRoot\SDK\include"
    $VcInclude  = "$SdkRoot\VC\include"
    $SdkLib     = "$SdkRoot\SDK\lib"
    $VcLib      = "$SdkRoot\VC\lib"
}

$ClArgs = @(
    "/nologo"
)

if (-not $UseToolchainFromPath) {
    $ClArgs += @(
        "/I$SdkInclude\um",
        "/I$SdkInclude\shared",
        "/I$SdkInclude\ucrt",
        "/I$VcInclude"
    )
}

$ClArgs += @(
    "/Fo:$NativeObj",
    "/LD",
    "/Fe:$NativeOutput\MdViewerWlx.wlx",
    "$NativeDir\wlx_bootstrapper.c",
    "/link",
    "/IMPLIB:$NativeLib",
    "/OUT:$NativeOutput\MdViewerWlx.wlx"
)

if (-not $UseToolchainFromPath) {
    $ClArgs += @(
        "/LIBPATH:$SdkLib",
        "/LIBPATH:$VcLib"
    )
}

$ClArgs += @(
    "kernel32.lib",
    "user32.lib"
)

& "$ClExe" @ClArgs
if ($LASTEXITCODE -ne 0) { throw "cl.exe compile failed" }

# Duplicate the x64 binary as .wlx64 to match common TC x64 plugin layout.
Copy-Item "$NativeOutput\MdViewerWlx.wlx" "$NativeOutput\MdViewerWlx.wlx64" -Force

Write-Host "  (ok) Native bootstrapper compiled" -ForegroundColor Green
Write-Host ""

# ---- Step 3: Copy app host (MdViewerWlx.exe) ----
Write-Host "==> Step 3/5: Copying app host binary..." -ForegroundColor Yellow
# The app host is produced by dotnet publish and must be present in the publish dir.
$AppHostDest = "$PublishDir\MdViewerWlx.exe"

if (Test-Path $AppHostDest) {
    Write-Host "  (ok) App host already present in publish dir" -ForegroundColor Green
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
Write-Host "==> Step 4/5: Assembling deploy directory..." -ForegroundColor Yellow

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
    "MdViewer.Shared.dll",       # Shared library
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
$PluginInf = $PluginInfPath
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

Write-Host "" 

# ---- Step 5: Create installation ZIP ----
Write-Host "==> Step 5/5: Creating Total Commander installation ZIP..." -ForegroundColor Yellow
if (Test-Path $ZipPath) {
    Remove-Item -LiteralPath $ZipPath -Force
}

Compress-Archive -Path "$DeployDir\*" -DestinationPath $ZipPath -CompressionLevel Optimal
Write-Host "  (ok) Installation ZIP created" -ForegroundColor Green

# Show result
$wlxFile = Get-Item "$DeployDir\MdViewerWlx.wlx"
$totalSize = (Get-ChildItem $DeployDir -Recurse | Measure-Object Length -Sum).Sum
$zipFile = Get-Item $ZipPath
Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Cyan
Write-Host "  WLX plugin deployed to: $DeployDir" -ForegroundColor Green
Write-Host "  TC install ZIP: $ZipPath" -ForegroundColor Green
Write-Host "  Plugin version: $PluginVersion" -ForegroundColor Green
Write-Host "  Native bootstrapper: $([int]($wlxFile.Length / 1024)) KB ($($wlxFile.LastWriteTime))" -ForegroundColor Green
Write-Host "  Total size: $([math]::Round($totalSize / 1MB, 2)) MB" -ForegroundColor Green
Write-Host "  ZIP size: $([math]::Round($zipFile.Length / 1MB, 2)) MB" -ForegroundColor Green
Write-Host ""
Write-Host "To install in Total Commander:" -ForegroundColor White
Write-Host "  1. Configuration -> Plugins -> Install plugin" -ForegroundColor White
Write-Host "     -> Browse to $ZipPath" -ForegroundColor White
Write-Host "  2. TC reads pluginst.inf from the ZIP and installs automatically" -ForegroundColor White
Write-Host "  (or manually: browse to deploy\wlx\pluginst.inf)" -ForegroundColor White
