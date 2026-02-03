using Microsoft.Maui.Controls;

namespace DramaBox
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            // ✅ AppShell vira a raiz do app
            MainPage = new AppShell();
        }
    }
}
