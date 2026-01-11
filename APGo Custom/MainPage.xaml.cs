
using Archipelago.MultiClient.Net;
using System.Text.Json;

namespace APGo_Custom;

public partial class MainPage : ContentPage
{
    private bool _isTracking = false;
    private List<Location> _setupLocations = new List<Location>();
    private Dictionary<string, long> _activeLocationMapping = new Dictionary<string, long>();
    private ArchipelagoSession? _session = null;
    private string? _currentRoomHash = null;

    public MainPage()
    {
        InitializeComponent();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await LoadMapAsync();
        await LoadSetupLocations();
        StartLocationTracking();
    }

    private async void StartLocationTracking()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        }

        if (status == PermissionStatus.Granted)
        {
            _isTracking = true;
            _ = TrackLocationAsync();
        }
    }

    private async void OnLocationButtonClicked(object sender, EventArgs e)
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

    private async Task TrackLocationAsync()
    {
        while (_isTracking)
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
                    await MapWebView.EvaluateJavaScriptAsync($"updateLocationMarker({location.Latitude}, {location.Longitude});");
                    var result = await MapWebView.EvaluateJavaScriptAsync(
                    $"checkProximity({location.Latitude}, {location.Longitude}, 20);");

                    if (result == "true")
                    {
                        System.Diagnostics.Debug.WriteLine("Within 20 meters of a marker!");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Location error: {ex.Message}");
            }

            await Task.Delay(2000);
        }
    }

    private bool _mapLoaded = false;
    private async Task LoadMapAsync()
    {
        using var stream = await FileSystem.OpenAppPackageFileAsync("map.html");
        using var reader = new StreamReader(stream);
        var html = await reader.ReadToEndAsync();
        var htmlSource = new HtmlWebViewSource
        {
            Html = html
        };
        MapWebView.Source = htmlSource;
        MapWebView.Navigating += OnMapNavigating;

        // Wait for the map to actually load
        var tcs = new TaskCompletionSource<bool>();
        MapWebView.Navigated += (s, e) =>
        {
            _mapLoaded = true;
            tcs.TrySetResult(true);
        };
        await tcs.Task;
        await Task.Delay(500); // Small delay to ensure JavaScript is ready
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isTracking = false;
    }

    private async void OnMapNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (e.Url.StartsWith("mapclick://"))
        {
            e.Cancel = true;

            var coords = e.Url.Replace("mapclick://", "");
            var parts = coords.Split(',');

            if (parts.Length == 2)
            {
                var lat = double.Parse(parts[0]);
                var lng = double.Parse(parts[1]);

                var location = new Location(lat, lng);
                if (_setupLocations.Any(l => l.Id == location.Id))
                {
                    await DisplayAlert("Duplicate", "A marker already exists at this location!", "OK");
                    return;
                }

                bool answer = await DisplayAlert("Add Location",
                $"Add marker at this location?\nLat: {lat}\nLng: {lng}",
                "Yes", "No");

                if (answer)
                {
                    _setupLocations.Add(location);

                    await MapWebView.EvaluateJavaScriptAsync($"addMarker({lat}, {lng}, '{location.Id}');");

                    await SaveSetupLocations();
                }
            }
        }
        else if (e.Url.StartsWith("markerclick://"))
        {
            e.Cancel = true;

            var locationId = e.Url.Replace("markerclick://", "");

            bool answer = await DisplayAlert("Remove Marker",
                "Do you want to remove this marker?",
                "Yes", "No");

            if (answer)
            {
                var location = _setupLocations.FirstOrDefault(l => l.Id == locationId);
                if (location != null)
                {
                    _setupLocations.Remove(location);
                    await SaveSetupLocations();
                }

                await MapWebView.EvaluateJavaScriptAsync($"removeMarker('{locationId}');");
            }
        }
    }
    private async Task SaveSetupLocations()
    {
        var json = JsonSerializer.Serialize(_setupLocations);
        var filePath = Path.Combine(FileSystem.AppDataDirectory, "setup_locations.json");
        await File.WriteAllTextAsync(filePath, json);
        System.Diagnostics.Debug.WriteLine($"Saved {_setupLocations.Count} locations to: {filePath}");
    }

    private async Task LoadSetupLocations()
    {
        var filePath = Path.Combine(FileSystem.AppDataDirectory, "setup_locations.json");
        System.Diagnostics.Debug.WriteLine($"Looking for locations at: {filePath}");

        if (File.Exists(filePath))
        {
            var json = await File.ReadAllTextAsync(filePath);
            System.Diagnostics.Debug.WriteLine($"File content: {json}");

            _setupLocations = JsonSerializer.Deserialize<List<Location>>(json) ?? new List<Location>();
            System.Diagnostics.Debug.WriteLine($"Loaded {_setupLocations.Count} locations");

            // Render them on the map
            foreach (var loc in _setupLocations)
            {
                await MapWebView.EvaluateJavaScriptAsync(
                    $"addMarker({loc.Latitude}, {loc.Longitude}, '{loc.Id}');");
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("No saved locations file found");
        }
    }
    private async Task SaveSeedMapping(string roomHash)
    {
        var json = JsonSerializer.Serialize(_activeLocationMapping);
        var filePath = Path.Combine(FileSystem.AppDataDirectory, $"seed_{roomHash}.json");
        await File.WriteAllTextAsync(filePath, json);
        System.Diagnostics.Debug.WriteLine($"Saved mapping for seed: {roomHash}");
    }

    private async Task<Dictionary<string, long>> LoadSeedMapping(string roomHash)
    {
        var filePath = Path.Combine(FileSystem.AppDataDirectory, $"seed_{roomHash}.json");
        if (File.Exists(filePath))
        {
            var json = await File.ReadAllTextAsync(filePath);
            var mapping = JsonSerializer.Deserialize<Dictionary<string, long>>(json);
            System.Diagnostics.Debug.WriteLine($"Loaded mapping for seed: {roomHash}");
            return mapping;
        }
        System.Diagnostics.Debug.WriteLine($"No mapping found for seed: {roomHash}");
        return null;
    }
    private async void OnConnectionButtonClicked(object sender, EventArgs e)
    {
        if (_session != null)
        {
            bool disconnect = await DisplayAlert("Disconnect",
                "Do you want to disconnect from Archipelago?",
                "Yes", "No");

            if (disconnect)
            {
                DisconnectFromArchipelago();
            }
        }
        else
        {
            await ShowConnectionDialog();
        }
    }

    private async Task ShowConnectionDialog()
    {
        string address = await DisplayPromptAsync("Connect", "Server Address:",
            initialValue: "archipelago.gg",
            placeholder: "archipelago.gg");

        if (string.IsNullOrWhiteSpace(address))
            return;

        string portStr = await DisplayPromptAsync("Connect", "Port:",
            initialValue: "38281",
            keyboard: Keyboard.Numeric);

        if (string.IsNullOrWhiteSpace(portStr) || !int.TryParse(portStr, out int port))
            return;

        string slotName = await DisplayPromptAsync("Connect", "Slot Name:",
            placeholder: "Player1");

        if (string.IsNullOrWhiteSpace(slotName))
            return;

        string password = await DisplayPromptAsync("Connect", "Password (optional):",
            placeholder: "Leave blank if none");

        await ConnectToArchipelago(address, port, slotName, password ?? "");
    }

    private async Task ConnectToArchipelago(string host, int port, string slotName, string password)
    {
        try
        {
            _session = ArchipelagoSessionFactory.CreateSession(host, port);
            var result = _session.TryConnectAndLogin("Archipela-Go!", slotName, Archipelago.MultiClient.Net.Enums.ItemsHandlingFlags.AllItems, password: password);

            if (result is LoginFailure failure)
            {
                await DisplayAlert("Connection Failed", string.Join("\n", failure.Errors), "OK");
                _session = null;
                return;
            }

            // Update button to green
            ConnectionButton.Text = "🟢";

            _currentRoomHash = $"{_session.RoomState.Seed}_{slotName}";

            await DisplayAlert("Connected", "Successfully connected to Archipelago!", "OK");

            // TODO: Next step - load/assign locations
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to connect: {ex.Message}", "OK");
            _session = null;
        }
    }

    private void DisconnectFromArchipelago()
    {
        _session?.Socket.DisconnectAsync();
        _session = null;
        _currentRoomHash = null;
        _activeLocationMapping.Clear();

        // Update button to red
        ConnectionButton.Text = "🔴";

        // Clear markers from map
        MapWebView.EvaluateJavaScriptAsync("clearAllMarkers();");

        DisplayAlert("Disconnected", "Disconnected from Archipelago", "OK");
    }
}