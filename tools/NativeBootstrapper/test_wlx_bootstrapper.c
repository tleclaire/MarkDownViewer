// Test: load MdViewerWlx.wlx, call ListGetDetectString + ListLoad
// Verifies the full bootstrapper pipeline works via hostfxr + command_line init + hdt=5
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <objbase.h>
#include <stdio.h>

// WLX API typedefs matching Total Commander plugin interface
typedef int  (__stdcall *ListGetDetectStringFn)(wchar_t* detect, int maxlen);
typedef HWND (__stdcall *ListLoadFn)(HWND parentWin, wchar_t* fileToShow, int showFlags);

LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    if (msg == WM_DESTROY) PostQuitMessage(0);
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

int main()
{
    // Ensure the calling thread is STA — required by WPF's HwndSource/InputManager.
    // (In production, TC's own UI thread is already STA.)
    HRESULT hr = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    if (FAILED(hr)) { printf("CoInitializeEx(STA) failed: 0x%lx\n", hr); return 1; }

    // Create a simple parent window (needed for ListLoad)
    const wchar_t CLASS_NAME[] = L"TestWlxParent";
    WNDCLASSW wc = {0};
    wc.lpfnWndProc   = WndProc;
    wc.hInstance     = GetModuleHandleW(NULL);
    wc.lpszClassName = CLASS_NAME;
    RegisterClassW(&wc);

    HWND parentHwnd = CreateWindowExW(0, CLASS_NAME, L"WLX Test", WS_OVERLAPPEDWINDOW,
                                      200, 200, 800, 600, NULL, NULL, wc.hInstance, NULL);
    if (!parentHwnd) { printf("FAIL: CreateWindow error=%lu\n", GetLastError()); return 1; }
    printf("Parent HWND created: %p\n", (void*)parentHwnd);

    // 1. Load the WLX DLL (the bootstrapper)
    HMODULE hwlx = LoadLibraryW(L"D:\\Projekte\\MarkdownViewer\\tools\\NativeBootstrapper\\MdViewerWlx.wlx");
    if (!hwlx) {
        printf("FAIL: LoadLibrary MdViewerWlx.wlx error=%lu\n", GetLastError());
        DestroyWindow(parentHwnd);
        return 1;
    }
    printf("MdViewerWlx.wlx loaded: %p\n", (void*)hwlx);

    // 2. Test ListGetDetectString
    ListGetDetectStringFn getDetect = (ListGetDetectStringFn)GetProcAddress(hwlx, "ListGetDetectString");
    if (!getDetect) {
        printf("FAIL: GetProcAddress ListGetDetectString\n");
        FreeLibrary(hwlx); DestroyWindow(parentHwnd);
        return 1;
    }
    printf("ListGetDetectString resolved: %p\n", (void*)getDetect);

    wchar_t detect[256] = {0};
    int rc = getDetect(detect, 256);
    wprintf(L"ListGetDetectString rc=%d, detect=\"%ls\"\n", rc, detect);
    if (rc == 0 || wcslen(detect) == 0) {
        printf("WARNING: detect string empty - bootstrapper native init OK but no trigger\n");
    }

    // 3. Test ListLoad (THIS triggers the full hostfxr pipeline)
    ListLoadFn listLoad = (ListLoadFn)GetProcAddress(hwlx, "ListLoad");
    if (!listLoad) {
        printf("FAIL: GetProcAddress ListLoad\n");
        FreeLibrary(hwlx); DestroyWindow(parentHwnd);
        return 1;
    }
    printf("ListLoad resolved: %p\n", (void*)listLoad);

    // Create a test .md file
    wchar_t testMd[] = L"D:\\Projekte\\MarkdownViewer\\tools\\NativeBootstrapper\\test_bootstrapper.md";
    // Ensure file exists
    FILE* f = NULL;
    _wfopen_s(&f, testMd, L"w, ccs=UTF-8");
    if (f) {
        fwprintf(f, L"# Bootstrapper Test\n\nThis is a **markdown** file to test the bootstrapper.\n");
        fclose(f);
    }

    printf("\n=== Calling ListLoad (THIS INITIALIZES HOSTFXR!) ===\n");
    HWND childWnd = listLoad(parentHwnd, testMd, 0);
    printf("ListLoad returned HWND=%p\n", (void*)childWnd);

    if (childWnd != NULL && childWnd != (HWND)-1) {
        printf("SUCCESS: ListLoad created managed WPF window!\n");
        ShowWindow(parentHwnd, SW_SHOW);

        // Peek message loop to let window render
        printf("Running message loop for 3 seconds...\n");
        MSG msg = {0};
        DWORD start = GetTickCount64();
        while (GetTickCount64() - start < 3000) {
            while (PeekMessageW(&msg, NULL, 0, 0, PM_REMOVE)) {
                TranslateMessage(&msg);
                DispatchMessageW(&msg);
            }
            Sleep(10);
        }

        // Post Quit to destroy windows
        PostMessageW(childWnd, WM_CLOSE, 0, 0);
        PostMessageW(parentHwnd, WM_CLOSE, 0, 0);
        printf("Test complete: WPF window was created and shown successfully!\n");
    } else {
        printf("WARNING: ListLoad returned NULL. This may be expected if WebView2/DPI issues.\n");
        printf("Checking if hostfxr init at least succeeded...\n");
        // Get last error from managed side... can't easily.
    }

    FreeLibrary(hwlx);
    DestroyWindow(parentHwnd);
    CoUninitialize();
    return (childWnd != NULL && childWnd != (HWND)-1) ? 0 : 1;
}
