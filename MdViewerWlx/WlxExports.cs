using MdViewer.Shared;
using System.Runtime.InteropServices;
using System.Text;

namespace MdViewerWlx;

/// <summary>
/// Managed WLX export methods called by the NativeAOT bootstrapper
/// via the .NET hosting API (hostfxr + load_assembly_and_get_function_pointer).
///
/// Methods use [UnmanagedCallersOnly] so they can be invoked from native
/// code via raw function pointers without delegate marshaling.
///
/// NOTE: ListGetDetectString is handled natively in the bootstrapper
/// and is NOT defined here.
/// </summary>
public static class WlxExports
{
    private static readonly object _lock = new();
    private static readonly Dictionary<IntPtr, WlxHost> _instances = new();

    private static IntPtr CreateHost(IntPtr parentWin, string? fileToLoad)
    {
        var host = new WlxHost(parentWin, fileToLoad ?? string.Empty);
        IntPtr hwnd = host.Start();

        if (hwnd != IntPtr.Zero)
        {
            lock (_lock)
            {
                _instances[hwnd] = host;
            }
        }

        return hwnd;
    }

    // ──────────────────────────────────────────────
    //  ListLoad — Hauptfunktion: Datei laden & anzeigen
    // ──────────────────────────────────────────────

    /// <summary>
    /// Wird von TC aufgerufen, um eine Datei zur Ansicht zu laden.
    /// Erzeugt ein WPF-Fenster als Child des Parent-HWND und gibt
    /// dessen HWND zurück.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr ListLoad(IntPtr parentWin, IntPtr fileToLoadPtr, int showFlags)
    {
        string? fileToLoad = null;

        try
        {
            if (fileToLoadPtr != IntPtr.Zero)
                fileToLoad = Marshal.PtrToStringAnsi(fileToLoadPtr);
        }
        catch { /* ignorieren */ }

        try
        {
            return CreateHost(parentWin, fileToLoad);
        }
        catch (Exception ex)
        {
            FileLogger.Error("WLX ListLoad fehlgeschlagen", ex);
            return IntPtr.Zero;
        }
    }

    [UnmanagedCallersOnly]
    public static IntPtr ListLoadW(IntPtr parentWin, IntPtr fileToLoadPtr, int showFlags)
    {
        string? fileToLoad = null;

        try
        {
            if (fileToLoadPtr != IntPtr.Zero)
                fileToLoad = Marshal.PtrToStringUni(fileToLoadPtr);
        }
        catch { /* ignorieren */ }

        try
        {
            return CreateHost(parentWin, fileToLoad);
        }
        catch (Exception ex)
        {
            FileLogger.Error("WLX ListLoadW fehlgeschlagen", ex);
            return IntPtr.Zero;
        }
    }

    // ──────────────────────────────────────────────
    //  ListLoadNext — für Multi-Archive (selten)
    // ──────────────────────────────────────────────

    [UnmanagedCallersOnly]
    public static int ListLoadNext(IntPtr parentWin, IntPtr fileToLoadPtr, int showFlags)
    {
        // Nicht implementiert — nur eine Datei pro Aufruf
        return 0;
    }

    [UnmanagedCallersOnly]
    public static int ListLoadNextW(IntPtr parentWin, IntPtr fileToLoadPtr, int showFlags)
    {
        // Nicht implementiert — nur eine Datei pro Aufruf
        return 0;
    }

    // ──────────────────────────────────────────────
    //  ListCloseWindow — Aufräumen
    // ──────────────────────────────────────────────

    [UnmanagedCallersOnly]
    public static void ListCloseWindow(IntPtr listWin)
    {
        WlxHost? host = null;

        lock (_lock)
        {
            if (_instances.TryGetValue(listWin, out host))
            {
                _instances.Remove(listWin);
            }
        }

        if (host != null)
        {
            try
            {
                host.Stop();
            }
            catch (Exception ex)
            {
                FileLogger.Error("WLX ListCloseWindow Fehler", ex);
            }
        }
    }
}
