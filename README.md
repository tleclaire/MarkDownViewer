# MarkdownViewer

MarkdownViewer ist ein Windows-Projekt fuer die Anzeige von Markdown-Dateien mit zwei Hauptzielen:

- eine eigenstaendige WPF-Desktop-Anwendung `MdViewer`
- ein Total-Commander-Lister-Plugin `MdViewerWlx` fuer `*.md` und verwandte Formate

Die Darstellung basiert auf `Markdig` fuer das Markdown-Rendering und `WebView2` fuer die HTML-Anzeige. Das WLX-Plugin verwendet einen nativen C-Bootstrapper, der die managed .NET-Komponenten ueber `hostfxr` laedt.

## Uebersicht

- Plattform: Windows x64
- UI-Technologie: WPF
- Runtime: .NET 10 Windows Desktop
- Markdown-Parser: `Markdig`
- HTML-Anzeige: `Microsoft.Web.WebView2`
- Total Commander Plugin-Typ: `WLX` (Lister Plugin)

## Features

- Rendert Markdown als HTML in einer WPF/WebView2-Oberflaeche
- Nutzt erweiterte Markdig-Features ueber `UseAdvancedExtensions()`
- Unterstuetzt Emoji und Smiley-Syntax
- Nutzt GitHub-inspiriertes CSS mit Hell/Dunkel-Anpassung per `prefers-color-scheme`
- Setzt einen `base`-Pfad fuer relative Inhalte aus Markdown-Dateien
- WLX-Plugin mit Unicode-Entry-Points fuer Total Commander x64
- Fallback-first-Verhalten im WLX-Plugin mit sofortiger Text-Vorschau
- anschliessendem Umschalten auf HTML/WebView2, sobald initialisiert

## Repository-Struktur

```text
MarkdownViewer/
|- MarkdownViewer/                     Desktop-App (MdViewer)
|- MdViewer.Shared/                   Gemeinsame Rendering- und Logging-Bausteine
|- MdViewerWlx/                       Managed WLX-Teil fuer Total Commander
|- tools/
|  |- NativeBootstrapper/
|  |  |- wlx_bootstrapper.c           Native WLX-Bootstrapper in C
|  |  |- build-wlx.ps1                Build-, Deploy- und Packaging-Skript
|  |  |- pluginst.inf                 TC-Installationsbeschreibung
|- deploy/
|  |- wlx/                            Zusammengestelltes Plugin-Deploy-Verzeichnis
|  |- MdViewerWlx-<version>-tc.zip    Installations-ZIP fuer Total Commander
|- README.md
```

Hinweis: Im aktuellen Stand gibt es keine `.sln` im Repository-Root. Die Builds laufen direkt ueber die einzelnen Projekte beziehungsweise ueber das PowerShell-Skript fuer das WLX-Paket.

## Projekte im Detail

### `MarkdownViewer`

Eigenstaendige WPF-Desktop-Anwendung mit folgendem Projektprofil:

- Projektdatei: `MarkdownViewer\MarkdownViewer.csproj`
- Ausgabe: `WinExe`
- Target Framework: `net10.0-windows`
- WPF aktiviert
- App-Icon: `MarkdownViewer\Resources\app.ico`

Die Anwendung verwendet `MdViewer.Shared` fuer Rendering und Logging.

### `MdViewer.Shared`

Gemeinsame Bibliothek fuer die Desktop-App und das WLX-Plugin.

Wichtige Dateien:

- `MdViewer.Shared\MarkdownRenderService.cs`
- `MdViewer.Shared\FileLogger.cs`

Aufgaben dieser Bibliothek:

- Markdown in vollstaendiges HTML-Dokument rendern
- GitHub-inspiriertes CSS bereitstellen
- Fehler- und Status-Logging in Dateien schreiben

### `MdViewerWlx`

Managed Teil des Total-Commander-Plugins.

- Projektdatei: `MdViewerWlx\MdViewerWlx.csproj`
- Ausgabe: `Exe`
- Target Framework: `net10.0-windows`
- Plattform: `x64`
- WPF aktiviert

Warum `Exe` statt `Dll`:

- Der native Bootstrapper initialisiert die CLR im App-Modus ueber `hostfxr_initialize_for_dotnet_command_line`
- dafuer wird `MdViewerWlx.exe` als Apphost benoetigt
- nur so werden die App-Kontexte und Abhaengigkeiten fuer WPF und WebView2 sauber geladen

Wichtige Dateien:

- `MdViewerWlx\WlxExports.cs`
- `MdViewerWlx\WlxHost.cs`
- `MdViewerWlx\WlxContentView.cs`
- `MdViewerWlx\Program.cs`

## WLX-Plugin-Architektur

Das Total-Commander-Plugin ist zweistufig aufgebaut.

### 1. Native Bootstrapper

Datei: `tools\NativeBootstrapper\wlx_bootstrapper.c`

Aufgaben:

- wird direkt von Total Commander als `.wlx` bzw. `.wlx64` geladen
- exportiert die WLX-Funktionen
- liefert `ListGetDetectString` nativ aus
- laedt `hostfxr.dll`
- initialisiert die .NET-Runtime
- laedt managed Export-Methoden aus `MdViewerWlx.dll`

Wichtige technische Punkte:

- verwendet `hdt=5` fuer `load_assembly_and_get_function_pointer`
- verwendet `hostfxr_initialize_for_dotnet_command_line`
- uebergibt `MdViewerWlx.exe` als `argv[0]`
- unterstuetzt ANSI- und Unicode-Varianten der WLX-API

Exportierte WLX-Funktionen:

- `ListGetDetectString`
- `ListGetDetectStringW`
- `ListLoad`
- `ListLoadW`
- `ListLoadNext`
- `ListLoadNextW`
- `ListCloseWindow`

Detect-String aktuell:

```text
ext="MD" | ext="MARKDOWN" | ext="MDOWN" | ext="RST"
```

### 2. Managed WLX-Teil

Dateien:

- `MdViewerWlx\WlxExports.cs`
- `MdViewerWlx\WlxHost.cs`
- `MdViewerWlx\WlxContentView.cs`

Aufgaben:

- nimmt WLX-Aufrufe aus dem nativen Teil entgegen
- erzeugt ein WPF-Child-Fenster innerhalb des von Total Commander gelieferten Parent-Fensters
- initialisiert WebView2 verzogert und robust
- zeigt bis dahin eine Text-Vorschau an

Besonderheiten des Hostings:

- `HwndSource` wird direkt auf dem aufrufenden TC-UI-Thread erzeugt
- das vermeidet Deadlocks beim Erzeugen des Child-Fensters
- die eigentliche WebView2-Initialisierung wird per `DispatcherTimer` leicht verzoegert gestartet
- Fokus und Z-Order werden aktiv korrigiert, damit die erste Lister-Instanz nicht hinter Total Commander verschwindet

## Rendering

Das Rendering erfolgt in `MdViewer.Shared\MarkdownRenderService.cs`.

Aktivierte Markdig-Pipeline:

- `UseAdvancedExtensions()`
- `UseEmojiAndSmiley()`
- `UseSoftlineBreakAsHardlineBreak()`

Zusatzverhalten:

- relative Pfade werden ueber ein `file:///`-`base`-Tag aufgeloest
- leere Dateien und Fehlerfaelle liefern ein vollstaendiges HTML-Dokument statt einer Exception
- CSS fuer Light- und Dark-Mode ist direkt eingebettet

## Logging

Datei: `MdViewer.Shared\FileLogger.cs`

Der Logger schreibt Log-Dateien in den `Log`-Unterordner des jeweiligen Programmordners:

```text
[Programmordner]\Log\log-YYYY-MM-DD.log
```

Beispiele:

- Desktop-App: neben `MdViewer.exe`
- WLX-Plugin: im Plugin-Ordner neben `MdViewerWlx.wlx` und `MdViewerWlx.exe`

Aktuell verbleibendes Logging ist produktionsnah gehalten und konzentriert sich auf Fehler- und Warnsituationen.

## Voraussetzungen

## Laufzeit

- Windows x64
- installierte .NET-Desktop-Runtime bzw. SDK passend zu `net10.0-windows`
- installierte WebView2 Runtime
- fuer Total Commander: x64-Version von TC empfohlen

## Build

- .NET 10 SDK
- ScopeCppSDK unter:

```text
C:\Program Files\Microsoft Visual Studio\18\Insiders\SDK\ScopeCppSDK\vc15
```

Das WLX-Build-Skript erwartet `cl.exe` genau an diesem Ort.

## Build der Desktop-App

Beispiel:

```powershell
dotnet build "D:\Projekte\MarkdownViewer\MarkdownViewer\MarkdownViewer.csproj" -c Release
```

## Build und Packaging des Total-Commander-Plugins

Der empfohlene Weg ist das Build-Skript:

```powershell
& "D:\Projekte\MarkdownViewer\tools\NativeBootstrapper\build-wlx.ps1"
```

Das Skript erledigt:

1. `dotnet publish` fuer `MdViewerWlx`
2. Kompilieren von `wlx_bootstrapper.c` zu `MdViewerWlx.wlx`
3. Duplizieren nach `MdViewerWlx.wlx64`
4. Zusammenstellen aller benoetigten Dateien in `deploy\wlx`
5. Erzeugen eines installierbaren Total-Commander-ZIP-Pakets

Wichtige Build-Ausgaben:

- Deploy-Ordner:

```text
D:\Projekte\MarkdownViewer\deploy\wlx
```

- Installations-ZIP:

```text
D:\Projekte\MarkdownViewer\deploy\MdViewerWlx-<version>-tc.zip
```

Die Versionsnummer wird automatisch aus `tools\NativeBootstrapper\pluginst.inf` gelesen.

## Inhalt des WLX-Deploys

Typischer Inhalt von `deploy\wlx`:

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

## Installation in Total Commander

### Empfohlen: Installation ueber ZIP

1. Total Commander oeffnen
2. `Configuration -> Plugins -> Install plugin`
3. `D:\Projekte\MarkdownViewer\deploy\MdViewerWlx-<version>-tc.zip` auswaehlen
4. Total Commander liest `pluginst.inf` aus dem ZIP und installiert das Plugin automatisch

### Alternative: Manuelle Installation

1. Inhalt von `deploy\wlx` in einen Plugin-Ordner kopieren
2. in Total Commander das Plugin registrieren oder ueber `pluginst.inf` installieren

Aktiver Plugin-Ordner in der aktuellen Entwicklungsumgebung:

```text
C:\wincmd\plugins\wlx\wlx_mdviewer
```

Aktive TC-Konfiguration in der aktuellen Entwicklungsumgebung:

```text
C:\Users\ThomasLeclaire\AppData\Roaming\GHISLER\WINCMD.INI
```

Der relevante Eintrag liegt unter `[ListerPlugins]`.

## Verhalten im Total Commander

Beim Oeffnen einer Markdown-Datei mit `F3`:

1. Total Commander laedt `MdViewerWlx.wlx` oder `MdViewerWlx.wlx64`
2. der native Bootstrapper initialisiert die .NET-Runtime
3. der managed Teil erstellt ein WPF-Child-Fenster
4. zunaechst erscheint eine sofort verfuegbare Text-Vorschau
5. danach wird auf die HTML/WebView2-Ansicht umgeschaltet

Dieses Verhalten ist absichtlich so umgesetzt, damit der erste Aufruf auch bei langsamer WebView2-Initialisierung benutzbar bleibt.

## Bekannte Hinweise

- Das Projekt ist klar auf Windows ausgelegt
- das WLX-Plugin ist auf x64 ausgelegt
- der erste WebView2-Start kann langsamer sein als Folgeaufrufe
- der Build des nativen WLX-Teils setzt das konfigurierte ScopeCppSDK voraus
- das Installations-ZIP ist fuer Total Commander gedacht und enthaelt `pluginst.inf` im ZIP-Root

## Wichtige Dateien

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

## Entwicklungshinweise

- Fuer normale Aenderungen am Rendering ist meist `MdViewer.Shared` der zentrale Einstiegspunkt
- fuer WLX-Ladeprobleme sind nativer Bootstrapper und `WlxExports` die ersten Anlaufstellen
- fuer Anzeigeprobleme im TC-Lister sind `WlxHost` und `WlxContentView` relevant
- die Produktionsartefakte fuer Tests sollten aus `deploy\wlx` oder dem versionierten TC-ZIP genommen werden, nicht aus beliebigen Zwischenordnern unter `bin\`
