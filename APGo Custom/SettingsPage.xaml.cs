namespace APGo_Custom;

public partial class SettingsPage : ContentPage
{
    private readonly MainPage MainPage;
    private readonly WebView MainMap;
    public int MarkerRadius = 20;
    public SettingsPage(MainPage parent, ConnectionDetails? connectionCache, UserSettings? uSerSettings, WebView Map)
    {
        InitializeComponent();

        ServerAddress = connectionCache?.Host ?? "archipelago.gg";
        Port = connectionCache?.Port ?? 38281;
        SlotName = connectionCache?.Slot ?? string.Empty;
        Password = connectionCache?.Password ?? string.Empty;

        MarkerRadius = uSerSettings?.Radius ?? 20;

        MainPage = parent;
        ServerEntry.Text = ServerAddress;
        PortEntry.Text = Port.ToString();
        SlotEntry.Text = SlotName;
        PasswordEntry.Text = Password;
        ProximitySlider.Value = MarkerRadius;
        ProximityLabel.Text = $"Proximity Range: {MarkerRadius} meters";
        MainMap = Map;
    }   

    public string ServerAddress;
    public int Port;
    public string SlotName;
    public string Password;

    public ConnectionDetails GetConnectionDetails()
    {
        return new ConnectionDetails(ServerAddress, Port, SlotName, Password);
    }

    private void OnServerChanged(object sender, TextChangedEventArgs e)
    {
        ServerAddress = e.NewTextValue;
    }

    private void OnPortChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(e.NewTextValue, out var newPort))
            Port = newPort;
    }

    private void OnSlotChanged(object sender, TextChangedEventArgs e)
    {
        SlotName = e.NewTextValue;
    }

    private void OnPasswordChanged(object sender, TextChangedEventArgs e)
    {
        Password = e.NewTextValue;
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();

        await DataFileHelpers.SaveUserSettings(new UserSettings(MarkerRadius));

        var location = await Geolocation.GetLocationAsync(new GeolocationRequest
        {
            DesiredAccuracy = GeolocationAccuracy.Best,
            Timeout = TimeSpan.FromSeconds(10)
        });

        if (location != null)
        {
            await MainMap.EvaluateJavaScriptAsync($"updateLocationMarker({location.Latitude}, {location.Longitude}, {MarkerRadius});");
        }
    }

    private async void OnClearConnectionCacheClicked(object sender, EventArgs e)
    {
        if (MainPage._session != null)
        {
            await MainPage.DisplayAlert("Not Available", "You must first discconect from your current session", "OK");
            return;
        }

        bool confirm = await DisplayAlert("Clear Connection Cache",
            "Are you sure you want to clear saved connection settings?",
            "Yes", "No");

        if (confirm)
            DataFileHelpers.ClearLastConnectionCache();
    }

    private async void OnClearValidLocationsClicked(object sender, EventArgs e)
    {
        if (MainPage._session != null)
        {
            await MainPage.DisplayAlert("Not Available", "You must first discconect from your current session", "OK");
            return;
        }

        bool confirm = await DisplayAlert("Clear Valid Locations",
            "Are you sure you want to clear all valid locations? This cannot be undone.",
            "Yes", "No");

        if (confirm)
        {
            DataFileHelpers.ClearSetupLocations();
            MainPage._setupLocations.Clear();
            await MainMap.EvaluateJavaScriptAsync("clearAllMarkers();");
        }
    }

    private async void OnClearSeedDataClicked(object sender, EventArgs e)
    {
        if (MainPage._session != null)
        {
            await MainPage.DisplayAlert("Not Available", "You must first discconect from your current session", "OK");
            return;
        }

        bool confirm = await DisplayAlert("Clear Seed Data",
            "Are you sure you want to clear all seed data? This cannot be undone.",
            "Yes", "No");

        if (confirm)
            DataFileHelpers.RemoveSeedMappings();
    }

    private void OnProximityChanged(object sender, ValueChangedEventArgs e)
    {
        int value = (int)e.NewValue;
        ProximityLabel.Text = $"Proximity Range: {value} meters";
        MarkerRadius = value;
    }
}