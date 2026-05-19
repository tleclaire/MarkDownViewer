using System;
using System.Runtime.InteropServices;
using System.Text;

[DllImport(@"D:\Projekte\MarkdownViewer\MdViewerWlx\bin\Release\net10.0-windows\MdViewerWlx.wlx",
    CallingConvention = CallingConvention.StdCall)]
static extern int ListGetDetectString(IntPtr buf, int maxLen);

[DllImport(@"D:\Projekte\MarkdownViewer\MdViewerWlx\bin\Release\net10.0-windows\MdViewerWlx.wlx",
    CallingConvention = CallingConvention.StdCall)]
static extern IntPtr ListLoad(IntPtr parentWin, string fileToLoad, int showFlags);

// Test ListGetDetectString
byte[] buf = new byte[256];
var ptr = Marshal.UnsafeAddrOfPinnedArrayElement(buf, 0);
int rc = ListGetDetectString(ptr, 256);
string detect = System.Text.Encoding.ASCII.GetString(buf).TrimEnd('\0');
Console.WriteLine($"ListGetDetectString: rc={rc}, detect='{detect}'");

// Test ListLoad
string testFile = @"D:\Projekte\MarkdownViewer\test.md";
Console.WriteLine($"Calling ListLoad with: {testFile}");
IntPtr hwnd = ListLoad(IntPtr.Zero, testFile, 0);
Console.WriteLine($"ListLoad returned: hwnd=0x{hwnd.ToInt64():X}");
