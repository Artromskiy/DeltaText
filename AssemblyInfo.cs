using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
[assembly: SuppressMessage(
    "Security",
    "CA5393:Do not use unsafe DllImportSearchPath values",
    Justification = "DeltaText ships HarfBuzz and the MSDF bridge beside the managed assembly; assembly-directory resolution is required and current-directory/PATH probing is not used.")]
