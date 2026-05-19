using System.Runtime.InteropServices;

namespace TestMinimalExport;

// Standard delegate for load_assembly_and_get_function_pointer
public delegate int PingDelegate(int value);

public static class Exports
{
    // Regular static method (callable via delegate)
    public static int Ping(int value)
    {
        return value + 1;
    }

    // Also keep the UnmanagedCallersOnly export
    [UnmanagedCallersOnly(EntryPoint = "PingNative")]
    public static int PingNative(int value)
    {
        return value + 1;
    }

    [UnmanagedCallersOnly(EntryPoint = "GetName")]
    public static IntPtr GetName()
    {
        return Marshal.StringToCoTaskMemAnsi("Hello from managed!");
    }
}
