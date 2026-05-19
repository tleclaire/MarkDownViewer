#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>

typedef int32_t(__cdecl* hostfxr_initialize_for_dotnet_command_line_fn)(
    int argc, const wchar_t* argv[], void* parameters, void** host_context);

typedef int32_t(__cdecl* hostfxr_close_fn)(void* host_context);

int main()
{
    HMODULE hfxr = LoadLibraryW(L"C:\\Program Files\\dotnet\\host\\fxr\\10.0.8\\hostfxr.dll");
    if (!hfxr) { printf("FAIL: LoadLibrary\n"); return 1; }

    hostfxr_initialize_for_dotnet_command_line_fn init =
        (hostfxr_initialize_for_dotnet_command_line_fn)
        GetProcAddress(hfxr, "hostfxr_initialize_for_dotnet_command_line");
    hostfxr_close_fn close_fn =
        (hostfxr_close_fn)
        GetProcAddress(hfxr, "hostfxr_close");

    // Test with MdViewerWlx.exe
    const wchar_t* argv1[] = { L"D:\\Projekte\\MarkdownViewer\\tools\\NativeBootstrapper\\MdViewerWlx.exe" };
    void* ctx1 = NULL;
    int rc1 = init(1, argv1, NULL, &ctx1);
    printf("MdViewerWlx.exe: rc=%d (0x%08x) ctx=%p\n", rc1, rc1, ctx1);
    if (rc1 == 0 && ctx1) close_fn(ctx1);

    // Test with TestShim.exe (known good)
    const wchar_t* argv2[] = { L"D:\\Projekte\\MarkdownViewer\\tools\\TestShim\\bin\\x64\\Release\\net10.0\\win-x64\\TestShim.exe" };
    void* ctx2 = NULL;
    int rc2 = init(1, argv2, NULL, &ctx2);
    printf("TestShim.exe:    rc=%d (0x%08x) ctx=%p\n", rc2, rc2, ctx2);
    if (rc2 == 0 && ctx2) close_fn(ctx2);

    FreeLibrary(hfxr);
    return 0;
}
