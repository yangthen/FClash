using System.Net.Http.Headers;
using System.Text;

namespace FClash.Domain;

public sealed class WebDavBackup(HttpClient http)
{
    private static HttpRequestMessage Request(HttpMethod method, string address, string fileName, string user, string password)
    {
        if (!Uri.TryCreate(address.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0
            || uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) throw new InvalidDataException("WebDAV 需要 HTTPS 地址（本机回环可用 HTTP）");
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains('/') || fileName.Contains('\\') || fileName is "." or "..") throw new InvalidDataException("备份文件名无效");
        var request = new HttpRequestMessage(method, new Uri(uri, Uri.EscapeDataString(fileName)));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password)));
        return request;
    }
    public async Task UploadAsync(string address, string fileName, string user, string password, string localFile, CancellationToken token = default)
    {
        using var request = Request(HttpMethod.Put, address, fileName, user, password);
        request.Content = new StreamContent(File.OpenRead(localFile)); request.Content.Headers.ContentType = new("application/zip");
        using var response = await http.SendAsync(request, token); response.EnsureSuccessStatusCode();
    }
    public async Task DownloadAsync(string address, string fileName, string user, string password, string localFile, CancellationToken token = default)
    {
        using var request = Request(HttpMethod.Get, address, fileName, user, password);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
        const long maximum = 512L * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("远程备份超过 512 MiB");
        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(localFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920]; long total = 0; int length;
        while ((length = await source.ReadAsync(buffer, token)) != 0)
        {
            total += length; if (total > maximum) throw new InvalidDataException("远程备份超过 512 MiB");
            await output.WriteAsync(buffer.AsMemory(0, length), token);
        }
    }
}
