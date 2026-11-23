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

    public static string TempRoot => Path.Combine(Path.GetTempPath(), "SnapKit-update");

    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 3, 3);

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
        string safeTag = SanitizeTag(info.Tag);
        string folder = Path.Combine(TempRoot, safeTag);
        Directory.CreateDirectory(folder);

        string partPath = Path.Combine(folder, $"SnapKit-win-x64-{Guid.NewGuid():N}.part");
        string zipPath = Path.Combine(folder, "SnapKit-win-x64.zip");
        string extractDir = Path.Combine(folder, "unpacked");
        string exePath = Path.Combine(folder, Brand.Name + ".exe");

        progress?.Report(new DownloadProgress("Connecting to GitHub…", null, 0, info.AssetSize > 0 ? info.AssetSize : null));

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SnapKit/" + Display);
            using var response = await http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength ?? (info.AssetSize > 0 ? info.AssetSize : null);
            await using (var remote = await response.Content.ReadAsStreamAsync(token))
            await using (var local = File.Create(partPath))
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

                await local.FlushAsync(token);
                progress?.Report(new DownloadProgress("Downloading " + info.Tag + "…", 100, readTotal, totalBytes ?? readTotal));
            }

            // Atomic commit from .part to final versioned zip
            File.Move(partPath, zipPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(partPath)) File.Delete(partPath); } catch { /* best-effort cleanup */ }
            throw;
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

    public static void CleanupTempDownloads()
    {
        try
        {
            if (!Directory.Exists(TempRoot))
                return;
            string currentProcess = Environment.ProcessPath ?? "";
            foreach (var entry in Directory.EnumerateFileSystemEntries(TempRoot))
            {
                try
                {
                    if (!string.IsNullOrEmpty(currentProcess)
                        && currentProcess.StartsWith(entry, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (Directory.Exists(entry))
                        Directory.Delete(entry, recursive: true);
                    else if (File.Exists(entry))
                        File.Delete(entry);
                }
                catch
                {
                    // Skip locked temp files.
                }
            }
            if (!Directory.EnumerateFileSystemEntries(TempRoot).Any())
                Directory.Delete(TempRoot, recursive: false);
        }
        catch
        {
            // Temp cleanup is best-effort.
        }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:0.0} KB";
        return $"{bytes / (1024.0 * 1024.0):0.1} MB";
    }

    private static string SanitizeTag(string tag)
    {
        var chars = tag.Trim().Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray();
        return chars.Length == 0 ? "latest" : new string(chars);
    }
}

internal static class UpdateCheck
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static CancellationTokenSource? _loopCts;
    private static string? _offeredTag;

    public static ReleaseInfo? Available { get; private set; }
    public static event Action<ReleaseInfo>? UpdateAvailable;

    public static void Queue()
    {
        if (_loopCts != null)
            return;
        _loopCts = new CancellationTokenSource();
        var token = _loopCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), token);
                await RunBackgroundCheck(token);

                using var timer = new PeriodicTimer(Interval);
                while (await timer.WaitForNextTickAsync(token))
                    await RunBackgroundCheck(token);
            }
            catch (OperationCanceledException)
            {
                // App shutting down.
            }
            catch
            {
                // Background checks must never crash the app.
            }
        }, token);
    }

    public static void Stop()
    {
        try
        {
            _loopCts?.Cancel();
            _loopCts?.Dispose();
        }
        catch
        {
            // Already cancelled.
        }
        _loopCts = null;
    }

    public static async Task<ReleaseInfo?> CheckAsync(CancellationToken token = default)
    {
        var info = await ReleaseClient.LatestAsync(token);
        if (info != null && ReleaseClient.IsNewer(info.Version))
            NotifyAvailable(info);
        return info;
    }

    private static async Task RunBackgroundCheck(CancellationToken token)
    {
        try
        {
            var info = await ReleaseClient.LatestAsync(token);
            if (info == null || !ReleaseClient.IsNewer(info.Version))
                return;
            NotifyAvailable(info);

            if (!string.Equals(_offeredTag, info.Tag, StringComparison.OrdinalIgnoreCase))
            {
                _offeredTag = info.Tag;
                var app = System.Windows.Application.Current;
                app?.Dispatcher.BeginInvoke(() => UpdateWindow.Offer(info));
            }
        }
        catch
        {
            // No release, or offline. Keep running silently.
        }
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
