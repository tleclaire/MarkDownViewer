using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using net.r_eg.DllExport;

// PatchExports — Adds native PE exports to a .NET assembly.
//
// Pipeline:
//   1. Run DllExportWeaver to generate .il with .export directives
//   2. Strip .export, add .vtfixup/.vtentry (our ilasm lacks /EXPORTED)
//   3. Run ilasm manually (without /EXPORTED) → DLL with VTableFixup thunks
//   4. Inject PE export directory entries pointing to VTableFixup slots
//
// Usage: PatchExports <path-to-dll>

var dllPath = args.Length > 0 ? args[0] : null;
if (args.Length > 0 && args[0] == "--inspect")
{
    dllPath = args.Length > 1 ? args[1] : null;
    if (dllPath == null || !File.Exists(dllPath)) { Console.Error.WriteLine("Usage: --inspect <path-to-dll>"); return 1; }
    InspectExports(dllPath);
    return 0;
}

if (dllPath == null || !File.Exists(dllPath))
{
    Console.Error.WriteLine("Usage: PatchExports <path-to-dll>");
    return 1;
}

var nugetRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".nuget", "packages", "dllexport", "1.8.1", "tools");

var monoCecilPath = Path.Combine(nugetRoot, "Mono.Cecil.dll");
var conariPath = Path.Combine(nugetRoot, "Conari.dll");
var ilasmPath = Path.Combine(nugetRoot, "coreclr", "ilasm.exe");
var peViewerPath = Path.Combine(nugetRoot, "PeViewer.exe");

AppDomain.CurrentDomain.AssemblyResolve += (_, eventArgs) =>
{
    var name = new AssemblyName(eventArgs.Name).Name;
    return name switch
    {
        "Mono.Cecil" => Assembly.LoadFrom(monoCecilPath),
        "Conari" => Assembly.LoadFrom(conariPath),
        _ => null
    };
};

Console.WriteLine($"Patching: {dllPath}");

var tempDir = Path.Combine(Path.GetTempPath(), "PX_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDir);

// === Step 1: DllExportWeaver ===
Console.Write("Step 1/5: DllExportWeaver... ");
var weaverOutputDll = Path.Combine(tempDir, "MdViewerWlx.dll");
var iv = new InputValuesCore(new Sp())
{
    InputFileName = dllPath,
    OutputFileName = weaverOutputDll,
    Cpu = CpuPlatform.X64,
    Patches = PatchesType.None,
    PeCheck = PeCheckType.Pe1to1,
    RootDirectory = Path.GetDirectoryName(dllPath) ?? ".",
    MetaLib = dllPath,
    OurILAsmPath = Path.Combine(nugetRoot, "coreclr"),
    EmitDebugSymbols = DebugType.Default,
    DllExportAttributeFullName = "DllExport.DllExportAttribute",
    DllExportAttributeAssemblyName = "MdViewerWlx",
    LeaveIntermediateFiles = "true",
};
Console.WriteLine($"  Patches={iv.Patches} PeCheck={iv.PeCheck} Cpu={iv.Cpu}");
Console.WriteLine($"  DllExportAttr='{iv.DllExportAttributeFullName}' in '{iv.DllExportAttributeAssemblyName}'");

using var weaver = new DllExportWeaver(new Sp()) { InputValues = iv, Timeout = 60000 };
bool weaverSucceeded;
try { weaver.Run(); weaverSucceeded = true; Console.WriteLine("succeeded!"); }
catch (Exception weaverEx) { weaverSucceeded = false; Console.WriteLine($"failed: {weaverEx.GetType().Name}: {weaverEx.Message}"); }

string outputDll = dllPath; // default: use original

if (weaverSucceeded && File.Exists(weaverOutputDll))
{
    Console.WriteLine($"  Weaver output: {weaverOutputDll} ({new FileInfo(weaverOutputDll).Length} bytes)");
    // Check if weaver output already has PE exports
    var (_, pvCountO, pvCountE) = Run(peViewerPath, $"-pemodule \"{weaverOutputDll}\" -num-functions", 10000);
    var countStr = string.IsNullOrEmpty(pvCountO) ? pvCountE : pvCountO;
    Console.WriteLine($"  PE exports in weaver output: {countStr}");
    if (int.TryParse(countStr, out var exportCount) && exportCount > 0)
    {
        // Weaver already produced a working DLL with exports! Deploy it.
        Console.WriteLine("  Weaver output already has exports — skipping IL patching");
        outputDll = weaverOutputDll;
        goto deploy;
    }
    Console.WriteLine("  Weaver output has no PE exports — continuing with IL patching");
}
else if (!weaverSucceeded)
{
    Console.WriteLine("  Weaver failed — continuing with IL patching of intermediate files");
}

// === Step 2: Find & patch .il ===
Console.Write("Step 2/5: Patching .il... ");
string? ilFile = Directory.EnumerateFiles(tempDir, "*.il", SearchOption.AllDirectories).FirstOrDefault();
if (ilFile == null)
{
    // Search more broadly
    ilFile = Directory.EnumerateFiles(Path.GetTempPath(), "*.il", SearchOption.AllDirectories)
        .OrderByDescending(f => File.GetLastWriteTime(f))
        .FirstOrDefault(f => File.ReadAllText(f).Contains("MdViewerWlx"));
}
if (ilFile == null) { Console.Error.WriteLine("\nERROR: .il file not found"); return 1; }

var ilContent = File.ReadAllText(ilFile);
var lines = ilContent.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

// Collect .export directives
var exports = new List<(int ordinal, string name, int lineIdx)>();
for (int i = 0; i < lines.Length; i++)
{
    var m = Regex.Match(lines[i], @"^\s*\.export\s+\[(\d+)\]\s+as\s+'([^']+)'");
    if (m.Success) exports.Add((int.Parse(m.Groups[1].Value), m.Groups[2].Value, i));
}
Console.Write($" {exports.Count} exports found... ");

if (exports.Count == 0) { Console.Error.WriteLine("ERROR: no exports"); return 1; }

// Find insertion point after module declarations
int insertAt = lines.Length - 1;
for (int i = 0; i < lines.Length; i++)
    if (lines[i].TrimStart().StartsWith(".module "))
        insertAt = i + 1;
// Skip blank lines after .module
while (insertAt < lines.Length && string.IsNullOrWhiteSpace(lines[insertAt]))
    insertAt++;

// Build new lines
var newLines = new List<string>();
newLines.AddRange(lines.Take(insertAt));
// VTable fixup header
newLines.Add("");
newLines.Add($"// Auto-generated VTableFixup for {exports.Count} exports");
newLines.Add($".vtfixup [{exports.Count}] fromunmanaged at VT_01");
newLines.Add($".data VT_01 = int32({string.Join(" ", Enumerable.Repeat("0", exports.Count))})");
newLines.Add("");

// Replace .export with .vtentry, copy rest
int entryIdx = 1;
var exportIdx = 0;
for (int i = insertAt; i < lines.Length; i++)
{
    if (exportIdx < exports.Count && i == exports[exportIdx].lineIdx)
    {
        newLines.Add($"    .vtentry 1 : {entryIdx}");
        entryIdx++;
        exportIdx++;
    }
    else
    {
        newLines.Add(lines[i]);
    }
}

File.WriteAllText(ilFile, string.Join(Environment.NewLine, newLines));
Console.WriteLine("OK");

// === Step 3: Run ilasm ===
Console.Write("Step 3/5: ilasm... ");
var ilasmOutputDll = dllPath + ".patched";
var resFile = Path.ChangeExtension(ilFile, ".res");
var ilasmArgs = $"\"{ilFile}\" /output=\"{ilasmOutputDll}\" /dll /x64 /quiet";
if (File.Exists(resFile)) ilasmArgs += $" /res=\"{resFile}\"";

var (ec, o, e) = Run(ilasmPath, ilasmArgs, 30000);
if (ec != 0) { Console.Error.WriteLine($"\nilasm error ({ec}): {e}{o}"); return 1; }
Console.WriteLine($"OK ({new FileInfo(ilasmOutputDll).Length} bytes)");

// === Step 4: PE Export Directory ===
Console.Write("Step 4/5: PE exports... ");
PeExportPatcher.Patch(ilasmOutputDll, exports.Select(x => x.name).ToList());
Console.WriteLine("OK");
outputDll = ilasmOutputDll;

// === Step 5: Deploy & Verify ===
deploy:
Console.Write("Step 5/5: Deploy... ");
File.Copy(dllPath, dllPath + ".backup", overwrite: true);
File.Copy(outputDll, dllPath, overwrite: true);
// Copy .pdb if exists
var pdbSrc = Path.ChangeExtension(outputDll, ".pdb");
var pdbOrig = Path.ChangeExtension(dllPath, ".pdb");
if (File.Exists(pdbSrc)) File.Copy(pdbSrc, pdbOrig, overwrite: true);
Console.WriteLine("OK");

try { Directory.Delete(tempDir, true); } catch { }

// Verify
Console.Write("\nVerification: ");
var (_, pvO, pvE) = Run(peViewerPath, $"-pemodule \"{dllPath}\" -num-functions", 10000);
Console.WriteLine(string.IsNullOrEmpty(pvO) ? pvE : pvO);

return 0;

static (int exitCode, string stdout, string stderr) Run(string exe, string args, int timeout)
{
    using var p = Process.Start(new ProcessStartInfo(exe, args)
    {
        RedirectStandardOutput = true, RedirectStandardError = true,
        UseShellExecute = false, CreateNoWindow = true
    })!;
    p.WaitForExit(timeout);
    return (p.ExitCode, p.StandardOutput.ReadToEnd(), p.StandardError.ReadToEnd());
}

static void InspectExports(string dllPath)
{
    // Build minimal InputValues to feed to inspector
    var ivType = typeof(DllExportWeaver).Assembly.GetType("net.r_eg.DllExport.InputValuesCore")
        ?? throw new InvalidOperationException("InputValuesCore not found");
    var iv = Activator.CreateInstance(ivType, [new Sp()])!;
    ivType.GetProperty("InputFileName")!.SetValue(iv, dllPath);

    // ExportAssemblyInspector is internal — use reflection
    var asm = typeof(DllExportWeaver).Assembly;
    var inspectorType = asm.GetType("net.r_eg.DllExport.ExportAssemblyInspector")
        ?? throw new InvalidOperationException("ExportAssemblyInspector not found");
    var inspector = Activator.CreateInstance(inspectorType, [iv])!;

    // ExtractExports() returns AssemblyExports
    var extractExports = inspectorType.GetMethod("ExtractExports", Type.EmptyTypes)
        ?? throw new InvalidOperationException("ExtractExports() not found");
    var result = extractExports.Invoke(inspector, null)!;

    // AssemblyExports has .Methods (ExportMethodInfo[]) and .Classes
    var methodsProp = result.GetType().GetProperty("Methods");
    if (methodsProp == null)
    {
        Console.WriteLine($"AssemblyExports type: {result.GetType().FullName}");
        foreach (var p in result.GetType().GetProperties())
            Console.WriteLine($"  Property: {p.Name} : {p.PropertyType.Name}");
        return;
    }
    var items = (Array)methodsProp.GetValue(result)!;
    Console.WriteLine($"DllExport methods found: {items.Length}");
    for (int i = 0; i < items.Length; i++)
    {
        var item = items.GetValue(i)!;
        var ordinal = (int)item.GetType().GetProperty("ExportOrdinal")!.GetValue(item)!;
        var methodName = (string)item.GetType().GetProperty("MethodName")!.GetValue(item)!;
        var fullName = (string)item.GetType().GetProperty("FullName")!.GetValue(item)!;
        Console.WriteLine($"  [{ordinal}] '{methodName}' ({fullName})");
    }
}

file class Sp : IServiceProvider
{
    private readonly DllExportNotifier _n = new();
    public object? GetService(Type t) => t == typeof(IDllExportNotifier) ? _n : null;
}
