using Microsoft.Extensions.Logging;
using DramaBox.Views;
using DramaBox.Services;

#if WINDOWS
using Microsoft.Maui.LifecycleEvents;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;
#endif

namespace DramaBox
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // =========================
            // DI
            // =========================
            builder.Services.AddSingleton(new HttpClient());
            builder.Services.AddSingleton<SessionService>();
            builder.Services.AddSingleton<FirebaseAuthService>();
            builder.Services.AddSingleton<FirebaseDatabaseService>();

            builder.Services.AddTransient<LoginView>();
            builder.Services.AddTransient<RegisterView>();
            builder.Services.AddTransient<MainPage>();

#if WINDOWS
            builder.ConfigureLifecycleEvents(events =>
            {
                events.AddWindows(windows =>
                {
                    windows.OnWindowCreated(window =>
                    {
                        const int targetWidth = 382;
                        const int targetHeight = 680;

                        var nativeWindow = window;
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
                        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);

                        var appWindow = AppWindow.GetFromWindowId(windowId);
                        if (appWindow is null)
                            return;

                        // Define tamanho
                        appWindow.Resize(new SizeInt32(targetWidth, targetHeight));

                        var presenter = appWindow.Presenter as OverlappedPresenter;
                        if (presenter != null)
                        {
                            presenter.IsResizable = false;
                            presenter.IsMaximizable = false;
                        }

                        try
                        {
                            appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                            appWindow.Title = "DramaBox (Mobile Preview)";
                        }
                        catch { }
                    });
                });
            });
#endif

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
