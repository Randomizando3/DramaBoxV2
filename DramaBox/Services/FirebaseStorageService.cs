// Services/FirebaseStorageService.cs
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using DramaBox.Config;

namespace DramaBox.Services;

public sealed class FirebaseStorageService
{
    private readonly HttpClient _http;

    public FirebaseStorageService(HttpClient http)
    {
        _http = http;
    }

    private static string Bucket => FirebaseConfig.StorageBucket;
    private static string ApiKey => FirebaseConfig.ApiKey;

    // ==========================
    // Upload genérico (helper)
    // ==========================
    private async Task<(bool ok, string url, string message)> UploadAsync(
        Stream stream,
        string objectPath,
        string contentType,
        string? idToken = null
    )
    {
        if (stream == null)
            return (false, "", "Stream inválido.");

        if (string.IsNullOrWhiteSpace(objectPath))
            return (false, "", "objectPath inválido.");

        try
        {
            // Firebase Storage upload (REST)
            // POST https://firebasestorage.googleapis.com/v0/b/{bucket}/o?name={objectPath}
            var url = $"https://firebasestorage.googleapis.com/v0/b/{Bucket}/o?name={Uri.EscapeDataString(objectPath)}";

            // se quiser proteger por token (regras), passe auth header
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("X-Goog-Api-Key", ApiKey);

            if (!string.IsNullOrWhiteSpace(idToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

            using var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            req.Content = content;

            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return (false, "", $"Falha upload. HTTP {(int)resp.StatusCode}.");

            var json = await resp.Content.ReadAsStringAsync();

            // resposta tem name + downloadTokens
            // url de download:
            // https://firebasestorage.googleapis.com/v0/b/{bucket}/o/{object}?alt=media&token={token}
            using var doc = JsonDocument.Parse(json);

            var name = doc.RootElement.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : objectPath;

            string token = "";
            if (doc.RootElement.TryGetProperty("downloadTokens", out var tokEl))
                token = tokEl.GetString() ?? "";

            // se não veio token (raro), ainda dá pra montar sem token se regras permitirem
            var downloadUrl =
                string.IsNullOrWhiteSpace(token)
                    ? $"https://firebasestorage.googleapis.com/v0/b/{Bucket}/o/{Uri.EscapeDataString(name ?? objectPath)}?alt=media"
                    : $"https://firebasestorage.googleapis.com/v0/b/{Bucket}/o/{Uri.EscapeDataString(name ?? objectPath)}?alt=media&token={Uri.EscapeDataString(token)}";

            return (true, downloadUrl, "OK");
        }
        catch (Exception ex)
        {
            return (false, "", $"Erro upload: {ex.Message}");
        }
    }

    // ==========================
    // Seu método existente
    // ==========================
    public async Task<(bool ok, string url, string message)> UploadProfilePhotoAsync(
        Stream stream,
        string userId,
        string? idToken = null
    )
    {
        var objectPath = $"users/{userId}/profile.jpg";
        return await UploadAsync(stream, objectPath, "image/jpeg", idToken);
    }

    // ==========================
    // COMMUNITY - uploads
    // ==========================

    public async Task<(bool ok, string url, string message)> UploadCommunitySeriesCoverAsync(
        Stream stream,
        string creatorUid,
        string seriesId,
        string extension, // ".jpg" ".png"
        string? idToken = null
    )
    {
        extension = (extension ?? "").Trim().ToLowerInvariant();
        var contentType = extension == ".png" ? "image/png" : "image/jpeg";
        var ext = extension == ".png" ? ".png" : ".jpg";

        var objectPath = $"community/{creatorUid}/series/{seriesId}/cover{ext}";
        return await UploadAsync(stream, objectPath, contentType, idToken);
    }

    public async Task<(bool ok, string url, string message)> UploadCommunityEpisodeMp4Async(
        Stream stream,
        string creatorUid,
        string seriesId,
        string episodeId,
        string? idToken = null
    )
    {
        // mp4 obrigatório
        var objectPath = $"community/{creatorUid}/series/{seriesId}/episodes/{episodeId}.mp4";
        return await UploadAsync(stream, objectPath, "video/mp4", idToken);
    }
}
