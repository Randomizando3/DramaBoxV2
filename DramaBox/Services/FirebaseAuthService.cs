// Services/FirebaseAuthService.cs
using System.Net.Http.Json;
using System.Text.Json;
using DramaBox.Config;

namespace DramaBox.Services;

public sealed class FirebaseAuthService
{
    private readonly HttpClient _http;

    public FirebaseAuthService(HttpClient http)
    {
        _http = http;
    }

    public async Task<(bool ok, string message, AuthResult? result)> SignUpAsync(string email, string password)
    {
        var payload = new
        {
            email = (email ?? "").Trim(),
            password = password ?? "",
            returnSecureToken = true
        };

        var resp = await _http.PostAsJsonAsync(FirebaseConfig.SignUpUrl, payload);
        var raw = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return (false, MapFirebaseAuthError(raw), null);

        var data = JsonSerializer.Deserialize<AuthResult>(raw, JsonOptions());
        return (true, "OK", data);
    }

    public async Task<(bool ok, string message, AuthResult? result)> SignInAsync(string email, string password)
    {
        var payload = new
        {
            email = (email ?? "").Trim(),
            password = password ?? "",
            returnSecureToken = true
        };

        var resp = await _http.PostAsJsonAsync(FirebaseConfig.SignInUrl, payload);
        var raw = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return (false, MapFirebaseAuthError(raw), null);

        var data = JsonSerializer.Deserialize<AuthResult>(raw, JsonOptions());
        return (true, "OK", data);
    }

    public async Task<(bool ok, string message)> SendPasswordResetAsync(string email)
    {
        var payload = new
        {
            requestType = "PASSWORD_RESET",
            email = (email ?? "").Trim()
        };

        var url = $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={FirebaseConfig.ApiKey}";
        var resp = await _http.PostAsJsonAsync(url, payload);
        var raw = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return (false, MapFirebaseAuthError(raw));

        return (true, "Se o email existir, você receberá as instruções.");
    }

    private static JsonSerializerOptions JsonOptions()
        => new(JsonSerializerDefaults.Web);

    private static string MapFirebaseAuthError(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var code = doc.RootElement.GetProperty("error").GetProperty("message").GetString() ?? "";

            return code switch
            {
                "EMAIL_EXISTS" => "Este email já está em uso.",
                "OPERATION_NOT_ALLOWED" => "Login por senha não habilitado no Firebase.",
                "TOO_MANY_ATTEMPTS_TRY_LATER" => "Muitas tentativas. Tente novamente mais tarde.",
                "EMAIL_NOT_FOUND" => "Email não encontrado.",
                "INVALID_PASSWORD" => "Senha inválida.",
                "USER_DISABLED" => "Usuário desabilitado.",
                "WEAK_PASSWORD : Password should be at least 6 characters" => "Senha fraca (mínimo 6 caracteres).",
                var s when s.StartsWith("WEAK_PASSWORD") => "Senha fraca (mínimo 6 caracteres).",
                "INVALID_EMAIL" => "Email inválido.",
                _ => $"Erro Firebase: {code}"
            };
        }
        catch
        {
            return "Erro ao comunicar com o Firebase.";
        }
    }

    // DTO do Firebase Auth REST
    public sealed class AuthResult
    {
        public string IdToken { get; set; } = "";
        public string Email { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string ExpiresIn { get; set; } = "";
        public string LocalId { get; set; } = "";
        public bool Registered { get; set; }
    }
}
