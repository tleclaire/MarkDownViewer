// MdViewerWlx Native Bootstrapper — NativeAOT C#
//
// Produces a native DLL (renamed to .wlx) that Total Commander loads.
// Handles ListGetDetectString natively, forwards other WLX calls
// to the managed MdViewerWlx assembly via the .NET hosting API (hostfxr).

using System.Runtime.InteropServices;
using System.Text;

namespace MdViewerWlx.Bootstrap;

/// <summary>
/// Minimal inline logger for bootstrapper diagnostic output.
/// Writes to &lt;bootstrapper-dir&gt;\Log\bootstrap-&lt;date&gt;.log
/// </summary>
internal static class BootstrapLogger
{
    private static readonly string LogDir;
    private static readonly object LockObj = new();

    static BootstrapLogger()
    {
        string baseDir = AppContext.BaseDirectory;
        LogDir = Path.Combine(baseDir, "Log");
        try { Directory.CreateDirectory(LogDir); } catch { }
    }

    internal static void Error(string msg, Exception? ex = null)
    {
        Write("ERROR", msg, ex);
    }

    internal static void Info(string msg)
    {
        Write("INFO ", msg);
    }

    private static void Write(string level, string msg, Exception? ex = null)
    {
        try
        {
            var now = DateTime.Now;
            string path = Path.Combine(LogDir, $"bootstrap-{now:yyyy-MM-dd}.log");
            var sb = new StringBuilder();
            sb.Append($"[{now:yyyy-MM-dd HH:mm:ss}] [{level}] ");
            sb.AppendLine(msg);
            if (ex != null)
            {
                sb.AppendLine($"  Type: {ex.GetType().FullName}");
                sb.AppendLine($"  Msg:  {ex.Message}");
                if (ex.StackTrace != null)
                    sb.AppendLine($"  ST:   {ex.StackTrace}");
            }
            lock (LockObj)
            {
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
        }
        catch { }
    }
}

/// <summary>
/// Thread-safe manager for .NET runtime + managed assembly loading via hostfxr.
/// </summary>
internal static partial class RuntimeManager
{
    private static IntPtr _hostfxrHandle;
    private static IntPtr _hostContextHandle;
    private static IntPtr _loadAsmPtr;
    private static readonly object _initLock = new();

    // Cached managed method function pointers
    private static IntPtr _listLoadPtr;
    private static IntPtr _listLoadNextPtr;
    private static IntPtr _listCloseWindowPtr;

    // ── hostfxr delegate types ──
    private delegate int HostfxrInitializeForRuntimeConfig(
        string runtimeConfigPath, IntPtr parameters, out IntPtr hostContextHandle);

    private delegate int HostfxrGetRuntimeDelegate(
        IntPtr hostContextHandle, int delegateType, out IntPtr delegatePtr);

    private delegate int HostfxrClose(IntPtr hostContextHandle);

    private delegate int LoadAssemblyAndGetFunctionPointer(
        string assemblyPath, string typeName, string methodName,
        string? delegateTypeName, IntPtr reserved, out IntPtr delegatePtr);

    // ── Kernel32 P/Invokes ──
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr LoadLibraryW(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeLibrary(IntPtr hModule);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetEnvironmentVariableW(
        string lpName, [Out] char[] lpBuffer, uint nSize);

    /// <summary>
    /// Path to the directory containing this bootstrapper DLL.
    /// </summary>
    internal static string BootstrapperDirectory =>
        AppContext.BaseDirectory;

    /// <summary>
    /// Ensure the .NET runtime is loaded and managed methods resolved.
    /// Thread-safe; only the first caller initializes.
    /// Returns 0 on success, negative on failure.
    /// </summary>
    internal static int EnsureManagedExportsLoaded()
    {
        if (_listLoadPtr != IntPtr.Zero) return 0;

        lock (_initLock)
        {
            if (_listLoadPtr != IntPtr.Zero) return 0;

            try
            {
                int rc = LoadHostFxr();
                if (rc != 0) return rc;

                // Get hostfxr function pointers
                var initFn = GetDelegate<HostfxrInitializeForRuntimeConfig>(
                    "hostfxr_initialize_for_runtime_config");
                var getDelegateFn = GetDelegate<HostfxrGetRuntimeDelegate>(
                    "hostfxr_get_runtime_delegate");
                var closeFn = GetDelegate<HostfxrClose>("hostfxr_close");

                if (initFn == null || getDelegateFn == null || closeFn == null)
                    return -2;

                // Init runtime via runtimeconfig.json
                string configPath = Path.Combine(
                    BootstrapperDirectory, "MdViewerWlx.runtimeconfig.json");

                if (!File.Exists(configPath))
                {
                    BootstrapLogger.Error($"Runtime config not found: {configPath}");
                    return -3;
                }

                rc = initFn(configPath, IntPtr.Zero, out _hostContextHandle);
                // rc == 0: first init (no CLR loaded yet)
                // rc == 1: Success_HostAlreadyInitialized (CLR already loaded)
                // Both are OK
                if (rc != 0 && rc != 1)
                {
                    BootstrapLogger.Error($"hostfxr_initialize_for_runtime_config: {rc}");
                    return -4;
                }

                // Get load_assembly_and_get_function_pointer (hdt = 6)
                rc = getDelegateFn(_hostContextHandle, 6, out _loadAsmPtr);
                if (rc != 0 || _loadAsmPtr == IntPtr.Zero)
                {
                    closeFn(_hostContextHandle);
                    _hostContextHandle = IntPtr.Zero;
                    BootstrapLogger.Error("hostfxr_get_runtime_delegate(hdt=6) failed");
                    return -5;
                }

                var loadFn = Marshal.GetDelegateForFunctionPointer<LoadAssemblyAndGetFunctionPointer>(_loadAsmPtr);
                if (loadFn == null) return -6;

                string assemblyPath = Path.Combine(BootstrapperDirectory, "MdViewerWlx.dll");
                string typeName = "MdViewerWlx.WlxExports, MdViewerWlx";

                // Resolve ListLoad — main entry point, required
                rc = loadFn(assemblyPath, typeName, "ListLoad", null, IntPtr.Zero, out _listLoadPtr);
                if (rc != 0 || _listLoadPtr == IntPtr.Zero)
                {
                    BootstrapLogger.Error($"Load ListLoad failed: {rc}");
                    return -7;
                }

                // Resolve ListLoadNext / ListCloseWindow — optional (non-fatal if missing)
                loadFn(assemblyPath, typeName, "ListLoadNext", null, IntPtr.Zero, out _listLoadNextPtr);
                loadFn(assemblyPath, typeName, "ListCloseWindow", null, IntPtr.Zero, out _listCloseWindowPtr);

                BootstrapLogger.Info("Runtime + managed exports loaded successfully");
                return 0;
            }
            catch (Exception ex)
            {
                BootstrapLogger.Error("EnsureManagedExportsLoaded failed", ex);
                return -99;
            }
        }
    }

    // ── Private helpers ──

    private static int LoadHostFxr()
    {
        // Check DOTNET_ROOT env var first
        char[] buf = new char[260];
        uint len = GetEnvironmentVariableW("DOTNET_ROOT", buf, (uint)buf.Length);
        string dotnetRoot = len > 0 && len < buf.Length
            ? new string(buf, 0, (int)len)
            : @"C:\Program Files\dotnet";

        string fxrDir = Path.Combine(dotnetRoot, "host", "fxr");
        if (!Directory.Exists(fxrDir))
        {
            BootstrapLogger.Error($"hostfxr dir not found: {fxrDir}");
            return -10;
        }

        // Find highest semver directory
        string? bestVersion = null;
        foreach (string dir in Directory.GetDirectories(fxrDir))
        {
            string name = Path.GetFileName(dir);
            if (string.Compare(name, bestVersion, StringComparison.OrdinalIgnoreCase) > 0)
                bestVersion = name;
        }

        if (bestVersion == null)
        {
            BootstrapLogger.Error($"No hostfxr version in {fxrDir}");
            return -11;
        }

        string fxrPath = Path.Combine(fxrDir, bestVersion, "hostfxr.dll");
        if (!File.Exists(fxrPath))
        {
            BootstrapLogger.Error($"hostfxr.dll not found: {fxrPath}");
            return -12;
        }

        _hostfxrHandle = LoadLibraryW(fxrPath);
        if (_hostfxrHandle == IntPtr.Zero)
        {
            BootstrapLogger.Error($"LoadLibrary(hostfxr.dll): {Marshal.GetLastWin32Error()}");
            return -13;
        }

        BootstrapLogger.Info($"Loaded hostfxr: {fxrPath}");
        return 0;
    }

    private static T? GetDelegate<T>(string name) where T : class
    {
        IntPtr ptr = GetProcAddress(_hostfxrHandle, name);
        return ptr != IntPtr.Zero
            ? Marshal.GetDelegateForFunctionPointer<T>(ptr)
            : null;
    }

    internal static void Shutdown()
    {
        if (_hostContextHandle != IntPtr.Zero)
        {
            var close = GetDelegate<HostfxrClose>("hostfxr_close");
            close?.Invoke(_hostContextHandle);
            _hostContextHandle = IntPtr.Zero;
        }
        if (_hostfxrHandle != IntPtr.Zero)
        {
            FreeLibrary(_hostfxrHandle);
            _hostfxrHandle = IntPtr.Zero;
        }
        BootstrapLogger.Info("Runtime shut down");
    }

    // ── Managed method call forwarding ──

    internal static unsafe IntPtr CallListLoad(IntPtr parentWin, IntPtr fileToLoad, int showFlags)
    {
        if (_listLoadPtr == IntPtr.Zero) return IntPtr.Zero;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr>)_listLoadPtr;
        return fn(parentWin, fileToLoad, showFlags);
    }

    internal static unsafe int CallListLoadNext(IntPtr parentWin, IntPtr fileToLoad, int showFlags)
    {
        if (_listLoadNextPtr == IntPtr.Zero) return 0;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int>)_listLoadNextPtr;
        return fn(parentWin, fileToLoad, showFlags);
    }

    internal static unsafe void CallListCloseWindow(IntPtr listWin)
    {
        if (_listCloseWindowPtr == IntPtr.Zero) return;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, void>)_listCloseWindowPtr;
        fn(listWin);
    }
}

// ──────────────────────────────────────────────
//  WLX Native Exports (exported from native DLL)
// ──────────────────────────────────────────────

public static unsafe class BootstrapperExports
{
    /// <summary>
    /// Returns the detect string. Implemented natively — no .NET runtime needed.
    /// TC signature: int __stdcall ListGetDetectString(char* detectString, int maxLen);
    /// </summary>
    [UnmanagedCallersOnly]
    public static int ListGetDetectString(IntPtr detectString, int maxLen)
    {
        const string detect =
            "EXTENSION=\"MD\" | EXTENSION=\"MARKDOWN\" | EXTENSION=\"MDOWN\" | EXTENSION=\"RST\"";

        byte[] bytes = Encoding.ASCII.GetBytes(detect);
        int len = Math.Min(bytes.Length, maxLen - 1);

        try
        {
            Marshal.Copy(bytes, 0, detectString, len);
            Marshal.WriteByte(detectString, len, 0);
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Opens a file for viewing. Lazy-loads the .NET runtime if needed.
    /// TC signature: HWND __stdcall ListLoad(HWND parentWin, char* fileToLoad, int showFlags);
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr ListLoad(IntPtr parentWin, IntPtr fileToLoad, int showFlags)
    {
        int rc = RuntimeManager.EnsureManagedExportsLoaded();
        if (rc != 0)
        {
            BootstrapLogger.Error($"ListLoad: runtime init failed ({rc})");
            return IntPtr.Zero;
        }
        return RuntimeManager.CallListLoad(parentWin, fileToLoad, showFlags);
    }

    /// <summary>
    /// Loads next file in archive (not implemented).
    /// TC signature: int __stdcall ListLoadNext(HWND parentWin, char* fileToLoad, int showFlags);
    /// </summary>
    [UnmanagedCallersOnly]
    public static int ListLoadNext(IntPtr parentWin, IntPtr fileToLoad, int showFlags)
        => RuntimeManager.CallListLoadNext(parentWin, fileToLoad, showFlags);

    /// <summary>
    /// Closes the viewer window.
    /// TC signature: void __stdcall ListCloseWindow(HWND listWin);
    /// </summary>
    [UnmanagedCallersOnly]
    public static void ListCloseWindow(IntPtr listWin)
        => RuntimeManager.CallListCloseWindow(listWin);
}
