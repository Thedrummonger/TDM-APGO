using System.Text.Json;

namespace APGo_Custom;

public partial class SettingsPage : ContentPage
{
    private readonly MainPage MainPage;
    private readonly WebView MainMap;
    public int YamlMinimumDistance = 500;
    public int YamlMaximumDistance = 5000;
    public SettingsPage(MainPage parent, ConnectionDetails? connectionCache, UserSettings? uSerSettings, WebView Map)
    {
        InitializeComponent();

        UserSettings = uSerSettings ?? new UserSettings();
        ConnectionDetails = connectionCache ?? new ConnectionDetails();

        MainPage = parent;
        ServerEntry.Text = ConnectionDetails.Host;
        PortEntry.Text = ConnectionDetails.Port.ToString();
        SlotEntry.Text = ConnectionDetails.Slot;
        PasswordEntry.Text = ConnectionDetails.Password;

        ProximitySlider.Value = UserSettings.Radius;
        UseYamlMaxDistanceSwitch.IsToggled = UserSettings.UseMaxDist;
        UseYamlMinDistanceSwitch.IsToggled = UserSettings.UseMinDist;
        ProximityLabel.Text = $"Proximity Range: {UserSettings.Radius} meters";
        MainMap = Map;
    }   

    public UserSettings UserSettings { get; }
    public ConnectionDetails ConnectionDetails { get; }

    private void OnServerChanged(object sender, TextChangedEventArgs e)
    {
        ConnectionDetails.Host = e.NewTextValue;
    }

    private void OnPortChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(e.NewTextValue, out var newPort))
            ConnectionDetails.Port = newPort;
    }

    private void OnSlotChanged(object sender, TextChangedEventArgs e)
    {
        ConnectionDetails.Slot = e.NewTextValue;
    }

    private void OnPasswordChanged(object sender, TextChangedEventArgs e)
    {
        ConnectionDetails.Password = e.NewTextValue;
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();

        await DataFileHelpers.SaveUserSettings(UserSettings);

        var location = await Geolocation.GetLocationAsync(new GeolocationRequest
        {
            DesiredAccuracy = GeolocationAccuracy.Best,
            Timeout = TimeSpan.FromSeconds(10)
        });

        if (location != null)
        {
            await MainMap.EvaluateJavaScriptAsync($"updateLocationMarker({location.Latitude}, {location.Longitude}, {UserSettings.Radius});");
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

    private void OnProximitySliderChanged(object sender, ValueChangedEventArgs e)
    {
        int value = (int)e.NewValue;
        ProximityLabel.Text = $"Proximity Range: {value} meters";
        UserSettings.Radius = value;
    }

    private void OnUseMaxDistanceToggled(object sender, ToggledEventArgs e)
    {
        UserSettings.UseMaxDist = e.Value;
    }

    private void OnUseMinDistanceToggled(object sender, ToggledEventArgs e)
    {
        UserSettings.UseMinDist = e.Value;
    }

    private async void OnExportValidLocationsClicked(object sender, EventArgs e)
    {
        var json = JsonSerializer.Serialize(MainPage._setupLocations);
        await DataFileHelpers.SaveFileAsync(json, "APGo_Valid_Location_List.json");
    }

    private async void OnExportSeedDataClicked(object sender, EventArgs e)
    {
        if (MainPage._session == null)
        {
            await MainPage.DisplayAlert("Not Available", "You must first load a seed to export the seed data", "OK");
            return;
        }
        var json = JsonSerializer.Serialize(MainPage._activeLocationMapping);
        await DataFileHelpers.SaveFileAsync(json, $"APGo_Mapping_{MainPage._currentRoomHash}.json");
    }

    private async void OnLoadValidLocationsClicked(object sender, EventArgs e)
    {
        if (MainPage._session != null)
        {
            await MainPage.DisplayAlert("Not Available", "You must first disconnect from your current session", "OK");
            return;
        }
        bool confirm = await DisplayAlert("Load Valid Locations",
            "Are you sure you want to load a list of valid locations? This will override your current locations.",
            "Yes", "No");
        if (!confirm)
            return;
        List<BaseLocation>? _setupLocations = await DataFileHelpers.LoadFileAsync<List<BaseLocation>>();
        if (_setupLocations != null)
        {
            MainPage._setupLocations = _setupLocations;
            await DataFileHelpers.SaveSetupLocations(MainPage);
            await MainMap.EvaluateJavaScriptAsync("clearAllMarkers();");
            MarkerHelpers.RenderTemplateLocations(MainPage, MainMap);
            await DisplayAlert("Success", "Loaded Saved Locations", "OK");
        }
        else
            await DisplayAlert("Error", "Could not read setup locations file", "OK");
    }

    private async void OnLoadSeedDataClicked(object sender, EventArgs e)
    {
        if (MainPage._session == null)
        {
            await MainPage.DisplayAlert("Not Available", "You must first load a seed to import seed data", "OK");
            return;
        }
        bool confirm = await DisplayAlert("Load Valid Locations",
            "Are you sure you want to load a list of valid locations? This will override your current locations.",
            "Yes", "No");
        if (!confirm)
            return;
        Dictionary<string, APLocation>? _activeSeedLocations = await DataFileHelpers.LoadFileAsync<Dictionary<string, APLocation>>();
        if (_activeSeedLocations == null)
        {
            await DisplayAlert("Error", "Could not read seed data file", "OK");
            return;
        }
        if (_activeSeedLocations.Count != MainPage._session.Locations.AllLocations.Count || !LocationsMatch())
        {
            await DisplayAlert("Error", "The loaded File could not be applied to this seed", "OK");
            return;
        }

        MainPage._activeLocationMapping = _activeSeedLocations;
        await DataFileHelpers.SaveSeedMapping(MainPage);
        await MainMap.EvaluateJavaScriptAsync("clearAllMarkers();");
        MarkerHelpers.RenderActiveLocations(MainPage, MainMap);
        await DisplayAlert("Success", "Loaded Seed Data", "OK");

        bool LocationsMatch()
        {
            foreach(var i in MainPage._session!.Locations.AllLocations)
            {
                var Match = _activeSeedLocations.Values.FirstOrDefault(x => x.ArchipelagoLocationId == i);
                if (Match == null)
                    return false;
            }
            return true;
        }

    }
}