namespace NovaLite.Core.Plugins;

/// <summary>Minimal interface for future NovaLite plugins.</summary>
public interface IPlugin
{
    string Name { get; }
    string Description { get; }
    Version Version { get; }
    Task InitializeAsync(CancellationToken ct = default);
    Task ShutdownAsync();
}
