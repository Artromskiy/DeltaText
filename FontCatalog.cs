namespace Delta.Text;

public readonly record struct FontSource(FontKey Key, string Path);

public sealed class FileFontCatalog
{
    private readonly FontSource[] _sources;

    private FileFontCatalog(FontSource[] sources) => _sources = sources;

    public ReadOnlyMemory<FontSource> Sources => _sources;

    public static FileFontCatalog Scan(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var sources = new List<FontSource>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(path);
                if (!extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".otf", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Path.GetFileNameWithoutExtension(path);
                sources.Add(new FontSource(new FontKey(name, "regular", path), path));
            }
        }
        sources.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path));
        return new FileFontCatalog(sources.ToArray());
    }
}
