using System.Net.Http.Headers;
using System.Text.Json;
using DramaBox.Config;

namespace DramaBox.Services;

public sealed class FirebaseStorageService
{
    private readonly HttpClient _http;

    public FirebaseStorageService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
    }

    public async Task<string> UploadUserProfilePhotoAsync(string userId, string localFilePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId inválido.", nameof(userId));

        if (string.IsNullOrWhiteSpace(localFilePath) || !File.Exists(localFilePath))
            throw new FileNotFoundException("Arquivo da foto não encontrado.", localFilePath);

        // Sempre sobrescreve (mesmo caminho) -> atualiza a foto do perfil
        var objectPath = $"usuarios/{userId}/profile/foto.jpg";

        // Endpoint de upload simples (media)
        var uploadUrl =
            $"{FirebaseConfig.StorageBase}?uploadType=media&name={Uri.EscapeDataString(objectPath)}";

        var bytes = await File.ReadAllBytesAsync(localFilePath, ct);

        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

        using var res = await _http.PostAsync(uploadUrl, content, ct);
        var json = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
            throw new Exception($"Falha ao enviar foto para o Storage: {json}");

        // Resposta do Firebase inclui downloadTokens
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var downloadTokens = root.TryGetProperty("downloadTokens", out var tokenEl)
            ? tokenEl.GetString()
            : null;

        // Alguns retornam múltiplos tokens separados por vírgula
        var token = (downloadTokens ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();

        // URL pública (alt=media + token)
        var downloadUrl =
            $"{FirebaseConfig.StorageBase}/{Uri.EscapeDataString(objectPath)}?alt=media";

        if (!string.IsNullOrWhiteSpace(token))
            downloadUrl += $"&token={Uri.EscapeDataString(token)}";

        return downloadUrl;
    }
}
