using System.Runtime.InteropServices;

// Minimal shim EXE — provides runtime context for native hostfxr hosting.
// If run directly, it waits for the native host to signal shutdown.

namespace TestShim;

// Exports for native code via load_assembly_and_get_function_pointer
public static class Exports
{
    /// <summary>
    /// Diagnostic export to verify native hosting can reach managed code.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "PingNative")]
    public static int PingNative(int value) => value + 1;
}

class Program
{
    static void Main()
    {
        Console.WriteLine("TestShim running");
        Console.ReadLine();
    }
}
