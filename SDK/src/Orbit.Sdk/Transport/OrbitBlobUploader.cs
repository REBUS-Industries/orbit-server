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
    public async Task<Dictionary<string, string>> UploadAsync(
        IReadOnlyDictionary<string, string> hashToFilePath,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>();
        foreach (var (hashHex, filePath) in hashToFilePath)
        {
            ct.ThrowIfCancellationRequested();
            if (result.ContainsKey(hashHex))
                continue;

            if (!File.Exists(filePath))
                continue;

            try
            {
                var bytes = await File.ReadAllBytesAsync(filePath, ct);
                var computed = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!string.Equals(computed, hashHex, StringComparison.OrdinalIgnoreCase))
                    continue;

                using var form = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(bytes);
                fileContent.Headers.ContentType =
                    new MediaTypeHeaderValue(GuessContentType(filePath));
                form.Add(fileContent, "files", $"{hashHex}{Path.GetExtension(filePath)}");

                var resp = await _http.PostAsync(_uploadUrl, form, ct);
                if (!resp.IsSuccessStatusCode)
                    continue;

                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var blobId = doc.RootElement
                    .GetProperty("uploadResults")[0]
                    .GetProperty("blobId")
                    .GetString();

                if (!string.IsNullOrEmpty(blobId))
                    result[hashHex] = blobId;
            }
            catch
            {
                // Skip failed blobs — send continues without that texture.
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
