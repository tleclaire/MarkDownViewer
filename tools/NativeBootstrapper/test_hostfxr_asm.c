// Minimal test: hostfxr via DOTNET_COMMAND_LINE -> load_assembly_and_get_function_pointer
// KEY FIX: hostfxr_initialize_for_runtime_config uses "libhost" mode which skips
// app deps.json. hostfxr_initialize_for_dotnet_command_line uses "apphost" mode
// which properly resolves deps.json and sets APP_CONTEXT_BASE_DIRECTORY.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <stdint.h>

// Official delegate types from hostfxr.h:
//   hdt_load_assembly_and_get_function_pointer = 5  (not 6!)
//   hdt_get_function_pointer                  = 6  (not 7!)
#define HDT_LOAD_AND_GET 5
#define HDT_GET_FN_PTR   6

// Per coreclr_delegates.h, delegate_calltype is __stdcall on Win32.
#define CORECLR_DELEGATE_CALLTYPE __stdcall

// For [UnmanagedCallersOnly] methods, pass UNMANAGEDCALLERSONLY_METHOD
// as delegate_type_name instead of NULL.
#define UNMANAGEDCALLERSONLY_METHOD ((const wchar_t*)-1)

// hostfxr typedefs
typedef int32_t(__cdecl* hostfxr_initialize_for_dotnet_command_line_fn)(
    int argc, const wchar_t* argv[], void* parameters, void** host_context);

typedef int32_t(__cdecl* hostfxr_get_runtime_delegate_fn)(
    void* host_context, int32_t delegate_type, void** delegate_ptr);

typedef int32_t(__cdecl* hostfxr_close_fn)(void* host_context);

// hdt=5: load_assembly_and_get_function_pointer
typedef int32_t(CORECLR_DELEGATE_CALLTYPE* load_asm_fn)(
    const wchar_t* assembly_path, const wchar_t* type_name,
    const wchar_t* method_name, const wchar_t* delegate_type_name,
    void* reserved, void** delegate);

// hdt=6: get_function_pointer (for already-loaded assemblies, no assembly_path)
typedef int32_t(CORECLR_DELEGATE_CALLTYPE* get_fn_ptr_fn)(
    const wchar_t* type_name, const wchar_t* method_name,
    const wchar_t* delegate_type_name,
    void* load_context, void* reserved, void** delegate);

int main()
{
    // 1. Find and load hostfxr.dll (semver-aware comparison)
    WIN32_FIND_DATAW ffd;
    HANDLE hFind = FindFirstFileW(L"C:\\Program Files\\dotnet\\host\\fxr\\*", &ffd);
    if (hFind == INVALID_HANDLE_VALUE) { printf("FAIL: can't list fxr dir\n"); return 1; }

    wchar_t best[100] = L"";
    do {
        if (ffd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
            if (wcscmp(ffd.cFileName, L".") != 0 && wcscmp(ffd.cFileName, L"..") != 0) {
                int ma = 0, na = 0, pa = 0, mb = 0, nb = 0, pb = 0;
                swscanf_s(ffd.cFileName, L"%d.%d.%d", &ma, &na, &pa);
                swscanf_s(best, L"%d.%d.%d", &mb, &nb, &pb);
                int cmp = (ma != mb) ? (ma - mb) : ((na != nb) ? (na - nb) : (pa - pb));
                if (best[0] == 0 || cmp > 0)
                    wcscpy_s(best, 100, ffd.cFileName);
            }
        }
    } while (FindNextFileW(hFind, &ffd) != 0);
    FindClose(hFind);

    if (best[0] == 0) { printf("FAIL: no fxr version found\n"); return 1; }

    wchar_t fxr_dll[300];
    swprintf_s(fxr_dll, 300, L"C:\\Program Files\\dotnet\\host\\fxr\\%s\\hostfxr.dll", best);
    wprintf(L"hostfxr: %ls\n", fxr_dll);

    HMODULE hfxr = LoadLibraryW(fxr_dll);
    if (!hfxr) { printf("FAIL: LoadLibrary hostfxr error=%lu\n", GetLastError()); return 1; }

    hostfxr_initialize_for_dotnet_command_line_fn init_fn =
        (hostfxr_initialize_for_dotnet_command_line_fn)GetProcAddress(hfxr, "hostfxr_initialize_for_dotnet_command_line");
    hostfxr_get_runtime_delegate_fn getdel_fn =
        (hostfxr_get_runtime_delegate_fn)GetProcAddress(hfxr, "hostfxr_get_runtime_delegate");
    hostfxr_close_fn close_fn =
        (hostfxr_close_fn)GetProcAddress(hfxr, "hostfxr_close");

    if (!init_fn || !getdel_fn || !close_fn) {
        printf("FAIL: GetProcAddress for hostfxr functions\n"); return 1;
    }
    printf("hostfxr functions resolved OK\n");

    // 2. Init runtime in APP mode via hostfxr_initialize_for_dotnet_command_line
    const wchar_t* app_path = L"D:\\Projekte\\MarkdownViewer\\tools\\TestShim\\bin\\x64\\Release\\net10.0\\win-x64\\TestShim.exe";
    const wchar_t* argv[] = { app_path };
    void* ctx = NULL;
    int rc = init_fn(1, argv, NULL, &ctx);
    printf("init_fn rc=%d (0=OK)\n", rc);
    if (rc != 0) { printf("FAIL: runtime init\n"); FreeLibrary(hfxr); return 1; }

    // 3. Get delegates
    // IMPORTANT: hdt=5 is load_assembly_and_get_function_pointer, hdt=6 is get_function_pointer
    load_asm_fn load_asm = NULL;
    rc = getdel_fn(ctx, HDT_LOAD_AND_GET, (void**)&load_asm);
    printf("getdel_fn(hdt=5=load_asm) rc=%d, ptr=%p\n", rc, (void*)load_asm);
    if (rc != 0 || !load_asm) { printf("FAIL: get hdt=5 delegate\n"); close_fn(ctx); FreeLibrary(hfxr); return 1; }

    get_fn_ptr_fn get_fn = NULL;
    rc = getdel_fn(ctx, HDT_GET_FN_PTR, (void**)&get_fn);
    printf("getdel_fn(hdt=6=get_fn) rc=%d, ptr=%p\n", rc, (void*)get_fn);

    // 4. Try hdt=7: get_function_pointer on TestShim (already loaded as entry point)
    if (get_fn)
    {
        void* ptr = NULL;
        // For [UnmanagedCallersOnly], delegate_type_name must be UNMANAGEDCALLERSONLY_METHOD
        rc = get_fn(L"TestShim.Exports, TestShim", L"PingNative",
                    UNMANAGEDCALLERSONLY_METHOD, NULL, NULL, &ptr);
        printf("hdt=7: TestShim.Exports.PingNative rc=%d (0x%08x) ptr=%p\n", rc, rc, ptr);

        if (rc == 0 && ptr) {
            typedef int(CORECLR_DELEGATE_CALLTYPE* ping_fn)(int);
            ping_fn ping = (ping_fn)ptr;
            int result = ping(42);
            printf("hdt=7: Ping(42) = %d\n", result);
            if (result == 43) printf("SUCCESS: hdt=7 works!\n");
        }
    }

    // 5. Try hdt=6: load_assembly_and_get_function_pointer with TestShim
    {
        wchar_t shim_asm[300];
        swprintf_s(shim_asm, 300, L"D:\\Projekte\\MarkdownViewer\\tools\\TestShim\\bin\\x64\\Release\\net10.0\\win-x64\\TestShim.dll");

        void* ptr = NULL;

        // Try with NULL delegate_type_name (returns component_entry_point_fn wrapper)
        rc = load_asm(shim_asm, L"TestShim.Exports, TestShim",
                      L"PingNative", NULL, NULL, &ptr);
        printf("hdt=6: TestShim NULL dt rc=%d (0x%08x) ptr=%p\n", rc, rc, ptr);

        // Try with UNMANAGEDCALLERSONLY_METHOD
        rc = load_asm(shim_asm, L"TestShim.Exports, TestShim",
                      L"PingNative", UNMANAGEDCALLERSONLY_METHOD, NULL, &ptr);
        printf("hdt=6: TestShim UCOnly dt rc=%d (0x%08x) ptr=%p\n", rc, rc, ptr);
    }

    // 6. Try loading TestMinimalExport.dll
    {
        wchar_t asm_path[300];
        swprintf_s(asm_path, 300, L"D:\\Projekte\\MarkdownViewer\\tools\\TestMinimalExport\\bin\\Release\\net10.0\\win-x64\\TestMinimalExport.dll");

        void* ping_ptr = NULL;

        // [UnmanagedCallersOnly] with UNMANAGEDCALLERSONLY_METHOD
        rc = load_asm(asm_path, L"TestMinimalExport.Exports, TestMinimalExport",
                      L"PingNative", UNMANAGEDCALLERSONLY_METHOD, NULL, &ping_ptr);
        printf("Test1: UCOnly dt PingNative rc=%d (0x%08x) ptr=%p\n", rc, rc, ping_ptr);

        // Regular method WITH fully-qualified delegate type
        void* ping_ptr2 = NULL;
        rc = load_asm(asm_path, L"TestMinimalExport.Exports, TestMinimalExport",
                      L"Ping", L"TestMinimalExport.PingDelegate, TestMinimalExport", NULL, &ping_ptr2);
        printf("Test2: delegate Ping rc=%d (0x%08x) ptr=%p\n", rc, rc, ping_ptr2);

        if (rc == 0 && ping_ptr2) {
            typedef int(CORECLR_DELEGATE_CALLTYPE* ping_fn)(int);
            ping_fn ping = (ping_fn)ping_ptr2;
            int result = ping(42);
            printf("Ping(42) via delegate = %d\n", result);
            if (result == 43) printf("SUCCESS: managed code via delegate works!\n");
        }
    }

    close_fn(ctx);
    FreeLibrary(hfxr);
    return 0;
}
