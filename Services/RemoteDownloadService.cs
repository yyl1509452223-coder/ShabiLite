using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ShabiLite.Services
{
    internal sealed class RemoteDownloadResult
    {
        public bool Success { get; set; }
        public string VideoPath { get; set; }
        public string PreviewPath { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
    }

    internal static class RemoteDownloadService
    {
        private const string ApiKeyHeader = "X-Shabi-Key";

        public static string NormalizeServerUrl(string value)
        {
            var text = (value ?? string.Empty).Trim().TrimEnd('/') + "/";
            Uri uri;
            if (!Uri.TryCreate(text, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("服务器地址必须以 http:// 或 https:// 开头。");
            }

            return uri.AbsoluteUri.TrimEnd('/');
        }

        public static async Task<string> TestConnectionAsync(string serverUrl, string apiKey)
        {
            var baseUri = CreateBaseUri(serverUrl);
            ValidateApiKey(apiKey);

            using (var client = CreateClient(apiKey))
            using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "api/status")))
            using (var response = await client.SendAsync(request))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(await GetErrorMessageAsync(response));
                }

                return "连接成功，下载服务器可以使用。";
            }
        }

        public static async Task<RemoteDownloadResult> DownloadAsync(
            string serverUrl,
            string apiKey,
            string workshopId,
            string destinationDirectory,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            try
            {
                var baseUri = CreateBaseUri(serverUrl);
                ValidateApiKey(apiKey);

                using (var client = CreateClient(apiKey))
                {
                    Report(progress, 5, "正在连接远程下载服务器…");
                    var job = await CreateJobAsync(client, baseUri, workshopId, cancellationToken);

                    while (!string.Equals(job.Status, "ready", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(job.Status, "failed", StringComparison.OrdinalIgnoreCase))
                        {
                            return Failed(job.Message);
                        }

                        Report(
                            progress,
                            Math.Max(5, Math.Min(95, job.Progress)),
                            string.IsNullOrWhiteSpace(job.Message) ? "服务器正在下载壁纸…" : job.Message);
                        await Task.Delay(1000, cancellationToken);
                        job = await GetJobAsync(client, baseUri, workshopId, cancellationToken);
                    }

                    Report(progress, 96, "服务器下载完成，正在传输 MP4…");
                    var videoPath = await DownloadFileAsync(
                        client,
                        new Uri(baseUri, "api/files/" + Uri.EscapeDataString(workshopId)),
                        workshopId,
                        job.Title,
                        destinationDirectory,
                        progress,
                        cancellationToken);
                    var previewPath = await DownloadPreviewAsync(
                        client,
                        new Uri(baseUri, "api/previews/" + Uri.EscapeDataString(workshopId)),
                        videoPath,
                        cancellationToken);

                    return new RemoteDownloadResult
                    {
                        Success = true,
                        VideoPath = videoPath,
                        PreviewPath = previewPath,
                        Title = job.Title,
                        Message = string.IsNullOrWhiteSpace(job.Message) ? "远程壁纸下载完成。" : job.Message
                    };
                }
            }
            catch (OperationCanceledException)
            {
                return Failed("远程下载已取消。");
            }
            catch (Exception exception)
            {
                return Failed("远程下载失败：" + exception.Message);
            }
        }

        private static async Task<RemoteJobResponse> CreateJobAsync(
            HttpClient client,
            Uri baseUri,
            string workshopId,
            CancellationToken cancellationToken)
        {
            var json = "{\"UrlOrId\":\"" + workshopId + "\"}";
            using (var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "api/jobs")))
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                using (var response = await client.SendAsync(request, cancellationToken))
                {
                    return await ReadJobResponseAsync(response);
                }
            }
        }

        private static async Task<RemoteJobResponse> GetJobAsync(
            HttpClient client,
            Uri baseUri,
            string workshopId,
            CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(baseUri, "api/jobs/" + Uri.EscapeDataString(workshopId))))
            using (var response = await client.SendAsync(request, cancellationToken))
            {
                return await ReadJobResponseAsync(response);
            }
        }

        private static async Task<RemoteJobResponse> ReadJobResponseAsync(HttpResponseMessage response)
        {
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(GetErrorMessage(response.StatusCode, json));
            }

            var job = Deserialize<RemoteJobResponse>(json);
            if (job == null || string.IsNullOrWhiteSpace(job.Status))
            {
                throw new InvalidOperationException("服务器返回了无法识别的任务状态。");
            }
            return job;
        }

        private static async Task<string> DownloadFileAsync(
            HttpClient client,
            Uri downloadUri,
            string workshopId,
            string title,
            string destinationDirectory,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, downloadUri))
            using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(await GetErrorMessageAsync(response));
                }

                Directory.CreateDirectory(destinationDirectory);
                var destination = GetAvailableDestinationPath(destinationDirectory, title, workshopId);
                var partialPath = destination + ".download-" + Guid.NewGuid().ToString("N");
                var total = response.Content.Headers.ContentLength;

                try
                {
                    using (var source = await response.Content.ReadAsStreamAsync())
                    using (var target = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        var buffer = new byte[81920];
                        long received = 0;
                        int count;
                        while ((count = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                        {
                            await target.WriteAsync(buffer, 0, count, cancellationToken);
                            received += count;
                            if (total.HasValue && total.Value > 0)
                            {
                                var percent = 96 + (int)Math.Min(3, received * 3L / total.Value);
                                Report(progress, percent, "正在接收壁纸文件…");
                            }
                        }
                    }
                    File.Move(partialPath, destination);
                    return destination;
                }
                catch
                {
                    if (File.Exists(partialPath))
                    {
                        File.Delete(partialPath);
                    }
                    throw;
                }
            }
        }

        private static async Task<string> DownloadPreviewAsync(
            HttpClient client,
            Uri previewUri,
            string videoPath,
            CancellationToken cancellationToken)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, previewUri))
                using (var response = await client.SendAsync(request, cancellationToken))
                {
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return null;
                    }
                    response.EnsureSuccessStatusCode();
                    var mediaType = response.Content.Headers.ContentType == null
                        ? string.Empty
                        : response.Content.Headers.ContentType.MediaType;
                    var extension = string.Equals(mediaType, "image/gif", StringComparison.OrdinalIgnoreCase)
                        ? ".gif"
                        : string.Equals(mediaType, "image/png", StringComparison.OrdinalIgnoreCase)
                            ? ".png"
                            : ".jpg";
                    var previewPath = Path.ChangeExtension(videoPath, extension);
                    using (var source = await response.Content.ReadAsStreamAsync())
                    using (var target = new FileStream(previewPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        await source.CopyToAsync(target, 81920, cancellationToken);
                    }
                    return previewPath;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string GetAvailableDestinationPath(string directory, string title, string workshopId)
        {
            var safeTitle = MakeSafeFileName(title);
            var baseName = safeTitle + " [" + workshopId + "]";
            var candidate = Path.Combine(directory, baseName + ".mp4");
            for (var index = 2; File.Exists(candidate); index++)
            {
                candidate = Path.Combine(directory, baseName + " (" + index + ").mp4");
            }
            return candidate;
        }

        private static string MakeSafeFileName(string value)
        {
            var text = string.IsNullOrWhiteSpace(value) ? "Workshop Wallpaper" : value.Trim();
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            {
                text = text.Replace(invalidCharacter, '_');
            }
            text = text.Trim().TrimEnd('.');
            if (text.Length > 130)
            {
                text = text.Substring(0, 130).TrimEnd();
            }
            return string.IsNullOrWhiteSpace(text) ? "Workshop Wallpaper" : text;
        }

        private static HttpClient CreateClient(string apiKey)
        {
            var client = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });
            client.Timeout = TimeSpan.FromHours(6);
            client.DefaultRequestHeaders.TryAddWithoutValidation(ApiKeyHeader, apiKey.Trim());
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ShabiLite/2.3");
            return client;
        }

        private static Uri CreateBaseUri(string serverUrl)
        {
            return new Uri(NormalizeServerUrl(serverUrl) + "/", UriKind.Absolute);
        }

        private static void ValidateApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("请输入远程服务器访问密钥。");
            }
        }

        private static async Task<string> GetErrorMessageAsync(HttpResponseMessage response)
        {
            return GetErrorMessage(response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        private static string GetErrorMessage(HttpStatusCode statusCode, string json)
        {
            try
            {
                var error = Deserialize<RemoteErrorResponse>(json);
                if (error != null && !string.IsNullOrWhiteSpace(error.Error))
                {
                    return error.Error;
                }
            }
            catch
            {
            }

            if (statusCode == HttpStatusCode.Unauthorized)
            {
                return "访问密钥不正确。";
            }
            return "服务器请求失败（" + (int)statusCode + "）。";
        }

        private static T Deserialize<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return serializer.ReadObject(stream) as T;
            }
        }

        private static RemoteDownloadResult Failed(string message)
        {
            return new RemoteDownloadResult { Success = false, Message = message };
        }

        private static void Report(IProgress<DownloadProgress> progress, int percent, string message)
        {
            progress?.Report(new DownloadProgress { Percent = percent, Message = message });
        }
    }

    [DataContract]
    internal sealed class RemoteJobResponse
    {
        [DataMember(Name = "status")]
        public string Status { get; set; }

        [DataMember(Name = "progress")]
        public int Progress { get; set; }

        [DataMember(Name = "message")]
        public string Message { get; set; }

        [DataMember(Name = "title")]
        public string Title { get; set; }
    }

    [DataContract]
    internal sealed class RemoteErrorResponse
    {
        [DataMember(Name = "error")]
        public string Error { get; set; }
    }
}
