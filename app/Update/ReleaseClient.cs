using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace SnapKit;

internal sealed record ReleaseInfo(Version Version, string AssetUrl, string Tag);

internal static class ReleaseClient
{
    public const string Owner = "d1same";
    public const string Repo = "SnapKit";

    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 7);

    public static string Display
    {
        get
        {
            var version = Current;
            return version.Build < 0
                ? $"{version.Major}.{version.Minor}"
                : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public static bool IsNewer(Version remote)
    {
        var current = Current;
        var left = new Version(remote.Major, remote.Minor, Math.Max(0, remote.Build));
        var right = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
        return left > right;
    }

    public static async Task<ReleaseInfo?> LatestAsync(CancellationToken token = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SnapKit");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.GetAsync(
            $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest",
            token);
        if (!response.IsSuccessStatusCode)
            return null;

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag) || !Version.TryParse(tag.Trim().TrimStart('v', 'V').Split('-', '+')[0], out var version))
            return null;

        if (!root.TryGetProperty("assets", out var assets))
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var urlValue) ? urlValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                continue;
            if (name.Contains("win-x64", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return new ReleaseInfo(version, url, tag);
        }

        return null;
    }

    public static async Task<string> DownloadAsync(ReleaseInfo info, IProgress<string>? progress, CancellationToken token = default)
    {
        var folder = Path.Combine(Path.GetTempPath(), "SnapKit-update");
        Directory.CreateDirectory(folder);
        var zipPath = Path.Combine(folder, "SnapKit-win-x64.zip");
        var exePath = Path.Combine(folder, Brand.Name + ".exe");
        progress?.Report("Downloading " + info.Tag + "…");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SnapKit");
        await using (var remote = await http.GetStreamAsync(info.AssetUrl, token))
        await using (var local = File.Create(zipPath))
            await remote.CopyToAsync(local, token);

        progress?.Report("Unpacking…");
        ZipFile.ExtractToDirectory(zipPath, folder, overwriteFiles: true);
        string? found = Directory.EnumerateFiles(folder, Brand.Name + ".exe", SearchOption.AllDirectories).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(found))
            throw new InvalidOperationException("The update did not contain " + Brand.Name + ".exe.");
        if (!string.Equals(Path.GetFullPath(found), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase))
            File.Copy(found, exePath, overwrite: true);
        return exePath;
    }
}

internal static class UpdateCheck
{
    public static void Queue()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var info = await ReleaseClient.LatestAsync();
                if (info == null || !ReleaseClient.IsNewer(info.Version))
                    return;
                var app = System.Windows.Application.Current;
                app?.Dispatcher.Invoke(() => UpdateWindow.Offer(info));
            }
            catch
            {
                // No release, or the network is blocked. The installed version keeps running.
            }
        });
    }
}
