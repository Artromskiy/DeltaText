using System.Reflection;
using System.Runtime.InteropServices;

namespace Delta.Text;

internal static class NativeLibraryResolver
{
    private static readonly string[] NativeLibraryNames =
    [
        "libHarfBuzzSharp",
        "DeltaTextMsdf"
    ];

    static NativeLibraryResolver()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
    }

    internal static void EnsureInitialized()
    {
    }

    internal static IReadOnlyList<string> CandidatePaths(string directory, string libraryName)
    {
        var candidates = new List<string>();
        AddPlatformNames(candidates, directory, libraryName);
        foreach (var runtimeId in RuntimeIds())
        {
            var runtimeDirectory = Path.Combine(directory, "runtimes", runtimeId, "native");
            AddPlatformNames(candidates, runtimeDirectory, libraryName);
        }

        return candidates;
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!NativeLibraryNames.Contains(libraryName, StringComparer.Ordinal))
        {
            return IntPtr.Zero;
        }

        var assemblyDirectory = Path.GetDirectoryName(assembly.Location);
        var baseDirectory = string.IsNullOrEmpty(assemblyDirectory) ? AppContext.BaseDirectory : assemblyDirectory;
        return ResolveCore(
            libraryName,
            baseDirectory,
            static name => NativeLibrary.TryLoad(name, out var handle) ? handle : IntPtr.Zero,
            static candidate => NativeLibrary.TryLoad(candidate, out var handle) ? handle : IntPtr.Zero);
    }

    internal static IntPtr ResolveCore(
        string libraryName,
        string directory,
        Func<string, IntPtr> defaultLoader,
        Func<string, IntPtr> candidateLoader)
    {
        ArgumentNullException.ThrowIfNull(libraryName);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(defaultLoader);
        ArgumentNullException.ThrowIfNull(candidateLoader);
        var defaultHandle = defaultLoader(libraryName);
        if (defaultHandle != IntPtr.Zero)
        {
            return defaultHandle;
        }

        var candidates = CandidatePaths(directory, libraryName);
        foreach (var candidate in candidates)
        {
            var handle = candidateLoader(candidate);
            if (handle != IntPtr.Zero)
            {
                return handle;
            }
        }

        throw new DllNotFoundException(
            $"Could not load native library '{libraryName}'. Tried: {string.Join(", ", candidates)}");
    }

    private static void AddPlatformNames(List<string> candidates, string directory, string libraryName)
    {
        var baseName = libraryName.StartsWith("lib", StringComparison.Ordinal) ? libraryName[3..] : libraryName;
        AddCandidate(candidates, Path.Combine(directory, libraryName));

        if (OperatingSystem.IsWindows())
        {
            AddCandidate(candidates, Path.Combine(directory, $"{libraryName}.dll"));
            AddCandidate(candidates, Path.Combine(directory, $"lib{baseName}.dll"));
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            AddCandidate(candidates, Path.Combine(directory, $"{libraryName}.dylib"));
            AddCandidate(candidates, Path.Combine(directory, $"lib{baseName}.dylib"));
            return;
        }

        AddCandidate(candidates, Path.Combine(directory, $"{libraryName}.so"));
        AddCandidate(candidates, Path.Combine(directory, $"lib{baseName}.so"));
    }

    private static void AddCandidate(List<string> candidates, string candidate)
    {
        if (!candidates.Contains(candidate, StringComparer.Ordinal))
        {
            candidates.Add(candidate);
        }
    }

    private static IEnumerable<string> RuntimeIds()
    {
        var platform = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var runtimeId = RuntimeInformation.RuntimeIdentifier;
        if (!string.IsNullOrWhiteSpace(runtimeId) && runtimeId.StartsWith(platform, StringComparison.OrdinalIgnoreCase))
        {
            yield return runtimeId;
        }

        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => string.Empty
        };
        if (architecture.Length > 0)
        {
            var architectureRid = $"{platform}-{architecture}";
            if (!string.Equals(architectureRid, runtimeId, StringComparison.OrdinalIgnoreCase))
            {
                yield return architectureRid;
            }
        }

        yield return platform;
    }
}
