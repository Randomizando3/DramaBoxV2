using System.Net.Http;
using Microsoft.Extensions.Logging;

using DramaBox.Views;
using DramaBox.Services;

using CommunityToolkit.Maui;

#if WINDOWS
using Microsoft.Maui.LifecycleEvents;          // ✅ necessário pro ConfigureLifecycleEvents aparecer
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
                .UseMauiCommunityToolkit()
                .UseMauiCommunityToolkitMediaElement()
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

                        try
                        {
                            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                            var appWindow = AppWindow.GetFromWindowId(windowId);
                            if (appWindow is null) return;

                            appWindow.Resize(new SizeInt32(targetWidth, targetHeight));

                            if (appWindow.Presenter is OverlappedPresenter presenter)
                            {
                                presenter.IsResizable = false;
                                presenter.IsMaximizable = false;
                            }

                            appWindow.Title = "DramaBox (Mobile Preview)";
                        }
                        catch
                        {
                            // não quebra a app
                        }
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