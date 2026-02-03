// Config/FirebaseConfig.cs
namespace DramaBox.Config;

public static class FirebaseConfig
{
    // ===== Firebase (Web App Config) =====
    public static string ApiKey = "AIzaSyCf9XSomMLrhsGyTMBkKtRLNfGQnS3PEFw";
    public static string ProjectId = "dramabox-93e83";

    // Realtime Database (SEM barra final)
    public static string DatabaseUrl = "https://dramabox-93e83-default-rtdb.firebaseio.com";

    // Storage bucket
    public static string StorageBucket = "dramabox-93e83.firebasestorage.app";

    // (Opcional) Mantidos para referência/uso futuro
    public static string MessagingSenderId = "266336995203";
    public static string AppId = "1:266336995203:web:6c375948b16fa6400298b6";

    // ===== Endpoints REST Firebase Auth =====
    public static string SignUpUrl =>
        $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={ApiKey}";

    public static string SignInUrl =>
        $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={ApiKey}";

    public static string GetAccountUrl =>
        $"https://identitytoolkit.googleapis.com/v1/accounts:lookup?key={ApiKey}";

    // ===== Helpers =====
    public static string NormalizeDbUrl(string url)
        => (url ?? "").Trim().TrimEnd('/');

    public static string DbBase => NormalizeDbUrl(DatabaseUrl);

    // Endpoint REST do Firebase Storage (upload/download metadata)
    public static string StorageBase =>
        $"https://firebasestorage.googleapis.com/v0/b/{StorageBucket}/o";

    // Realtime Database root (sem barra no final)
    public static string RealtimeBaseUrl { get; set; } = "https://dramabox-93e83-default-rtdb.firebaseio.com";

    // Se você estiver usando regras abertas pode deixar vazio.
    public static string RealtimeAuthToken { get; set; } = "";
}
