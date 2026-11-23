using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace SnapKit;

internal sealed record ReleaseInfo(
    Version Version,
    string AssetUrl,
    string Tag,
    string Notes = "",
    string HtmlUrl = "",
    long AssetSize = 0);

internal readonly record struct DownloadProgress(
    string Stage,
    double? Percent,
    long BytesRead,
    long? TotalBytes);

internal static class ReleaseClient
{
    public const string Owner = "d1same";
    public const string Repo = "SnapKit";

    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 2, 0);

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
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SnapKit/" + Display);
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

        var notes = root.TryGetProperty("body", out var bodyValue) ? bodyValue.GetString() ?? "" : "";
        var htmlUrl = root.TryGetProperty("html_url", out var htmlValue)
            ? htmlValue.GetString() ?? ""
            : $"https://github.com/{Owner}/{Repo}/releases/tag/{tag}";

        if (!root.TryGetProperty("assets", out var assets))
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var urlValue) ? urlValue.GetString() : null;
            long size = asset.TryGetProperty("size", out var sizeValue) && sizeValue.TryGetInt64(out var parsedSize)
                ? parsedSize
                : 0;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                continue;
            if (name.Contains("win-x64", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return new ReleaseInfo(version, url, tag, notes, htmlUrl, size);
        }

        return null;
    }

    public static async Task<string> DownloadAsync(
        ReleaseInfo info,
        IProgress<DownloadProgress>? progress,
        CancellationToken token = default)
    {
        var folder = Path.Combine(Path.GetTempPath(), "SnapKit-update");
        Directory.CreateDirectory(folder);
        var zipPath = Path.Combine(folder, "SnapKit-win-x64.zip");
        var extractDir = Path.Combine(folder, "unpacked");
        var exePath = Path.Combine(folder, Brand.Name + ".exe");

        progress?.Report(new DownloadProgress("Connecting to GitHub…", null, 0, info.AssetSize > 0 ? info.AssetSize : null));

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SnapKit/" + Display);
        using var response = await http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength ?? (info.AssetSize > 0 ? info.AssetSize : null);
        await using (var remote = await response.Content.ReadAsStreamAsync(token))
        await using (var local = File.Create(zipPath))
        {
            var buffer = new byte[65536];
            long readTotal = 0;
            long lastReportAt = 0;
            int lastPercent = -1;

            int read;
            while ((read = await remote.ReadAsync(buffer, token)) > 0)
            {
                await local.WriteAsync(buffer.AsMemory(0, read), token);
                readTotal += read;

                double? pct = totalBytes is > 0
                    ? Math.Clamp(readTotal * 100.0 / totalBytes.Value, 0, 100)
                    : null;
                int wholePct = pct.HasValue ? (int)Math.Floor(pct.Value) : -1;
                long now = Environment.TickCount64;
                if (now - lastReportAt >= 50 || wholePct != lastPercent)
                {
                    lastReportAt = now;
                    lastPercent = wholePct;
                    progress?.Report(new DownloadProgress("Downloading " + info.Tag + "…", pct, readTotal, totalBytes));
                }
            }

            progress?.Report(new DownloadProgress("Downloading " + info.Tag + "…", 100, readTotal, totalBytes ?? readTotal));
        }

        progress?.Report(new DownloadProgress("Unpacking update…", null, 0, null));
        if (Directory.Exists(extractDir))
        {
            try { Directory.Delete(extractDir, recursive: true); } catch { /* overwrite below */ }
        }
        Directory.CreateDirectory(extractDir);
        ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
        string? found = Directory.EnumerateFiles(extractDir, Brand.Name + ".exe", SearchOption.AllDirectories).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(found))
            throw new InvalidOperationException("The update did not contain " + Brand.Name + ".exe.");
        File.Copy(found, exePath, overwrite: true);
        return exePath;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:0.0} KB";
        return $"{bytes / (1024.0 * 1024.0):0.1} MB";
    }
}

internal static class UpdateCheck
{
    public static ReleaseInfo? Available { get; private set; }
    public static event Action<ReleaseInfo>? UpdateAvailable;

    public static void Queue()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var info = await ReleaseClient.LatestAsync();
                if (info == null || !ReleaseClient.IsNewer(info.Version))
                    return;
                NotifyAvailable(info);
                var app = System.Windows.Application.Current;
                app?.Dispatcher.BeginInvoke(() => UpdateWindow.Offer(info));
            }
            catch
            {
                // No release, or the network is blocked. The installed version keeps running.
            }
        });
    }

    public static async Task<ReleaseInfo?> CheckAsync(CancellationToken token = default)
    {
        var info = await ReleaseClient.LatestAsync(token);
        if (info != null && ReleaseClient.IsNewer(info.Version))
            NotifyAvailable(info);
        return info;
    }

    private static void NotifyAvailable(ReleaseInfo info)
    {
        Available = info;
        var app = System.Windows.Application.Current;
        if (app != null)
            app.Dispatcher.BeginInvoke(() => UpdateAvailable?.Invoke(info));
        else
            UpdateAvailable?.Invoke(info);
    }
}
