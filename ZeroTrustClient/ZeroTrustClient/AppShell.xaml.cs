namespace ZeroTrustClient
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            // Start page (always first)
            Items.Add(new ShellContent
            {
                ContentTemplate = new DataTemplate(typeof(StartPage)),
                Route = "StartPage"
            });

            // Register routes for later navigation
            Routing.RegisterRoute("MainPage", typeof(MainPage));
            Routing.RegisterRoute("PhonePage", typeof(PhonePage));
        }
    }
}
