// Empty main entry point — the app is loaded as a runtime host anchor
// for the native bootstrapper. Actual WLX work happens via [UnmanagedCallersOnly]
// exports called from native code through the .NET hosting API.
namespace MdViewerWlx;

internal static class Program
{
    static void Main()
    {
        // This EXE is never meant to run standalone.
        // It exists to provide a valid managed EXE for
        // hostfxr_initialize_for_dotnet_command_line so that
        // the native bootstrapper can initialize the runtime,
        // then call [UnmanagedCallersOnly] exports.
    }
}
