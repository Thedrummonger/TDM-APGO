
using Archipelago.MultiClient.Net;
using Newtonsoft.Json.Linq;
using System.Text.Json;

namespace APGo_Custom;

public partial class MainPage : ContentPage
{
    public bool _isTracking = false;
    public List<BaseLocation> _setupLocations = new List<BaseLocation>();
    public Dictionary<string, APLocation> _activeLocationMapping = new Dictionary<string, APLocation>();
    public ArchipelagoSession? _session = null;
    public string? _currentRoomHash = null;
    public bool _mapLoaded = false;
    public Timer? _refreshTimer = null;
    public bool _needsRefresh = false;

    public MainPage()
    {
        InitializeComponent();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await OpenStreetMapHelpers.LoadMapAsync(this, MapWebView);
        await DataFileHelpers.LoadSetupLocations(this, MapWebView);
        OpenStreetMapHelpers.StartLocationTracking(this, MapWebView);
        _refreshTimer = new Timer(DoRefresh, null, 1000, Timeout.Infinite);
    }

    public void DoRefresh(object? state)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_session != null && !_session.Socket.Connected)
            {
                await APConnectionHelpers.DisconnectFromArchipelago(this, MapWebView, ConnectionButton);
                return;
            }
            if (!_needsRefresh || _session is null) return;

            _needsRefresh = false;
            System.Diagnostics.Debug.WriteLine("Refreshing map markers");

            var checkedLocations = _session.Locations.AllLocationsChecked;

            foreach (var mapping in _activeLocationMapping)
            {
                if (checkedLocations.Contains(mapping.Value.ArchipelagoLocationId))
                {
                    await MapWebView.EvaluateJavaScriptAsync($"removeMarker('{mapping.Key}');");
                    continue;
                }

                (string borderColor, string fillColor) = MarkerHelpers.GetMarkerColor(_session, mapping.Value);
                await MapWebView.EvaluateJavaScriptAsync($"updateMarkerColor('{mapping.Value.Id}', '{borderColor}', '{fillColor}');");
            }
        });

        _refreshTimer?.Change(1000, Timeout.Infinite);
    }

    public async void OnLocationButtonClicked(object sender, EventArgs e)
    {
        try
        {
            var location = await Geolocation.GetLocationAsync(new GeolocationRequest
            {
                DesiredAccuracy = GeolocationAccuracy.Best,
                Timeout = TimeSpan.FromSeconds(10)
            });

            if (location != null)
            {
                await MapWebView.EvaluateJavaScriptAsync($"updateLocation({location.Latitude}, {location.Longitude});");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", "Could not get location", "OK");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isTracking = false;
    }

    public async void OnMapNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (_session == null)
        {
            if (e.Url.StartsWith("mapclick://"))
                await MarkerHelpers.AddMarker(this, e, MapWebView);
            else if (e.Url.StartsWith("markerclick://"))
                await MarkerHelpers.RemoveMarker(this, e, MapWebView);
        }
        else
        {
            if (e.Url.StartsWith("markerclick://"))
                await APLocationHelpers.DisplayLocationDetails(this, e);
        }
    }

    public async void OnConnectionButtonClicked(object sender, EventArgs e)
    {
        if (_session != null)
        {
            bool disconnect = await DisplayAlert("Disconnect",
                "Do you want to disconnect from Archipelago?",
                "Yes", "No");

            if (disconnect)
            {
               await APConnectionHelpers.DisconnectFromArchipelago(this, MapWebView, ConnectionButton);
            }
        }
        else
        {
            await APConnectionHelpers.ShowConnectionDialog(this, MapWebView, ConnectionButton);
        }
    }
    
    public void OnItemReceived(Archipelago.MultiClient.Net.Helpers.ReceivedItemsHelper helper)
    {
        _needsRefresh = true;
    }

    public void OnLocationsChecked(System.Collections.ObjectModel.ReadOnlyCollection<long> newCheckedLocations)
    {
        _needsRefresh = true;
    }

}