using MdViewer.Shared;
using Microsoft.Web.WebView2.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace MarkdownViewer;

public partial class MainWindow : Window
{
    private string? _filePath;
    private FileSystemWatcher? _fileWatcher;
    private bool _autoReload = true;
    private bool _isFullScreen;
    private WindowStyle _previousStyle;
    private WindowState _previousState;
    private bool _webViewReady;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SetWindowIcon();
    }

    private void SetWindowIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute);
            Icon = new System.Windows.Media.Imaging.BitmapImage(uri);
        }
        catch
        {
            // Icon ist optional — ignorieren bei Fehlern
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // WebView2 initialisieren
        await InitializeWebViewAsync();
    }

    // ──────────────────────────────────────────────
    //  WebView2 Initialisierung
    // ──────────────────────────────────────────────

    private async Task InitializeWebViewAsync()
    {
        try
        {
            await InitializeWebViewCoreAsync();

            _webViewReady = true;

            // Datei aus Kommandozeile laden
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1)
            {
                await LoadFileAsync(args[1]);
            }
            else
            {
                ShowWelcomePage();
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("WebView2-Initialisierung fehlgeschlagen", ex);

            // WebView2-Fehler → WPF-Fallback anzeigen
            WebView.Visibility = Visibility.Collapsed;
            WebView2Fallback.Visibility = Visibility.Visible;
            ErrorDetailText.Text = FormatExceptionDetail(ex);
            StatusInfo.Content = "WebView2-Fehler";
        }
    }

    private async Task InitializeWebViewCoreAsync()
    {
        var exceptions = new List<Exception>();

        // Strategie 1: Mit eigenem User-Data-Ordner
        try
        {
            var userDataFolder = GetWebViewUserDataFolder();
            Directory.CreateDirectory(userDataFolder); // sicherstellen, dass er existiert

            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: userDataFolder);
            await WebView.EnsureCoreWebView2Async(env);

            ConfigureWebViewSettings();
            return;
        }
        catch (Exception ex)
        {
            exceptions.Add(ex);
        }

        // Strategie 2: Ohne eigenen User-Data-Ordner
        try
        {
            await WebView.EnsureCoreWebView2Async();
            ConfigureWebViewSettings();
            return;
        }
        catch (Exception ex)
        {
            exceptions.Add(ex);
        }

        // Strategie 3: Mit spezifischer Browser-EXE (falls WebView2 Runtime registriert ist)
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,  // null = automatisch suchen
                userDataFolder: Path.Combine(Path.GetTempPath(), "MdViewerWebView2"),
                options: null);
            await WebView.EnsureCoreWebView2Async(env);
            ConfigureWebViewSettings();
            return;
        }
        catch (Exception ex)
        {
            exceptions.Add(ex);
        }

        // Alle Strategien fehlgeschlagen
        throw new AggregateException(
            "WebView2 konnte nicht initialisiert werden. " +
            "Stellen Sie sicher, dass Microsoft Edge WebView2 Runtime installiert ist " +
            "(läuft mit Edge Chromium automatisch).",
            exceptions);
    }

    private void ConfigureWebViewSettings()
    {
        var settings = WebView.CoreWebView2.Settings;
        settings.IsScriptEnabled = true;
        settings.AreDevToolsEnabled = false;
        settings.IsWebMessageEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsZoomControlEnabled = true;
        settings.IsBuiltInErrorPageEnabled = true;
    }

    private static string FormatExceptionDetail(Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine(ex.Message);
        if (ex is AggregateException ae)
        {
            for (int i = 0; i < ae.InnerExceptions.Count; i++)
            {
                sb.AppendLine();
                sb.AppendLine($"--- Strategie {i + 1} fehlgeschlagen ---");
                sb.Append(ae.InnerExceptions[i].GetType().Name);
                sb.Append(": ");
                sb.AppendLine(ae.InnerExceptions[i].Message);
            }
        }
        else if (ex.InnerException != null)
        {
            sb.AppendLine();
            sb.AppendLine("--- Details ---");
            sb.Append(ex.InnerException.GetType().Name);
            sb.Append(": ");
            sb.Append(ex.InnerException.Message);
        }
        return sb.ToString();
    }

    private static string GetWebViewUserDataFolder()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "MdViewer");
    }

    // ──────────────────────────────────────────────
    //  Datei-Logik
    // ──────────────────────────────────────────────

    public async Task LoadFileAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            ShowWelcomePage();
            return;
        }

        // Pfad normalisieren (Total Commander kann %P%N übergeben)
        path = path.Trim('"');
        if (!Path.IsPathRooted(path))
            path = Path.GetFullPath(path);

        if (!File.Exists(path))
        {
            WebView.NavigateToString(MarkdownRenderService.Render($"# ⚠️ Datei nicht gefunden\n\n`{System.Net.WebUtility.HtmlEncode(path)}`"));
            Title = "MdViewer — Datei nicht gefunden";
            return;
        }

        _filePath = path;
        Title = $"MdViewer — {Path.GetFileName(_filePath)}";
        UpdateStatusBar();

        // Markdown rendern
        try
        {
            var html = await MarkdownRenderService.RenderFileAsync(_filePath);
            if (_webViewReady)
            {
                WebView.NavigateToString(html);
                StatusInfo.Content = "Gerendert";
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error($"Fehler beim Rendern von {_filePath}", ex);
            WebView.NavigateToString(MarkdownRenderService.Render($"# Fehler beim Rendern\n\n{System.Net.WebUtility.HtmlEncode(ex.Message)}"));
            StatusInfo.Content = "Fehler";
        }

        // FileWatcher für Auto-Reload
        SetupFileWatcher();
    }

    private void SetupFileWatcher()
    {
        StopFileWatcher();

        if (_filePath == null || !_autoReload) return;

        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            var file = Path.GetFileName(_filePath);

            if (dir == null) return;

            _fileWatcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _fileWatcher.Changed += OnFileChanged;
        }
        catch (Exception ex)
        {
            FileLogger.Warning("FileWatcher konnte nicht eingerichtet werden", ex);
        }
    }

    private void StopFileWatcher()
    {
        if (_fileWatcher != null)
        {
            _fileWatcher.Changed -= OnFileChanged;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }
    }

    private DateTime _lastFileChange;
    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: manche Editoren lösen mehrere Events aus
        var now = DateTime.UtcNow;
        if ((now - _lastFileChange).TotalMilliseconds < 500)
            return;
        _lastFileChange = now;

        // Kurz warten, bis der Schreibzugriff abgeschlossen ist
        await Task.Delay(200);

        await Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var html = await MarkdownRenderService.RenderFileAsync(_filePath!);
                if (_webViewReady)
                {
                    WebView.NavigateToString(html);
                    StatusInfo.Content = "Auto-Reload";
                    UpdateStatusBar();
                }
            }
            catch (Exception ex)
            {
                FileLogger.Warning($"Auto-Reload fehlgeschlagen für {_filePath}", ex);
            }
        });
    }

    // ──────────────────────────────────────────────
    //  Status Bar
    // ──────────────────────────────────────────────

    private void UpdateStatusBar()
    {
        if (_filePath == null) return;

        StatusFile.Content = _filePath;

        try
        {
            var fi = new FileInfo(_filePath);
            StatusSize.Content = FormatFileSize(fi.Length);

            // Encoding erkennen (BOM)
            using var sr = new StreamReader(_filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            sr.Read();
            StatusEncoding.Content = sr.CurrentEncoding.EncodingName;
        }
        catch (Exception ex)
        {
            FileLogger.Warning("StatusBar konnte nicht aktualisiert werden", ex);
            StatusSize.Content = "";
            StatusEncoding.Content = "";
        }
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB"
    };

    // ──────────────────────────────────────────────
    //  Welcome Page
    // ──────────────────────────────────────────────

    private void ShowWelcomePage()
    {
        var markdigVersion = typeof(Markdig.Markdown).Assembly.GetName().Version;
        var welcome = $$"""
# 🎉 Markdown Viewer — MdViewer

## Verwendung

Öffnen Sie eine Markdown-Datei per Kommandozeile:

```bash
MdViewer.exe "Pfad/zur/Datei.md"
```

Oder konfigurieren Sie MdViewer als **externen Viewer** im **Total Commander**:

1. Total Commander → **Konfiguration** → **Optionen** → **Ansicht/Bearbeiten**
2. Bei *«Dateien mit F3 anzeigen»* wählen Sie *«Assoziation nach Dateiendung»*
3. Fügen Sie eine Regel für `*.md` hinzu, die zu `MdViewer.exe` zeigt

## Tastenkürzel

| Taste | Funktion |
|-------|----------|
| `F5` | Neu laden |
| `F11` | Vollbild umschalten |
| `Esc` | Vollbild beenden / Schließen |
| `Ctrl+P` | Drucken |
| `Ctrl+Wheel` | Zoomen |
| `Drag & Drop` | `.md`-Datei öffnen |

## Unterstützt

✅ GitHub Flavored Markdown (GFM)  
✅ Syntax-Highlighting  
✅ Tabellen, Aufgabenlisten, Fußnoten  
✅ Emojis (:smile:) · Definitionen  
✅ Dark Mode (automatisch)  
✅ Auto-Reload bei Dateiänderungen  

---

*Rendering Engine: **Markdig {{markdigVersion}}** · Runtime: .NET 10 · WebView2*
""";

        WebView.NavigateToString(MarkdownRenderService.Render(welcome));
    }

    private void OnWebView2DownloadLinkClick(object sender, RequestNavigateEventArgs e)
    {
        OpenUrl(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private void OnWebView2DownloadClick(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://developer.microsoft.com/microsoft-edge/webview2/");
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    // ──────────────────────────────────────────────
    //  Event Handler
    // ──────────────────────────────────────────────

    private async void OnReloadClick(object sender, RoutedEventArgs e)
    {
        if (_filePath != null)
            await LoadFileAsync(_filePath);
    }

    private void OnPrintClick(object sender, RoutedEventArgs e)
    {
        if (_webViewReady)
        {
            WebView.CoreWebView2.ExecuteScriptAsync("window.print()");
        }
    }

    private void OnAutoReloadChanged(object sender, RoutedEventArgs e)
    {
        _autoReload = ChkAutoReload.IsChecked == true;
        if (_autoReload)
            SetupFileWatcher();
        else
            StopFileWatcher();
    }

    // ──────────────────────────────────────────────
    //  Tastatur
    // ──────────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F5:
                e.Handled = true;
                if (_filePath != null)
                    _ = LoadFileAsync(_filePath);
                break;

            case Key.F11:
                e.Handled = true;
                ToggleFullScreen();
                break;

            case Key.Escape:
                if (_isFullScreen)
                {
                    e.Handled = true;
                    ToggleFullScreen();
                }
                else
                {
                    Close();
                }
                break;

            case Key.P:
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    e.Handled = true;
                    OnPrintClick(this, new RoutedEventArgs());
                }
                break;
        }

        base.OnKeyDown(e);
    }

    // ──────────────────────────────────────────────
    //  Vollbild
    // ──────────────────────────────────────────────

    private void ToggleFullScreen()
    {
        _isFullScreen = !_isFullScreen;

        if (_isFullScreen)
        {
            _previousStyle = WindowStyle;
            _previousState = WindowState;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
        }
        else
        {
            WindowStyle = _previousStyle;
            WindowState = _previousState;
            ResizeMode = ResizeMode.CanResize;
            Topmost = false;
        }
    }

    // ──────────────────────────────────────────────
    //  Drag & Drop
    // ──────────────────────────────────────────────

    protected override void OnPreviewDragOver(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    protected override async void OnDrop(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Length > 0 &&
            files[0] is string path &&
            File.Exists(path))
        {
            await LoadFileAsync(path);
        }
    }

    // ──────────────────────────────────────────────
    //  Fenster-Aufräumen
    // ──────────────────────────────────────────────

    protected override void OnClosing(CancelEventArgs e)
    {
        StopFileWatcher();
        WebView?.Dispose();
        base.OnClosing(e);
    }
}
