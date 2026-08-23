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

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!NativeLibraryNames.Contains(libraryName, StringComparer.Ordinal))
        {
            return IntPtr.Zero;
        }

        var assemblyDirectory = Path.GetDirectoryName(assembly.Location);
        var baseDirectory = string.IsNullOrEmpty(assemblyDirectory) ? AppContext.BaseDirectory : assemblyDirectory;
        foreach (var candidate in CandidatePaths(baseDirectory, libraryName))
        {
            if (NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> CandidatePaths(string directory, string libraryName)
    {
        yield return Path.Combine(directory, libraryName);

        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(directory, $"{libraryName}.dll");
            yield return Path.Combine(directory, $"lib{libraryName}.dll");
            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(directory, $"{libraryName}.dylib");
            yield return Path.Combine(directory, $"lib{libraryName}.dylib");
            yield break;
        }

        yield return Path.Combine(directory, $"{libraryName}.so");
        yield return Path.Combine(directory, $"lib{libraryName}.so");
    }
}
