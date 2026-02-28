using DramaBox.Services;
using DramaBox.Views;

namespace DramaBox;

public partial class App : Application
{
    private readonly SessionService _session;
    private readonly FirebaseAuthService _auth;
    private readonly FirebaseDatabaseService _db;

    public App(SessionService session,
               FirebaseAuthService auth,
               FirebaseDatabaseService db)
    {
        InitializeComponent();

        _session = session;
        _auth = auth;
        _db = db;

        // Um loading simples enquanto inicializa (pode ser qualquer página sua)
        MainPage = new ContentPage
        {
            Content = new ActivityIndicator
            {
                IsRunning = true,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center
            }
        };

        // Garante que o InitializeAsync rode depois que o app já tem Dispatcher/handler
        Dispatcher.Dispatch(async () => await InitializeAsync());
    }

    private async Task InitializeAsync()
    {
        try
        {
            var (email, password) = _session.GetCredentials();

            if (!string.IsNullOrWhiteSpace(email) &&
                !string.IsNullOrWhiteSpace(password))
            {
                var (ok, _, result) = await _auth.SignInAsync(email, password);

                if (ok && result != null)
                {
                    _session.SetSession(result.IdToken,
                                        result.RefreshToken,
                                        result.LocalId,
                                        result.Email);

                    var profile = await _db.GetUserProfileAsync(result.LocalId, result.IdToken);
                    _session.SetProfile(profile);

                    await SetRootAsync(new AppShell());
                    return;
                }
            }
        }
        catch
        {
            // se quiser, loga aqui
        }

        await SetRootAsync(new NavigationPage(new LoginView()));
    }

    private static Task SetRootAsync(Page root)
        => MainThread.InvokeOnMainThreadAsync(() =>
        {
            Application.Current!.MainPage = root;
        });
}