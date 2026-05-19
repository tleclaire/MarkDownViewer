// Test: load MdViewerWlx.wlx and call ListLoad (from native process, no CLR loaded)
#include <windows.h>
#include <stdio.h>

int main()
{
    HMODULE hwlx = LoadLibraryW(L"D:\\Projekte\\MarkdownViewer\\MdViewerWlx\\bin\\Release\\net10.0-windows\\MdViewerWlx.wlx");
    if (!hwlx) { printf("FAIL: LoadLibrary wlx\n"); return 1; }
    printf("OK: LoadLibrary wlx = %p\n", (void*)hwlx);

    // Test ListGetDetectString (native)
    typedef int (__stdcall* lds_fn)(void*, int);
    lds_fn lds = (lds_fn)GetProcAddress(hwlx, "ListGetDetectString");
    if (!lds) { printf("FAIL: GetProcAddress ListGetDetectString\n"); return 1; }
    printf("OK: GetProcAddress ListGetDetectString = %p\n", (void*)lds);

    char buf[256] = {0};
    int rc = lds(buf, 256);
    printf("ListGetDetectString -> rc=%d, buf='%s'\n", rc, buf);

    // Test ListLoad (should init CLR)
    typedef void* (__stdcall* ll_fn)(void*, const char*, int);
    ll_fn ll = (ll_fn)GetProcAddress(hwlx, "ListLoad");
    if (!ll) { printf("FAIL: GetProcAddress ListLoad\n"); return 1; }
    printf("OK: GetProcAddress ListLoad = %p\n", (void*)ll);

    printf("Calling ListLoad...\n");
    void* result = ll(NULL, "D:\\Projekte\\MarkdownViewer\\test.md", 0);
    printf("ListLoad returned: %p\n", result);

    FreeLibrary(hwlx);
    return 0;
}
