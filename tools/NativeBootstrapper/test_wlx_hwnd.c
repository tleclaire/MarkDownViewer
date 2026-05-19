// Test: Load MdViewerWlx.wlx and call ListLoad with a real parent HWND.
// Creates a visible window, passes it as parent, runs message loop.
//
// Build:
//   cl.exe /I"SDK\include\um" /I"SDK\include\shared" /I"SDK\include\ucrt"
//         /nologo test_wlx_hwnd.c
//         /link /LIBPATH:"SDK\lib" /LIBPATH:"VC\lib"
//         kernel32.lib user32.lib

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>

// WLX function pointer types
typedef int (__stdcall* lds_fn)(void*, int);
typedef void* (__stdcall* ll_fn)(void*, const char*, int);
typedef void (__stdcall* lcw_fn)(void*);

static const char CLASS_NAME[] = "TestWlxParent";
static volatile int g_quit = 0;

// Window procedure for our test parent window
LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_DESTROY:
        g_quit = 1;
        return 0;
    case WM_CLOSE:
        g_quit = 1;
        return 0;
    }
    return DefWindowProc(hwnd, msg, wParam, lParam);
}

int main()
{
    // ---- Register window class ----
    HINSTANCE hInst = GetModuleHandle(NULL);
    WNDCLASS wc = {0};
    wc.lpfnWndProc   = WndProc;
    wc.hInstance     = hInst;
    wc.lpszClassName = CLASS_NAME;
    wc.hbrBackground = (HBRUSH)(COLOR_WINDOW + 1);
    wc.hCursor       = LoadCursor(NULL, IDC_ARROW);

    if (!RegisterClass(&wc))
    {
        printf("FAIL: RegisterClass (error %lu)\n", GetLastError());
        return 1;
    }
    printf("OK: Window class registered\n");

    // ---- Create parent window ----
    HWND hwndParent = CreateWindowEx(
        0, CLASS_NAME, "WLX Test Parent",
        WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT, CW_USEDEFAULT, 900, 700,
        NULL, NULL, hInst, NULL);

    if (!hwndParent)
    {
        printf("FAIL: CreateWindow (error %lu)\n", GetLastError());
        return 1;
    }
    printf("OK: Parent HWND = 0x%llX\n", (unsigned long long)(uintptr_t)hwndParent);
    ShowWindow(hwndParent, SW_SHOW);
    UpdateWindow(hwndParent);

    // ---- Load .wlx DLL ----
    HMODULE hwlx = LoadLibraryW(
        L"D:\\Projekte\\MarkdownViewer\\MdViewerWlx\\bin\\Release\\net10.0-windows\\publish\\MdViewerWlx.wlx");
    if (!hwlx)
    {
        printf("FAIL: LoadLibrary wlx (error %lu)\n", GetLastError());
        return 1;
    }
    printf("OK: LoadLibrary wlx = %p\n", (void*)hwlx);

    // ---- Get function pointers ----
    lds_fn lds = (lds_fn)GetProcAddress(hwlx, "ListGetDetectString");
    ll_fn  ll  = (ll_fn)GetProcAddress(hwlx, "ListLoad");
    lcw_fn lcw = (lcw_fn)GetProcAddress(hwlx, "ListCloseWindow");

    if (!lds) { printf("FAIL: GetProcAddress ListGetDetectString\n"); return 1; }
    if (!ll)  { printf("FAIL: GetProcAddress ListLoad\n"); return 1; }
    if (!lcw) { printf("FAIL: GetProcAddress ListCloseWindow\n"); return 1; }
    printf("OK: All function pointers resolved\n");

    // ---- Test ListGetDetectString ----
    char buf[256] = {0};
    int rc = lds(buf, 256);
    printf("ListGetDetectString -> rc=%d, buf='%s'\n", rc, buf);

    // ---- Test ListLoad (with real HWND!) ----
    const char* testFile = "D:\\Projekte\\MarkdownViewer\\test.md";
    printf("Calling ListLoad with parent=0x%llX, file='%s'...\n",
           (unsigned long long)(uintptr_t)hwndParent, testFile);

    void* viewerHwnd = ll((void*)hwndParent, testFile, 0);
    printf("ListLoad returned: %p\n", viewerHwnd);

    if (viewerHwnd)
    {
        printf("SUCCESS: Viewer HWND created!\n");
        printf("Running message loop for 5 seconds...\n");

        // Message loop: keep parent window alive so child WPF window works
        MSG msg;
        for (int i = 0; i < 50 && !g_quit; i++) // ~5s (100ms per iteration)
        {
            while (PeekMessage(&msg, NULL, 0, 0, PM_REMOVE))
            {
                TranslateMessage(&msg);
                DispatchMessage(&msg);
            }
            Sleep(100);
        }

        // Cleanup
        printf("Calling ListCloseWindow(%p)...\n", viewerHwnd);
        lcw(viewerHwnd);
        printf("ListCloseWindow done\n");
    }
    else
    {
        printf("FAIL: ListLoad returned NULL\n");
    }

    // ---- Cleanup ----
    FreeLibrary(hwlx);
    printf("OK: FreeLibrary done\n");

    DestroyWindow(hwndParent);
    printf("OK: Parent window destroyed\n");

    return viewerHwnd ? 0 : 1;
}
