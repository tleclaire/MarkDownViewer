using System.Reflection;

string path = @"D:\Projekte\MarkdownViewer\tools\TestMinimalExport\bin\Release\net10.0\win-x64\TestMinimalExport.dll";
Console.WriteLine($"Loading: {path}");
Console.WriteLine($"File exists: {File.Exists(path)}");

try
{
    var asm = Assembly.LoadFrom(path);
    Console.WriteLine($"Loaded: {asm.FullName}");

    var t = asm.GetType("TestMinimalExport.Exports");
    Console.WriteLine($"Type: {t?.FullName ?? "NULL"}");

    var m = t?.GetMethod("Ping", BindingFlags.Public | BindingFlags.Static);
    Console.WriteLine($"Method: {m?.Name ?? "NULL"}");

    if (m != null)
    {
        var result = m.Invoke(null, [42]);
        Console.WriteLine($"Ping(42) = {result}");
        Console.WriteLine($"SUCCESS! (result == 43: {(int)result! == 43})");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.GetType().Name}: {ex.Message}");
    if (ex.InnerException != null)
        Console.WriteLine($"Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
}
