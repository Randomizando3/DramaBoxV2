using DramaBox.Views;

namespace DramaBox
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute("login", typeof(LoginView));
            Routing.RegisterRoute("register", typeof(RegisterView));
            Routing.RegisterRoute("upgrade", typeof(Upgrade));


        }


    }
}
