using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Orbit.Sdk.Transport;

/// <summary>
/// Uploads texture image files to the Speckle-compatible blob REST endpoint
/// (<c>POST /api/stream/{streamId}/blob</c>). Returns a local SHA-256 hex hash
/// → server-assigned short blob id map for patching <see cref="Objects.Other.RenderMaterial"/>
/// texture fields before object serialisation.
/// </summary>
public sealed class OrbitBlobUploader : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _uploadUrl;

    public OrbitBlobUploader(string serverUrl, string streamId, string authToken)
    {
        var baseUrl = serverUrl.TrimEnd('/');
        _uploadUrl = $"{baseUrl}/api/stream/{streamId}/blob";
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", authToken);
    }

    /// <summary>
    /// Upload every unique file in <paramref name="hashToFilePath"/>.
    /// Keys are lowercase SHA-256 hex digests of file bytes.
    /// </summary>
    /// <param name="hashToFilePath">SHA-256 hex digest → absolute file path of the texture image to upload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="log">
    /// Optional diagnostic sink. Receives one line per upload attempt with
    /// status (200/non-success/exception) and the assigned server blob id.
    /// </param>
    public async Task<Dictionary<string, string>> UploadAsync(
        IReadOnlyDictionary<string, string> hashToFilePath,
        CancellationToken ct = default,
        Action<string>? log = null)
    {
        var result = new Dictionary<string, string>();
        foreach (var (hashHex, filePath) in hashToFilePath)
        {
            ct.ThrowIfCancellationRequested();
            if (result.ContainsKey(hashHex))
                continue;

            var shortHash = hashHex.Length >= 16 ? hashHex.Substring(0, 16) : hashHex;

            if (!File.Exists(filePath))
            {
                log?.Invoke($"[BlobUploader] {shortHash}… SKIP — file does not exist: {filePath}");
                continue;
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(filePath, ct);
                var computed = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!string.Equals(computed, hashHex, StringComparison.OrdinalIgnoreCase))
                {
                    log?.Invoke(
                        $"[BlobUploader] {shortHash}… SKIP — hash mismatch (computed " +
                        $"{computed.Substring(0, 16)}…) file changed between extract and upload? path={filePath}");
                    continue;
                }

                using var form = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(bytes);
                fileContent.Headers.ContentType =
                    new MediaTypeHeaderValue(GuessContentType(filePath));
                form.Add(fileContent, "files", $"{hashHex}{Path.GetExtension(filePath)}");

                var resp = await _http.PostAsync(_uploadUrl, form, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = string.Empty;
                    try { body = await resp.Content.ReadAsStringAsync(ct); } catch { }
                    log?.Invoke(
                        $"[BlobUploader] {shortHash}… HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} " +
                        $"from {_uploadUrl}: {body}");
                    continue;
                }

                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var blobId = doc.RootElement
                    .GetProperty("uploadResults")[0]
                    .GetProperty("blobId")
                    .GetString();

                if (!string.IsNullOrEmpty(blobId))
                {
                    result[hashHex] = blobId;
                    log?.Invoke(
                        $"[BlobUploader] {shortHash}… HTTP 200, blobId='{blobId}' ({bytes.Length}B)");
                }
                else
                {
                    log?.Invoke(
                        $"[BlobUploader] {shortHash}… HTTP 200 but server returned empty blobId. " +
                        $"Raw response: {json}");
                }
            }
            catch (Exception ex)
            {
                log?.Invoke(
                    $"[BlobUploader] {shortHash}… EXCEPTION {ex.GetType().Name}: {ex.Message} " +
                    $"(file: {filePath})");
            }
        }

        return result;
    }

    private static string GuessContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp"            => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            ".webp"           => "image/webp",
            _                 => "image/png",
        };
    }

    public void Dispose() => _http.Dispose();
}
