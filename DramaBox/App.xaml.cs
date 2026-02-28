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

        MainPage = new ContentPage(); // placeholder
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
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

                var profile = await _db.GetUserProfileAsync(result.LocalId,
                                                            result.IdToken);

                _session.SetProfile(profile);

                MainPage = new AppShell();
                return;
            }
        }

        MainPage = new NavigationPage(new LoginView());
    }
}