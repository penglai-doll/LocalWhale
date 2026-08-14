using System.Text;

namespace LocalWhale.Core.Runtime;

public sealed class BridgeOverlayMaterializer : IDisposable
{
    private const string Placeholder = "name: '@localwhale/dsh-desktop-bridge'";
    private static readonly string OverlayRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LocalWhale.BridgeOverlays");
    private readonly string _directory;
    private bool _disposed;

    private BridgeOverlayMaterializer(string directory, string path)
    {
        _directory = directory;
        Path = path;
    }

    public string Path { get; }

    public static BridgeOverlayMaterializer Create(string templatePath, string modulePath)
    {
        var fullTemplatePath = System.IO.Path.GetFullPath(templatePath);
        var fullModulePath = System.IO.Path.GetFullPath(modulePath);
        if (!File.Exists(fullTemplatePath)) throw new FileNotFoundException("Bridge overlay template was not found.", fullTemplatePath);
        if (!File.Exists(fullModulePath)) throw new FileNotFoundException("Bridge module was not found.", fullModulePath);

        var template = File.ReadAllText(fullTemplatePath, Encoding.UTF8);
        if (!template.Contains(Placeholder, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Bridge overlay template does not contain the expected module placeholder.");
        }

        var moduleUrl = new Uri(fullModulePath).AbsoluteUri.Replace("'", "%27", StringComparison.Ordinal);
        var directory = System.IO.Path.Combine(OverlayRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "desktop-bridge.yml");
        try
        {
            File.WriteAllText(path, template.Replace(Placeholder, $"name: '{moduleUrl}'", StringComparison.Ordinal), new UTF8Encoding(false));
            return new BridgeOverlayMaterializer(directory, path);
        }
        catch
        {
            TemporaryDirectoryCleaner.DeleteTreeWithin(OverlayRoot, directory);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TemporaryDirectoryCleaner.DeleteTreeWithin(OverlayRoot, _directory);
    }
}
