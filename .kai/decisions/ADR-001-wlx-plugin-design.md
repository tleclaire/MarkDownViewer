# ADR-001: Total Commander WLX Viewer-Plugin — Design

**Status:** Entwurf (noch nicht implementiert)
**Datum:** 2026-05-18
**Kontext:** MdViewer soll zusätzlich zur EXE als natives WLX-Plugin für Total Commander laufen.

---

## 1. Ziel

Ein TC-Plugin (`MdViewer.wlx` bzw. `MdViewerWlx.dll`), das Markdown-Dateien direkt
im TC-Lister-Fenster **eingebettet** anzeigt — ohne separaten Prozess.

**Vorteile gegenüber EXE-Ansatz:**
- Im Lister-Fenster eingebettet (kein separates Fenster)
- Pfeiltasten zum Blättern (vor/zurück) funktionieren nativ
- TC-interne Suche durchsucht den Inhalt
- Schneller (kein Prozess-Start pro Datei)

---

## 2. TC WLX API (Überblick)

TC definiert einen festen Satz an Exports in einer nativen DLL:

```c
// Minimale Implementierung
HWND __stdcall ListLoad(HWND ParentWin, char* FileToLoad, int ShowFlags);
void __stdcall ListCloseWindow(HWND ListWin);
void __stdcall ListGetDetectString(char* DetectString, int MaxLen);
void __stdcall ListSetDefaultParams(ListDefaultParamStruct* dps);
int  __stdcall ListNotificationHandler(HWND ListWin, int DWord);
int  __stdcall ListSearchText(HWND ListWin, char* SearchString, int SearchParameter);
```

**Schlüsselfunktion `ListLoad`:**
- TC übergibt Parent-HWND + Dateipfad
- Plugin erzeugt ein Child-Fenster (HWND) und gibt es zurück
- TC bettet es in sein Lister-Layout ein

---

## 3. Architektur (vorgeschlagen)

```
MdViewerWlx.dll (.NET C++/CLI)
  │
  ├── mdviewer_wlx.cpp/.h       — Native WLX-Exports (C++/CLI)
  ├── WlxHost.cs                 — HwndSource-Host + WPF-Dispatcher-Management
  ├── WlxViewerControl.xaml/  — WPF UserControl (enthält WebView2)
  │   └── WlxViewerControl.xaml.cs
  │
  └── Verweis auf:
      ├── MarkdownRenderService  — aus MdViewer.exe (shared project / NuGet-Local)
      ├── Markdig                — Markdown → HTML
      └── Microsoft.Web.WebView2.Wpf — Rendering Engine
```

---

## 4. Technische Details

### 4.1 Projekttyp: C++/CLI Class Library

- **Ziel-Framework:** `net10.0-windows` (C++/CLI)
- **Ausgabe:** `.dll` (native TC ladbar)
- **Build-Tool:** MSBuild via `dotnet build` (C++/CLI wird ab .NET 5 unterstützt)

Warum C++/CLI? Weil TC eine **native DLL** erwartet. C++/CLI kann:
- Native Exports (`__declspec(dllexport)`) bereitstellen
- Gleichzeitig .NET-Assemblies referenzieren und aufrufen

Alternative: **C# mit `DllExport`** (z.B. `DllExport` von 3F).
Vorteil: reines C#, kein C++ nötig. Nachteil: Export-Mechanismus ist weniger standardisiert,
bei TC-Update ggf. inkompatibel. **Empfehlung: C++/CLI** für maximale Kompatibilität.

### 4.2 WPF-Hosting in nativem HWND

Das Kernproblem: TC gibt ein natives `HWND` (ParentWin) vor, WPF braucht aber
einen eigenen Dispatcher und ein HwndSource-Objekt.

```
TC Lister-Fenster (HWND)
  └── Wlx-Container (HWND, via CreateWindow)
       └── HwndSource (WPF-Bridge)
            └── WlxViewerControl (WPF UserControl)
                 └── WebView2
```

**Ablauf `ListLoad`:**

1. TC ruft `ListLoad(parentHWND, filePath, flags)` auf
2. Wir erzeugen ein natives Child-Fenster via `CreateWindow()` als Platzhalter
3. Wir erstellen einen WPF-Dispatcher (falls nötig) und wrappen das Child-Fenster
   mit `HwndSource`
4. `HwndSource.RootVisual` wird auf die WPF `WlxViewerControl` gesetzt
5. Diese lädt die Datei über `MarkdownRenderService.RenderFileAsync(filePath)`
   und zeigt sie im WebView2 an
6. Wir geben das Child-HWND an TC zurück

**Dispatcher-Management:**
- WPF braucht einen STA-Thread mit Dispatcher
- `ListLoad` wird von TC auf einem beliebigen Thread aufgerufen
- Strategie: WPF-Dispatcher on demand starten oder via `Dispatcher.CurrentDispatcher`
  den des Main-Threads nutzen
- Einfachster Ansatz: `HwndSource` erzeugt automatisch einen Dispatcher auf
  dem aktuellen Thread (muss STA sein)

### 4.3 Mehrere Lister-Tabs (Instanzen)

TC kann mehrere Lister-Fenster gleichzeitig öffnen. Jeder `ListLoad`-Aufruf
erzeugt eine unabhängige Instanz. Wichtig:
- Jede Instanz bekommt ihren eigenen `HwndSource` + `WlxViewerControl`
- WebView2-Instanzen sind unabhängig (jede hat eigenen Speicher)
- Aber: eine `CoreWebView2Environment` kann global geteilt werden

### 4.4 Optionale WLX-Features

| Export-Funktion | Nutzen | Aufwand |
|----------------|--------|---------|
| `ListSearchText` | TC-Suche durchsucht Markdown-Inhalt | Mittel |
| `ListSearchDialog` | Eigenes Suchdialog-Fenster | Hoch |
| `ListPrint` | Drucken aus TC heraus | Niedrig (via CoreWebView2) |
| `ListNotificationHandler` | Benachrichtigungen von TC empfangen | Niedrig |
| `ListGetDetectString` | Dateierkennung ("EXT:MD" + "EXT:MARKDOWN") | Minimal |

---

## 5. Wiederverwendung bestehender Komponenten

| Komponente | Aktueller Ort | Verwendung im Plugin |
|-----------|--------------|---------------------|
| `MarkdownRenderService` | `Services/MarkdownRenderService.cs` | **Direkt übernehmbar** |
| CSS-Styles | in `MarkdownRenderService` | **Direkt übernehmbar** |
| WebView2-Init-Logik | `MainWindow.xaml.cs` | **Anpassen** (HwndSource statt Window) |
| FileLogger | `Services/FileLogger.cs` | **Direkt übernehmbar** |
| MainWindow.xaml | Komplette Fenster-Logik | **Nicht nutzbar** — muss durch UserControl ersetzt werden |
| App.xaml.cs | Globaler Exception-Handler | **Nicht nutzbar** — entfällt im DLL-Kontext |

### Option: Shared-Projekt-Struktur

Für maximale Wiederverwendung bietet sich an:

```
MarkdownViewer.sln
  ├── MdViewer.Shared/          — class library (MarkdownRenderService, FileLogger, CSS)
  ├── MdViewer/                 — WPF EXE (referenziert Shared)
  └── MdViewerWlx/              — C++/CLI WLX DLL (referenziert Shared)
```

Das Shared-Projekt enthält die render-Logik ohne UI-Komponenten.
EXE und Plugin referenzieren es gemeinsam.

---

## 6. Build & Deployment

### 6.1 Build-Prozess

```
dotnet build .\MdViewer.Shared\           → MdViewer.Shared.dll
dotnet build .\MdViewerWlx\              → MdViewerWlx.dll   /   MdViewerWlx.pdb
msbuild .\MdViewerWlx\                   → mit C++-Compiler
```

### 6.2 Abhängigkeiten im deploy-Verzeichnis

```
deploy\
  ├── MdViewer.exe                    (bestehend)
  ├── MdViewerWlx.dll                 (neu — WLX-Plugin)
  ├── MdViewer.Shared.dll
  ├── Markdig.dll
  ├── Microsoft.Web.WebView2.Core.dll
  ├── Microsoft.Web.WebView2.Wpf.dll
  ├── Microsoft.Web.WebView2.WinForms.dll
  └── runtimes\
```

TC erwartet `MdViewerWlx.dll` im Plugin-Verzeichnis oder im TC-Verzeichnis.
Per Symlink oder Kopie deployen.

### 6.3 TC-Konfiguration

```
[ListerPlugins]
; In wincmd.ini
MdViewer=%COMMANDER_PATH%\Plugins\wlx\MdViewerWlx\MdViewerWlx.dll
```

---

## 7. Risiken & offene Punkte

| Risiko | Auswirkung | Lösungsidee |
|--------|-----------|-------------|
| **WPF-Dispatcher in TC** | `ListLoad` wird auf unbekanntem Thread aufgerufen | HwndSource erzeugt Dispatcher implizit; ggf. `Thread` mit STA-Apartment starten |
| **WebView2-Init-Latenz** | Erster Aufruf dauert 1-3s | WebView2-Umgebung vorab initialisieren / cachen |
| **Mehrere Instanzen** | Speicherverbrauch bei vielen offenen Tabs | Pro-Tab-Speicher optimieren; Environment teilen |
| **C++/CLI mit .NET 10** | Mögliche Kompatibilitätsprobleme | Im Zweifel auf .NET 8 LTS ausweichen |

---

## 8. Nächste Schritte (wenn implementiert wird)

1. **Shared-Projekt auslagern:** `MdViewer.Shared` erstellen,
   `MarkdownRenderService` und `FileLogger` dorthin verschieben
2. **C++/CLI-Projekt anlegen:** `MdViewerWlx` als neues Projekt im Solution
3. **`ListLoad` implementieren:** HwndSource + WPF UserControl + WebView2
4. **`ListGetDetectString`:** Dateierkennung für `*.md`, `*.markdown`, `*.mdown`
5. **Testen:** Mit TC → F3 auf .md-Datei → Plugin wird geladen
6. **Optionale Features:** Suchfunktion, Drucken, Copy-to-Clipboard

---

## 9. Referenzen

- [Total Commander Plugin API (wlx.h)](https://www.ghisler.com/plugins.htm)
- [C++/CLI für .NET 10](https://learn.microsoft.com/cpp/dotnet/dotnet-programming-with-cpp-cli)
- [HwndSource – WPF in nativen Fenstern hosten](https://learn.microsoft.com/dotnet/desktop/wpf/advanced/walkthrough-hosting-a-wpf-control-in-win32)
- [DllExport für C# TC-Plugins (Alternative)](https://github.com/3F/DllExport)
