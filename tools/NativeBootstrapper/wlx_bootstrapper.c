// MdViewerWlx Native Bootstrapper (C)
//
// Native DLL loaded by Total Commander as a WLX plugin.
// Handles ListGetDetectString natively, forwards other WLX calls
// to the managed .NET 10 assembly via hostfxr (CLR hosting API).
//
// Build (ScopeCppSDK):
//   cl.exe /I"SDK\include\um" /I"SDK\include\shared" /I"SDK\include\ucrt"
//         /LD /Fe:MdViewerWlx.wlx wlx_bootstrapper.c
//         /link /LIBPATH:"SDK\lib" /LIBPATH:"VC\lib"
//         kernel32.lib user32.lib

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdlib.h>
#include <stdio.h>

// ---- DLL module handle (stored from DllMain) ----
static HMODULE g_hinstDLL = NULL;

// ---- Official delegate types from hostfxr.h ----
//   hdt_load_assembly_and_get_function_pointer = 5  (NOT 6!)
// Must use hdt=5 or the parameter signature is completely different
#define HDT_LOAD_AND_GET 5

// Per coreclr_delegates.h, delegate CALLTYPE is __stdcall on Win32
#define CORECLR_DELEGATE_CALLTYPE __stdcall

// For [UnmanagedCallersOnly] methods, pass UNMANAGEDCALLERSONLY_METHOD
// as delegate_type_name (const wchar_t*)-1
#define UNMANAGEDCALLERSONLY_METHOD ((const wchar_t*)-1)

// ---- hostfxr API function pointer types ----
typedef int32_t(__cdecl* hostfxr_initialize_for_dotnet_command_line_fn)(
    int argc, const wchar_t* argv[],
    void* parameters,
    void** host_context_handle);

typedef int32_t(__cdecl* hostfxr_get_runtime_delegate_fn)(
    void* host_context_handle, int32_t delegate_type,
    void** delegate);

typedef int32_t(__cdecl* hostfxr_close_fn)(
    void* host_context_handle);

typedef int32_t(CORECLR_DELEGATE_CALLTYPE* load_assembly_and_get_function_pointer_fn)(
    const wchar_t* assembly_path,
    const wchar_t* type_name,
    const wchar_t* method_name,
    const wchar_t* delegate_type_name,
    void* reserved,
    void** delegate);

// Managed method function pointer types (TC uses __stdcall convention)
typedef intptr_t(__stdcall* managed_list_load_fn)(
    void* parentWin, const char* fileToLoad, int32_t showFlags);

typedef intptr_t(__stdcall* managed_list_load_w_fn)(
    void* parentWin, const wchar_t* fileToLoad, int32_t showFlags);

typedef int32_t(__stdcall* managed_list_load_next_fn)(
    void* parentWin, const char* fileToLoad, int32_t showFlags);

typedef int32_t(__stdcall* managed_list_load_next_w_fn)(
    void* parentWin, const wchar_t* fileToLoad, int32_t showFlags);

typedef void(__stdcall* managed_list_close_window_fn)(
    void* listWin);

// ---- Globals ----
static HMODULE g_hostfxr = NULL;
static void* g_host_context = NULL;
static load_assembly_and_get_function_pointer_fn g_load_asm = NULL;
static int g_runtime_loaded = 0;

// Cached managed function pointers
static managed_list_load_fn g_list_load = NULL;
static managed_list_load_w_fn g_list_load_w = NULL;
static managed_list_load_next_fn g_list_load_next = NULL;
static managed_list_load_next_w_fn g_list_load_next_w = NULL;
static managed_list_close_window_fn g_list_close = NULL;

static int GetMyDir(wchar_t* buf, size_t bufsz);

// ---- Helper: get directory of this DLL ----
static int GetMyDir(wchar_t* buf, size_t bufsz)
{
    wchar_t path[MAX_PATH];
    DWORD len = GetModuleFileNameW(g_hinstDLL, path, MAX_PATH);
    if (len == 0 || len >= MAX_PATH) return -1;

    // Remove DLL name, keep trailing backslash
    wchar_t* slash = wcsrchr(path, L'\\');
    if (!slash) return -1;
    *(slash + 1) = L'\0';

    wcsncpy_s(buf, bufsz, path, _TRUNCATE);
    return 0;
}

// ---- Semver comparison (supports major.minor.patch) ----
static int CompareSemver(const wchar_t* a, const wchar_t* b)
{
    int ma = 0, na = 0, pa = 0;
    int mb = 0, nb = 0, pb = 0;
    swscanf_s(a, L"%d.%d.%d", &ma, &na, &pa);
    swscanf_s(b, L"%d.%d.%d", &mb, &nb, &pb);
    if (ma != mb) return ma - mb;
    if (na != nb) return na - nb;
    return pa - pb;
}

// ---- Helper: find hostfxr.dll ----
static HMODULE LoadHostFxr(void)
{
    wchar_t dotnet_root[MAX_PATH];
    DWORD len = GetEnvironmentVariableW(L"DOTNET_ROOT", dotnet_root, MAX_PATH);
    if (len == 0 || len >= MAX_PATH)
        wcscpy_s(dotnet_root, MAX_PATH, L"C:\\Program Files\\dotnet");

    // Look for the highest version in dotnet\host\fxr
    wchar_t fxr_dir[MAX_PATH];
    swprintf_s(fxr_dir, MAX_PATH, L"%s\\host\\fxr", dotnet_root);

    wchar_t best_version[MAX_PATH] = L"";
    wchar_t search_path[MAX_PATH];
    swprintf_s(search_path, MAX_PATH, L"%s\\*", fxr_dir);

    WIN32_FIND_DATAW fd;
    HANDLE hFind = FindFirstFileW(search_path, &fd);
    if (hFind == INVALID_HANDLE_VALUE) return NULL;

    do {
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
        {
            if (wcscmp(fd.cFileName, L".") == 0 || wcscmp(fd.cFileName, L"..") == 0)
                continue;
            // Semantic version comparison (works across major version boundaries)
            if (best_version[0] == L'\0' || CompareSemver(fd.cFileName, best_version) > 0)
                wcsncpy_s(best_version, MAX_PATH, fd.cFileName, _TRUNCATE);
        }
    } while (FindNextFileW(hFind, &fd));
    FindClose(hFind);

    if (best_version[0] == L'\0') return NULL;

    wchar_t fxr_path[MAX_PATH];
    swprintf_s(fxr_path, MAX_PATH, L"%s\\%s\\hostfxr.dll", fxr_dir, best_version);

    return LoadLibraryW(fxr_path);
}

// ---- Load the .NET runtime ----
static int EnsureRuntimeLoaded(void)
{
    if (g_runtime_loaded) return 0;

    g_hostfxr = LoadHostFxr();
    if (!g_hostfxr) return -1;

    hostfxr_initialize_for_dotnet_command_line_fn init = (hostfxr_initialize_for_dotnet_command_line_fn)
        GetProcAddress(g_hostfxr, "hostfxr_initialize_for_dotnet_command_line");
    hostfxr_get_runtime_delegate_fn get_delegate = (hostfxr_get_runtime_delegate_fn)
        GetProcAddress(g_hostfxr, "hostfxr_get_runtime_delegate");
    hostfxr_close_fn close = (hostfxr_close_fn)
        GetProcAddress(g_hostfxr, "hostfxr_close");

    if (!init || !get_delegate || !close) { FreeLibrary(g_hostfxr); g_hostfxr = NULL; return -2; }

    wchar_t my_dir[MAX_PATH];
    if (GetMyDir(my_dir, MAX_PATH) != 0) return -3;

    // Initialize in APP mode via hostfxr_initialize_for_dotnet_command_line.
    // argv[0] = the managed DLL path. hostfxr will find the corresponding
    // .runtimeconfig.json and .deps.json in the same directory.
    // APP mode loads FULL deps (including WPF, WebView2).
    // This NO LONGER uses hostfxr_initialize_for_runtime_config (component mode)
    // which skips app deps.json entirely.
    wchar_t app_exe[MAX_PATH];
    swprintf_s(app_exe, MAX_PATH, L"%sMdViewerWlx.exe", my_dir);
    const wchar_t* argv[] = { app_exe };

    int rc = init(1, argv, NULL, &g_host_context);
    if (rc != 0) {
        fprintf(stderr, "hostfxr_initialize_for_dotnet_command_line returned: %d (0x%x)\n", rc, rc);
        FreeLibrary(g_hostfxr); g_hostfxr = NULL; return -4;
    }
    fprintf(stderr, "DBG: hostfxr init OK\n");

    // CRITICAL: hdt=5 (NOT 6!) for load_assembly_and_get_function_pointer
    // hdt=6 is get_function_pointer which has a DIFFERENT signature (no assembly_path)
    rc = get_delegate(g_host_context, HDT_LOAD_AND_GET, (void**)&g_load_asm);
    if (rc != 0 || !g_load_asm) {
        fprintf(stderr, "DBG: get_delegate(hdt=5) FAILED rc=%d ptr=%p\n", rc, (void*)g_load_asm);
        close(g_host_context); g_host_context = NULL;
        FreeLibrary(g_hostfxr); g_hostfxr = NULL; return -5;
    }
    fprintf(stderr, "DBG: get_delegate(hdt=5) OK\n");

    g_runtime_loaded = 1;
    return 0;
}

// ---- Load managed function pointers ----
static int EnsureManagedMethodsLoaded(void)
{
    if (g_list_load) { fprintf(stderr, "DBG: managed methods already loaded\n"); return 0; }

    int rc = EnsureRuntimeLoaded();
    if (rc != 0) { fprintf(stderr, "DBG: EnsureRuntimeLoaded failed rc=%d\n", rc); return rc; }

    wchar_t my_dir[MAX_PATH];
    if (GetMyDir(my_dir, MAX_PATH) != 0) { fprintf(stderr, "DBG: GetMyDir failed\n"); return -3; }

    wchar_t assembly_path[MAX_PATH];
    swprintf_s(assembly_path, MAX_PATH, L"%sMdViewerWlx.dll", my_dir);

    wchar_t* type_name = L"MdViewerWlx.WlxExports, MdViewerWlx";
    fprintf(stderr, "DBG: loading assembly=%ls type=%ls\n", assembly_path, type_name);

    // Use UNMANAGEDCALLERSONLY_METHOD for [UnmanagedCallersOnly] managed methods.
    // This returns a native function pointer matching the method's exact signature.
    int r1 = g_load_asm(assembly_path, type_name, L"ListLoad", UNMANAGEDCALLERSONLY_METHOD, NULL, (void**)&g_list_load);
    fprintf(stderr, "DBG: ListLoad load_asm rc=%d ptr=%p\n", r1, (void*)g_list_load);
    
    int r2 = g_load_asm(assembly_path, type_name, L"ListLoadW", UNMANAGEDCALLERSONLY_METHOD, NULL, (void**)&g_list_load_w);
    fprintf(stderr, "DBG: ListLoadW load_asm rc=%d ptr=%p\n", r2, (void*)g_list_load_w);

    int r3 = g_load_asm(assembly_path, type_name, L"ListLoadNext", UNMANAGEDCALLERSONLY_METHOD, NULL, (void**)&g_list_load_next);
    fprintf(stderr, "DBG: ListLoadNext load_asm rc=%d ptr=%p\n", r3, (void*)g_list_load_next);

    int r4 = g_load_asm(assembly_path, type_name, L"ListLoadNextW", UNMANAGEDCALLERSONLY_METHOD, NULL, (void**)&g_list_load_next_w);
    fprintf(stderr, "DBG: ListLoadNextW load_asm rc=%d ptr=%p\n", r4, (void*)g_list_load_next_w);
    
    int r5 = g_load_asm(assembly_path, type_name, L"ListCloseWindow", UNMANAGEDCALLERSONLY_METHOD, NULL, (void**)&g_list_close);
    fprintf(stderr, "DBG: ListCloseWindow load_asm rc=%d ptr=%p\n", r5, (void*)g_list_close);

    if (!g_list_load) { fprintf(stderr, "DBG: ListLoad ptr is NULL - returning -6\n"); return -6; }
    fprintf(stderr, "DBG: All managed methods loaded successfully\n");
    return 0;
}

// ========== WLX Native Exports ==========

__declspec(dllexport) void __stdcall ListGetDetectString(void* detectString, int32_t maxLen)
{
    const char* detect = "ext=\"MD\" | ext=\"MARKDOWN\" | ext=\"MDOWN\" | ext=\"RST\"";
    size_t slen = strlen(detect);
    int32_t len = (int32_t)(slen < (size_t)(maxLen - 1) ? slen : (size_t)(maxLen - 1));

    memcpy(detectString, detect, (size_t)len);
    ((char*)detectString)[len] = '\0';
}

__declspec(dllexport) void __stdcall ListGetDetectStringW(void* detectString, int32_t maxLen)
{
    const wchar_t* detect = L"ext=\"MD\" | ext=\"MARKDOWN\" | ext=\"MDOWN\" | ext=\"RST\"";
    size_t slen = wcslen(detect);
    int32_t len = (int32_t)(slen < (size_t)(maxLen - 1) ? slen : (size_t)(maxLen - 1));

    memcpy(detectString, detect, (size_t)len * sizeof(wchar_t));
    ((wchar_t*)detectString)[len] = L'\0';
}

__declspec(dllexport) void* __stdcall ListLoad(
    void* parentWin, void* fileToLoad, int32_t showFlags)
{
    if (EnsureManagedMethodsLoaded() != 0) return NULL;
    return (void*)g_list_load(parentWin, (const char*)fileToLoad, showFlags);
}

__declspec(dllexport) void* __stdcall ListLoadW(
    void* parentWin, void* fileToLoad, int32_t showFlags)
{
    if (EnsureManagedMethodsLoaded() != 0) return NULL;
    if (g_list_load_w) return (void*)g_list_load_w(parentWin, (const wchar_t*)fileToLoad, showFlags);
    return (void*)g_list_load(parentWin, (const char*)fileToLoad, showFlags);
}

__declspec(dllexport) int32_t __stdcall ListLoadNext(
    void* parentWin, void* fileToLoad, int32_t showFlags)
{
    if (!g_list_load_next) return 0;
    return g_list_load_next(parentWin, (const char*)fileToLoad, showFlags);
}

__declspec(dllexport) int32_t __stdcall ListLoadNextW(
    void* parentWin, void* fileToLoad, int32_t showFlags)
{
    if (g_list_load_next_w) return g_list_load_next_w(parentWin, (const wchar_t*)fileToLoad, showFlags);
    if (!g_list_load_next) return 0;
    return g_list_load_next(parentWin, (const char*)fileToLoad, showFlags);
}

__declspec(dllexport) void __stdcall ListCloseWindow(void* listWin)
{
    if (g_list_close) g_list_close(listWin);
}

// ---- DllMain ----
BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD fdwReason, LPVOID lpvReserved)
{
    (void)lpvReserved;

    if (fdwReason == DLL_PROCESS_ATTACH)
    {
        g_hinstDLL = hinstDLL;
        DisableThreadLibraryCalls(hinstDLL);
    }
    else if (fdwReason == DLL_PROCESS_DETACH)
    {
        if (g_host_context) {
            hostfxr_close_fn close = (hostfxr_close_fn)
                GetProcAddress(g_hostfxr, "hostfxr_close");
            if (close) close(g_host_context);
        }
        if (g_hostfxr) FreeLibrary(g_hostfxr);
    }
    return TRUE;
}
