using CommunityToolkit.Mvvm.Messaging;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using ZeroTrustClient.Services;
using static ZeroTrustClient.Services.ApiService;

namespace ZeroTrustClient
{
    public partial class AccountDetailsPage : ContentPage
    {
        private readonly IHttpClientFactory _clientFactory;
        public AccountDetailsPage(IHttpClientFactory clientFactory)
        {
            InitializeComponent();
            _clientFactory = clientFactory;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            InactivityService.Start(); // Start the 45s countdown
        }

        private void OnPageInteracted(object sender, EventArgs e)
        {
            InactivityService.Reset();
        }

        private async void OnGoBackClicked(object sender, EventArgs e)
        {
            Application.Current.MainPage = new NavigationPage(new AppPage(_clientFactory));
        }
    }
}
