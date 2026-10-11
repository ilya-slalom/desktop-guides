using System.Text.Json;

namespace DesktopGuides.Production;

internal interface IExternalLinkLauncher
{
    Task<bool> LaunchAsync(Uri uri);
}

internal static class ExternalLinkLaunchers
{
    // Installed tests record the URL instead of opening a browser on the runner.
    public static IExternalLinkLauncher Create(string cacheRoot) =>
        TestGate.IsOpen(AppLane.Current.LocalEvent("ExternalLaunch", Environment.ProcessId))
            ? new RecordingExternalLinkLauncher(Path.Combine(cacheRoot, "diagnostics", "external-launches.json"))
            : new SystemExternalLinkLauncher();
}

internal sealed class SystemExternalLinkLauncher : IExternalLinkLauncher
{
    public async Task<bool> LaunchAsync(Uri uri) => await Windows.System.Launcher.LaunchUriAsync(uri);
}

internal sealed class RecordingExternalLinkLauncher(string path) : IExternalLinkLauncher
{
    public async Task<bool> LaunchAsync(Uri uri)
    {
        List<string> launched = File.Exists(path)
            ? JsonSerializer.Deserialize<List<string>>(await File.ReadAllTextAsync(path)) ?? []
            : [];
        launched.Add(uri.AbsoluteUri);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(launched));
        return true;
    }
}
