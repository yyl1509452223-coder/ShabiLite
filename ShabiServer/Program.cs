using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ShabiLite.Services;

var settingsPath = Path.Combine(AppContext.BaseDirectory, "server-settings.json");
var settings = ServerSettings.LoadOrCreate(settingsPath);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://127.0.0.1:{settings.Port}");
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<WorkshopJobManager>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    if (context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    var suppliedKey = context.Request.Headers["X-Shabi-Key"].ToString();
    if (!ApiKeyMatches(settings.ApiKey, suppliedKey))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "访问密钥无效。" });
        return;
    }

    await next();
});

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "ShabiServer",
    version = "1.0"
}));

app.MapGet("/api/status", () => Results.Ok(new
{
    status = "ok",
    service = "ShabiServer"
}));

app.MapPost("/api/jobs", (WorkshopRequest request, WorkshopJobManager jobs) =>
{
    if (!SteamCmdService.TryExtractWorkshopId(request.UrlOrId, out var workshopId) || workshopId == null)
    {
        return Results.BadRequest(new { error = "请输入有效的创意工坊链接或 Workshop ID。" });
    }

    var job = jobs.Enqueue(workshopId);
    return Results.Accepted($"/api/jobs/{workshopId}", job.ToResponse());
});

app.MapGet("/api/jobs/{workshopId}", (string workshopId, WorkshopJobManager jobs) =>
{
    if (!WorkshopJobManager.IsValidWorkshopId(workshopId))
    {
        return Results.BadRequest(new { error = "Workshop ID 格式无效。" });
    }

    var job = jobs.Find(workshopId);
    return job == null
        ? Results.NotFound(new { error = "没有找到这个下载任务。" })
        : Results.Ok(job.ToResponse());
});

app.MapGet("/api/files/{workshopId}", (string workshopId, WorkshopJobManager jobs) =>
{
    if (!WorkshopJobManager.IsValidWorkshopId(workshopId))
    {
        return Results.BadRequest(new { error = "Workshop ID 格式无效。" });
    }

    var job = jobs.Find(workshopId);
    if (job == null || job.Status != "ready" || string.IsNullOrWhiteSpace(job.FilePath) || !File.Exists(job.FilePath))
    {
        return Results.NotFound(new { error = "壁纸文件尚未准备好。" });
    }

    return Results.File(
        job.FilePath,
        "video/mp4",
        Path.GetFileName(job.FilePath),
        enableRangeProcessing: true);
});

app.MapGet("/api/previews/{workshopId}", (string workshopId, WorkshopJobManager jobs) =>
{
    if (!WorkshopJobManager.IsValidWorkshopId(workshopId))
    {
        return Results.BadRequest(new { error = "Workshop ID 格式无效。" });
    }

    var job = jobs.Find(workshopId);
    if (job == null || job.Status != "ready" || string.IsNullOrWhiteSpace(job.PreviewPath) || !File.Exists(job.PreviewPath))
    {
        return Results.NotFound(new { error = "这个壁纸没有可用的静态预览图。" });
    }

    var contentType = Path.GetExtension(job.PreviewPath).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".jpeg" => "image/jpeg",
        _ => "image/jpeg"
    };
    return Results.File(job.PreviewPath, contentType, Path.GetFileName(job.PreviewPath));
});

Console.WriteLine($"鲨壁下载服务器已启动：http://127.0.0.1:{settings.Port}");
Console.WriteLine($"配置文件：{settingsPath}");
Console.WriteLine("访问密钥保存在配置文件中，请勿上传到公开仓库。");
app.Run();

static bool ApiKeyMatches(string expected, string supplied)
{
    if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(supplied))
    {
        return false;
    }

    var expectedBytes = Encoding.UTF8.GetBytes(expected);
    var suppliedBytes = Encoding.UTF8.GetBytes(supplied.Trim());
    return expectedBytes.Length == suppliedBytes.Length &&
           CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
}

internal sealed class WorkshopRequest
{
    public string? UrlOrId { get; set; }
}

internal sealed class ServerSettings
{
    public int Port { get; set; } = 38523;
    public string ApiKey { get; set; } = string.Empty;
    public string SteamCmdPath { get; set; } = string.Empty;
    public string SteamUserName { get; set; } = SteamCmdService.DefaultSteamUserName;
    public string CacheDirectory { get; set; } = "Cache";

    public static ServerSettings LoadOrCreate(string path)
    {
        ServerSettings? settings = null;
        if (File.Exists(path))
        {
            try
            {
                settings = JsonSerializer.Deserialize<ServerSettings>(
                    File.ReadAllText(path, Encoding.UTF8),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                settings = null;
            }
        }

        settings ??= new ServerSettings();
        settings.Port = settings.Port is >= 1024 and <= 65535 ? settings.Port : 38523;
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || settings.ApiKey.Length < 32)
        {
            settings.ApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        }

        settings.SteamUserName = SteamCmdService.IsValidSteamUserName(settings.SteamUserName)
            ? settings.SteamUserName.Trim()
            : SteamCmdService.DefaultSteamUserName;
        settings.SteamCmdPath = SteamCmdService.FindSteamCmd(settings.SteamCmdPath) ?? settings.SteamCmdPath;
        settings.CacheDirectory = string.IsNullOrWhiteSpace(settings.CacheDirectory)
            ? "Cache"
            : settings.CacheDirectory.Trim();

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
        return settings;
    }

    public string GetCacheDirectory()
    {
        return Path.GetFullPath(Path.IsPathRooted(CacheDirectory)
            ? CacheDirectory
            : Path.Combine(AppContext.BaseDirectory, CacheDirectory));
    }
}

internal sealed class WorkshopJob
{
    public string WorkshopId { get; init; } = string.Empty;
    public string Status { get; set; } = "queued";
    public int Progress { get; set; }
    public string Message { get; set; } = "任务已进入队列。";
    public string? Title { get; set; }
    public string? FilePath { get; set; }
    public string? PreviewPath { get; set; }

    public object ToResponse()
    {
        return new
        {
            id = WorkshopId,
            status = Status,
            progress = Progress,
            message = Message,
            title = Title,
            fileName = string.IsNullOrWhiteSpace(FilePath) ? null : Path.GetFileName(FilePath),
            downloadUrl = Status == "ready" ? $"/api/files/{WorkshopId}" : null,
            previewUrl = Status == "ready" && !string.IsNullOrWhiteSpace(PreviewPath)
                ? $"/api/previews/{WorkshopId}"
                : null
        };
    }
}

internal sealed class WorkshopJobManager
{
    private static readonly Regex WorkshopIdPattern = new("^\\d{6,20}$", RegexOptions.Compiled);
    private readonly ConcurrentDictionary<string, WorkshopJob> _jobs = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    private readonly ServerSettings _settings;
    private readonly string _cacheDirectory;

    public WorkshopJobManager(ServerSettings settings)
    {
        _settings = settings;
        _cacheDirectory = settings.GetCacheDirectory();
        Directory.CreateDirectory(_cacheDirectory);
    }

    public static bool IsValidWorkshopId(string value)
    {
        return WorkshopIdPattern.IsMatch(value ?? string.Empty);
    }

    public WorkshopJob Enqueue(string workshopId)
    {
        var cached = FindCached(workshopId);
        if (cached != null)
        {
            _jobs[workshopId] = cached;
            return cached;
        }

        if (_jobs.TryGetValue(workshopId, out var existing) && existing.Status != "failed")
        {
            return existing;
        }

        var job = new WorkshopJob { WorkshopId = workshopId };
        _jobs[workshopId] = job;
        _ = Task.Run(() => DownloadAsync(job));
        return job;
    }

    public WorkshopJob? Find(string workshopId)
    {
        if (_jobs.TryGetValue(workshopId, out var job))
        {
            return job;
        }

        var cached = FindCached(workshopId);
        if (cached != null)
        {
            _jobs[workshopId] = cached;
        }
        return cached;
    }

    private WorkshopJob? FindCached(string workshopId)
    {
        var directory = Path.Combine(_cacheDirectory, workshopId);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var file = Directory.GetFiles(directory, "*.mp4", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        var preview = FindOrCopyPreview(workshopId, directory);
        return file == null
            ? null
            : new WorkshopJob
            {
                WorkshopId = workshopId,
                Status = "ready",
                Progress = 100,
                Message = "已从服务器缓存中找到壁纸。",
                Title = Path.GetFileNameWithoutExtension(file),
                FilePath = file,
                PreviewPath = preview
            };
    }

    private async Task DownloadAsync(WorkshopJob job)
    {
        await _downloadGate.WaitAsync();
        try
        {
            job.Status = "downloading";
            job.Progress = 3;
            job.Message = "正在准备 SteamCMD 下载…";

            var steamCmdPath = SteamCmdService.FindSteamCmd(_settings.SteamCmdPath);
            if (steamCmdPath == null)
            {
                Fail(job, "服务器没有找到 steamcmd.exe，请修改 server-settings.json。");
                return;
            }

            var progress = new Progress<SteamCmdProgress>(update =>
            {
                job.Progress = update.Percent;
                job.Message = update.Message;
            });
            var result = await SteamCmdService.DownloadAsync(
                steamCmdPath,
                job.WorkshopId,
                _settings.SteamUserName,
                progress);

            if (!result.Success || string.IsNullOrWhiteSpace(result.VideoPath) || !File.Exists(result.VideoPath))
            {
                Fail(job, result.Message);
                return;
            }

            var title = string.IsNullOrWhiteSpace(result.Title)
                ? Path.GetFileNameWithoutExtension(result.VideoPath)
                : result.Title.Trim();
            var directory = Path.Combine(_cacheDirectory, job.WorkshopId);
            Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory, SanitizeFileName(title) + ".mp4");
            if (!string.Equals(Path.GetFullPath(result.VideoPath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(result.VideoPath, destination, true);
            }

            var previewPath = CopyPreviewToCache(result.WorkshopDirectory, directory);

            job.Title = title;
            job.FilePath = destination;
            job.PreviewPath = previewPath;
            job.Status = "ready";
            job.Progress = 100;
            job.Message = "服务器下载完成，可以获取壁纸。";
        }
        catch (Exception exception)
        {
            Fail(job, "服务器下载失败：" + exception.Message);
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    private static void Fail(WorkshopJob job, string message)
    {
        job.Status = "failed";
        job.Progress = 100;
        job.Message = string.IsNullOrWhiteSpace(message) ? "服务器下载失败。" : message;
    }

    private string? FindOrCopyPreview(string workshopId, string cacheDirectory)
    {
        var cachedPreview = FindPreviewFile(cacheDirectory);
        if (cachedPreview != null)
        {
            return cachedPreview;
        }

        var steamCmdPath = SteamCmdService.FindSteamCmd(_settings.SteamCmdPath);
        if (steamCmdPath == null)
        {
            return null;
        }

        var workshopDirectory = Path.Combine(
            Path.GetDirectoryName(steamCmdPath)!,
            "steamapps", "workshop", "content", "431960", workshopId);
        return CopyPreviewToCache(workshopDirectory, cacheDirectory);
    }

    private static string? CopyPreviewToCache(string? workshopDirectory, string cacheDirectory)
    {
        var source = FindPreviewFile(workshopDirectory);
        if (source == null)
        {
            return null;
        }

        Directory.CreateDirectory(cacheDirectory);
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension != ".jpg" && extension != ".jpeg" && extension != ".png" && extension != ".gif")
        {
            extension = ".jpg";
        }
        var destination = Path.Combine(cacheDirectory, "preview" + extension);
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(source, destination, true);
        }
        return destination;
    }

    private static string? FindPreviewFile(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        return Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var extension = Path.GetExtension(path);
                return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                       extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                       extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                       extension.Equals(".gif", StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(path => Path.GetFileName(path).Equals("preview.jpg", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(path => new FileInfo(path).Length)
            .FirstOrDefault();
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        if (safe.Length > 100)
        {
            safe = safe[..100].Trim();
        }
        return string.IsNullOrWhiteSpace(safe) ? "wallpaper" : safe;
    }
}
