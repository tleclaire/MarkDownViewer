// Minimal test: load hostfxr.dll, call init_for_runtime_config
#include <windows.h>
#include <stdio.h>

struct hostfxr_initialize_parameters {
    size_t size;
    const wchar_t* host_path;
    const wchar_t* dotnet_root;
};

typedef int (__cdecl* hostfxr_initialize_fn)(
    const wchar_t* runtime_config_path,
    const struct hostfxr_initialize_parameters* parameters,
    void** host_context_handle);

typedef int (__cdecl* hostfxr_close_fn)(void* host_context_handle);

int main()
{
    HMODULE hfxr = LoadLibraryW(L"C:\\Program Files\\dotnet\\host\\fxr\\10.0.8\\hostfxr.dll");
    if (!hfxr) { printf("FAIL: LoadLibrary hostfxr\n"); return 1; }
    printf("OK: LoadLibrary hostfxr\n");

    hostfxr_initialize_fn init = (hostfxr_initialize_fn)
        GetProcAddress(hfxr, "hostfxr_initialize_for_runtime_config");
    hostfxr_close_fn close = (hostfxr_close_fn)
        GetProcAddress(hfxr, "hostfxr_close");
    if (!init || !close) { printf("FAIL: GetProcAddress\n"); return 1; }
    printf("OK: GetProcAddress\n");

    // Test 1: NULL parameters
    void* ctx = NULL;
    int rc = init(L"D:\\Projekte\\MarkdownViewer\\MdViewerWlx\\bin\\Release\\net10.0-windows\\MdViewerWlx.runtimeconfig.json",
                  NULL, &ctx);
    if (rc == 0 || rc == 1) {
        printf("Test1 (NULL params): rc=%d (0x%x) ctx=%p\n", rc, rc, ctx);
        if (ctx) close(ctx);
    } else {
        printf("Test1 FAILED: rc=%d (0x%x) - bad args?\n", rc, rc);
    }

    // Test 2: With params
    struct hostfxr_initialize_parameters params;
    params.size = sizeof(params);
    params.host_path = L"D:\\Projekte\\MarkdownViewer\\MdViewerWlx\\bin\\Release\\net10.0-windows\\MdViewerWlx.wlx";
    params.dotnet_root = NULL;

    ctx = NULL;
    rc = init(L"D:\\Projekte\\MarkdownViewer\\MdViewerWlx\\bin\\Release\\net10.0-windows\\MdViewerWlx.runtimeconfig.json",
              &params, &ctx);
    if (rc == 0 || rc == 1) {
        printf("Test2 (with params): rc=%d (0x%x) ctx=%p\n", rc, rc, ctx);
        if (ctx) close(ctx);
    } else {
        printf("Test2 FAILED: rc=%d (0x%x) - bad params?\n", rc, rc);
    }

    FreeLibrary(hfxr);
    return 0;
}
