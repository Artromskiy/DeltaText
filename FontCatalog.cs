namespace Delta.Text;

/// <summary>A discovered font face and its source path.</summary>
/// <param name="Key">The stable font identity.</param>
/// <param name="Path">The source file path.</param>
public readonly record struct FontSource(FontKey Key, string Path);

/// <summary>Discovers TrueType and OpenType files below configured roots.</summary>
public sealed class FileFontCatalog
{
    private readonly FontSource[] _sources;

    private FileFontCatalog(FontSource[] sources) => _sources = sources;

    /// <summary>The discovered font sources sorted by path.</summary>
    public ReadOnlyMemory<FontSource> Sources => _sources;

    /// <summary>Scans directories for font files.</summary>
    /// <param name="roots">The directories to search recursively.</param>
    /// <returns>A catalog containing all discovered font files.</returns>
    public static FileFontCatalog Scan(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var sources = new List<FontSource>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(path);
                if (!extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".otf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(path);
                sources.Add(new FontSource(new FontKey(name, "regular", path), path));
            }
        }
        sources.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path));
        return new FileFontCatalog(sources.ToArray());
    }
}
