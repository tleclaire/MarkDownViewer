# MarkdownViewer

MarkdownViewer is a Windows project for viewing Markdown files with two primary targets:

- a standalone WPF desktop application: `MdViewer`
- a Total Commander Lister plugin: `MdViewerWlx` for `*.md` and related formats

Rendering is based on `Markdig` for Markdown parsing and `WebView2` for HTML display. The WLX plugin uses a native C bootstrapper that loads the managed .NET components through `hostfxr`.

## Overview

- Platform: Windows x64
- UI technology: WPF
- Runtime: .NET 10 Windows Desktop
- Markdown parser: `Markdig`
- HTML renderer: `Microsoft.Web.WebView2`
- Total Commander plugin type: `WLX` (Lister plugin)

## Features

- Renders Markdown as HTML inside a WPF/WebView2 UI
- Uses advanced Markdig extensions via `UseAdvancedExtensions()`
- Supports emoji and smiley syntax
- Uses GitHub-inspired CSS with light/dark adaptation via `prefers-color-scheme`
- Sets a `base` path for relative content from Markdown files
- WLX plugin with Unicode entry points for Total Commander x64
- Fallback-first WLX behavior with immediate text preview
- Automatic switch to HTML/WebView2 once initialization completes

## Repository Structure

```text
MarkdownViewer/
|- MarkdownViewer/                     Desktop app (MdViewer)
|- MdViewer.Shared/                   Shared rendering and logging components
|- MdViewerWlx/                       Managed WLX layer for Total Commander
|- tools/
|  |- NativeBootstrapper/
|  |  |- wlx_bootstrapper.c           Native WLX bootstrapper in C
|  |  |- build-wlx.ps1                Build, deploy, and packaging script
|  |  |- pluginst.inf                 Total Commander installer descriptor
|- deploy/
|  |- wlx/                            Assembled WLX deploy directory
|  |- MdViewerWlx-<version>-tc.zip    Total Commander installation ZIP
|- README.md
```

The repository root contains `MdViewer.slnx`.

## Projects

### `MarkdownViewer`

Standalone WPF desktop application with the following profile:

- Project file: `MarkdownViewer\MarkdownViewer.csproj`
- Output type: `WinExe`
- Target framework: `net10.0-windows`
- WPF enabled
- App icon: `MarkdownViewer\Resources\app.ico`

The application uses `MdViewer.Shared` for rendering and logging.

### `MdViewer.Shared`

Shared library used by both the desktop application and the WLX plugin.

Important files:

- `MdViewer.Shared\MarkdownRenderService.cs`
- `MdViewer.Shared\FileLogger.cs`

Responsibilities:

- render Markdown into a complete HTML document
- provide GitHub-inspired CSS
- write error and status logs to files

### `MdViewerWlx`

Managed part of the Total Commander plugin.

- Project file: `MdViewerWlx\MdViewerWlx.csproj`
- Output type: `Exe`
- Target framework: `net10.0-windows`
- Platform: `x64`
- WPF enabled

Why `Exe` instead of `Dll`:

- the native bootstrapper initializes the CLR in app mode through `hostfxr_initialize_for_dotnet_command_line`
- this requires `MdViewerWlx.exe` as the app host
- this is necessary so WPF and WebView2 dependencies are resolved in the correct app context

Important files:

- `MdViewerWlx\WlxExports.cs`
- `MdViewerWlx\WlxHost.cs`
- `MdViewerWlx\WlxContentView.cs`
- `MdViewerWlx\Program.cs`

## WLX Plugin Architecture

The Total Commander plugin is split into two layers.

### 1. Native Bootstrapper

File: `tools\NativeBootstrapper\wlx_bootstrapper.c`

Responsibilities:

- loaded directly by Total Commander as `.wlx` or `.wlx64`
- exports the WLX functions
- handles `ListGetDetectString` natively
- loads `hostfxr.dll`
- initializes the .NET runtime
- loads managed export methods from `MdViewerWlx.dll`

Important implementation details:

- uses `hdt=5` for `load_assembly_and_get_function_pointer`
- uses `hostfxr_initialize_for_dotnet_command_line`
- passes `MdViewerWlx.exe` as `argv[0]`
- supports both ANSI and Unicode WLX API variants

Exported WLX functions:

- `ListGetDetectString`
- `ListGetDetectStringW`
- `ListLoad`
- `ListLoadW`
- `ListLoadNext`
- `ListLoadNextW`
- `ListCloseWindow`

Current detect string:

```text
ext="MD" | ext="MARKDOWN" | ext="MDOWN" | ext="RST"
```

### 2. Managed WLX Layer

Files:

- `MdViewerWlx\WlxExports.cs`
- `MdViewerWlx\WlxHost.cs`
- `MdViewerWlx\WlxContentView.cs`

Responsibilities:

- receives WLX calls forwarded from the native layer
- creates a WPF child window inside the Total Commander parent window
- initializes WebView2 in a delayed and robust way
- shows a text preview until WebView2 is ready

Hosting details:

- `HwndSource` is created directly on the calling Total Commander UI thread
- this avoids child-window creation deadlocks
- actual WebView2 initialization is delayed slightly via `DispatcherTimer`
- focus and z-order are corrected explicitly so the first Lister instance does not remain behind Total Commander

## Rendering

Rendering is implemented in `MdViewer.Shared\MarkdownRenderService.cs`.

Enabled Markdig pipeline features:

- `UseAdvancedExtensions()`
- `UseEmojiAndSmiley()`
- `UseSoftlineBreakAsHardlineBreak()`

Additional behavior:

- relative paths are resolved using a `file:///` `base` tag
- empty files and file-read errors return a full HTML document instead of throwing exceptions
- light and dark CSS is embedded directly into the HTML output

## Logging

File: `MdViewer.Shared\FileLogger.cs`

The logger writes log files into a `Log` subdirectory next to the executable:

```text
[ProgramFolder]\Log\log-YYYY-MM-DD.log
```

Examples:

- desktop app: next to `MdViewer.exe`
- WLX plugin: inside the plugin folder next to `MdViewerWlx.wlx` and `MdViewerWlx.exe`

The remaining logging is intended for production use and focuses on errors and warnings.

## Requirements

## Runtime

- Windows x64
- installed .NET desktop runtime or SDK compatible with `net10.0-windows`
- installed WebView2 Runtime
- for Total Commander: x64 Total Commander is recommended

## Build

- .NET 10 SDK
- One of the following native toolchains:

```text
an initialized MSVC developer environment with cl.exe on PATH
```

or:

```text
C:\Program Files\Microsoft Visual Studio\18\Insiders\SDK\ScopeCppSDK\vc15
```

The WLX build script first uses `cl.exe` from `PATH`. If none is available, it falls back to the ScopeCppSDK path above.

## Building the Desktop App

Example:

```powershell
dotnet build "D:\Projekte\MarkdownViewer\MarkdownViewer\MarkdownViewer.csproj" -c Release
```

## Building and Packaging the Total Commander Plugin

The recommended path is the build script:

```powershell
& "D:\Projekte\MarkdownViewer\tools\NativeBootstrapper\build-wlx.ps1"
```

The script performs the following steps:

1. runs `dotnet publish` for `MdViewerWlx`
2. compiles `wlx_bootstrapper.c` into `MdViewerWlx.wlx`
3. duplicates the binary to `MdViewerWlx.wlx64`
4. assembles all required files into `deploy\wlx`
5. creates an installable Total Commander ZIP package

Important build outputs:

- Deploy directory:

```text
D:\Projekte\MarkdownViewer\deploy\wlx
```

- Installation ZIP:

```text
D:\Projekte\MarkdownViewer\deploy\MdViewerWlx-<version>-tc.zip
```

The version number is read automatically from `tools\NativeBootstrapper\pluginst.inf`.

## GitHub Releases

GitHub Releases are built automatically from version tags.

Workflow file:

- `.github\workflows\release-wlx.yml`

Release flow:

1. update `tools\NativeBootstrapper\pluginst.inf`
2. set `version=` to the release version, for example `1.0`
3. create and push a matching Git tag, for example `v1.0`
4. GitHub Actions builds `deploy\MdViewerWlx-<version>-tc.zip`
5. the workflow publishes that ZIP as a GitHub Release asset

Important rule:

- the pushed Git tag must match the plugin version from `pluginst.inf`
- example: `version=1.0` requires tag `v1.0`

## WLX Deploy Contents

Typical contents of `deploy\wlx`:

- `MdViewerWlx.wlx`
- `MdViewerWlx.wlx64`
- `MdViewerWlx.dll`
- `MdViewerWlx.exe`
- `MdViewerWlx.deps.json`
- `MdViewerWlx.runtimeconfig.json`
- `MdViewer.Shared.dll`
- `Markdig.dll`
- `Microsoft.Web.WebView2.Core.dll`
- `Microsoft.Web.WebView2.Wpf.dll`
- `WebView2Loader.dll`
- `pluginst.inf`
- `runtimes\...`

## Installing in Total Commander

### Recommended: ZIP-based installation

1. Open Total Commander
2. Go to `Configuration -> Plugins -> Install plugin`
3. Select `D:\Projekte\MarkdownViewer\deploy\MdViewerWlx-<version>-tc.zip`
4. Total Commander reads `pluginst.inf` from the ZIP and installs the plugin automatically

### Alternative: Manual installation

1. Copy the contents of `deploy\wlx` into a plugin folder
2. Register the plugin in Total Commander or install it through `pluginst.inf`

Active plugin folder in the current development environment:

```text
C:\wincmd\plugins\wlx\wlx_mdviewer
```

Active Total Commander configuration file in the current development environment:

```text
C:\Users\ThomasLeclaire\AppData\Roaming\GHISLER\WINCMD.INI
```

The relevant entry is stored in the `[ListerPlugins]` section.

## Behavior in Total Commander

When opening a Markdown file with `F3`:

1. Total Commander loads `MdViewerWlx.wlx` or `MdViewerWlx.wlx64`
2. the native bootstrapper initializes the .NET runtime
3. the managed layer creates a WPF child window
4. an immediate text preview is shown first
5. the view then switches to the rendered HTML/WebView2 display

This behavior is intentional so the first load stays usable even when WebView2 startup is slow.

## Notes

- the project is Windows-only by design
- the WLX plugin is x64-focused
- the first WebView2 startup can be slower than later launches
- the native WLX build needs either an MSVC developer environment on `PATH` or the configured ScopeCppSDK fallback path
- the installation ZIP is intended for Total Commander and contains `pluginst.inf` at the ZIP root

## Important Files

- `README.md`
- `MarkdownViewer\MarkdownViewer.csproj`
- `MdViewer.Shared\MarkdownRenderService.cs`
- `MdViewer.Shared\FileLogger.cs`
- `MdViewerWlx\MdViewerWlx.csproj`
- `MdViewerWlx\WlxExports.cs`
- `MdViewerWlx\WlxHost.cs`
- `MdViewerWlx\WlxContentView.cs`
- `tools\NativeBootstrapper\wlx_bootstrapper.c`
- `tools\NativeBootstrapper\build-wlx.ps1`
- `tools\NativeBootstrapper\pluginst.inf`

## Development Notes

- for rendering changes, `MdViewer.Shared` is usually the main entry point
- for WLX loading issues, start with the native bootstrapper and `WlxExports`
- for display issues inside Total Commander Lister, inspect `WlxHost` and `WlxContentView`
- for testing production artifacts, use `deploy\wlx` or the versioned Total Commander ZIP rather than arbitrary files from intermediate `bin\` folders
