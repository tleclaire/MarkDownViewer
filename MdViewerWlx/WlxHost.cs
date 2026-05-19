using MdViewer.Shared;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace MdViewerWlx;

/// <summary>
/// Erzeugt ein HwndSource als Child des von TC bereitgestellten Parent-Fensters
/// und hostet darin den WPF-WebView2-Content.
///
/// WICHTIG: Das HwndSource wird auf DEM AUFRUFENDEN THREAD erstellt (TC's UI-Thread),
/// NICHT auf einem separaten Thread. Dies verhindert einen SendMessage-Deadlock:
/// CreateWindowEx sendet WM_PARENTNOTIFY via SendMessage an das Parent-Fenster.
/// Wäre der Parent-Thread blockiert (wartend auf einen separaten STA-Thread),
/// würde das SendMessage nie zurückkehren → Deadlock.
/// Da TC eine laufende Message-Pumpe hat und wir auf demselben Thread sind,
/// werden SendMessage-Aufrufe sofort und ohne Blockierung zugestellt.
/// </summary>
internal sealed class WlxHost : IDisposable
{
    private readonly IntPtr _parentHwnd;
    private readonly string _filePath;

    private HwndSource? _source;
    private DispatcherTimer? _initializeTimer;
    private bool _disposed;

    public WlxHost(IntPtr parentHwnd, string filePath)
    {
        _parentHwnd = parentHwnd;
        _filePath = filePath;
    }

    /// <summary>
    /// Erstellt das HwndSource auf dem aktuellen Thread (TC's UI-Thread).
    /// Das HWND des Child-Fensters wird sofort zurückgegeben.
    /// </summary>
    public IntPtr Start()
    {
        FileLogger.Info("WLX Host: Starte auf Thread " + Environment.CurrentManagedThreadId);

        // WPF-Dispatcher initialisieren (erzeugt ggf. einen neuen für diesen Thread)
        _ = Dispatcher.CurrentDispatcher;

        // Größe des TC-Parent-Fensters ermitteln
        GetClientRect(_parentHwnd, out var parentRect);
        int width = Math.Max(parentRect.Right - parentRect.Left, 400);
        int height = Math.Max(parentRect.Bottom - parentRect.Top, 200);

        var sourceParams = new HwndSourceParameters("MdViewerWlx")
        {
            ParentWindow = _parentHwnd,
            WindowStyle = WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
            Width = width,
            Height = height
        };

        _source = new HwndSource(sourceParams);
        FileLogger.Info("WLX Host: HwndSource erstellt, Handle=" + _source.Handle);

        // WPF-Content erstellen und anzeigen
        var content = new WlxContentView(_filePath);
        _source.RootVisual = content;

        // TC sometimes leaves the first lister instance behind its own main window.
        // Nudge focus/z-order back to the created child and its parent lister frame.
        SetFocus(_source.Handle);
        BringWindowToTop(_source.Handle);
        BringWindowToTop(_parentHwnd);

        // Im Erstaufruf kommt TC hier offenbar noch in einer empfindlichen Phase an.
        // Ein kurzer Dispatcher-Delay macht die Initialisierung stabil, ohne den HWND-Return zu blockieren.
        _initializeTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _initializeTimer.Tick += (_, _) =>
        {
            _initializeTimer?.Stop();
            _initializeTimer = null;
            _ = InitializeContentAsync(content);
        };
        _initializeTimer.Start();

        return _source.Handle;
    }

    /// <summary>
    /// Stoppt den Viewer und räumt auf.
    /// </summary>
    public void Stop()
    {
        _initializeTimer?.Stop();
        _initializeTimer = null;

        if (_source is { IsDisposed: false })
        {
            // Wir sind auf TC's Thread — einfach dispatchen
            _source.Dispose();
            _source = null;
        }
    }

    private async Task InitializeContentAsync(WlxContentView content)
    {
        try
        {
            FileLogger.Info("WLX Host: Starte WebView2-Initialisierung...");
            bool success = await content.InitializeAsync();
            if (success)
                FileLogger.Info("WLX Host: WebView2-Initialisierung abgeschlossen");
            else
                FileLogger.Warning("WLX Host: WebView2 blieb im Fehler-/Fallback-Zustand");
        }
        catch (Exception ex)
        {
            FileLogger.Error("WLX Host: WebView2-Initialisierung fehlgeschlagen", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    // ── Win32-Interop ──

    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPCHILDREN = 0x02000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool BringWindowToTop(IntPtr hWnd);
}
