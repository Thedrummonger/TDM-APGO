
using Archipelago.MultiClient.Net;
using Newtonsoft.Json.Linq;
using System.Text.Json;

namespace APGo_Custom;

public partial class MainPage : ContentPage
{
    private bool _isTracking = false;
    private List<Location> _setupLocations = new List<Location>();
    private Dictionary<string, APLocation> _activeLocationMapping = new Dictionary<string, APLocation>();
    private ArchipelagoSession? _session = null;
    private string? _currentRoomHash = null;
    private bool _mapLoaded = false;
    private Timer? _refreshTimer = null;
    private bool _needsRefresh = false;

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
        _refreshTimer = new Timer(DoRefresh, null, 1000, Timeout.Infinite);
    }

    private void DoRefresh(object? state)
    {
        if (!_needsRefresh || _session is null) return;

        _needsRefresh = false;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            System.Diagnostics.Debug.WriteLine("Refreshing map markers");

            var checkedLocations = _session.Locations.AllLocationsChecked;

            foreach (var mapping in _activeLocationMapping)
            {
                if (checkedLocations.Contains(mapping.Value.ArchipelagoLocationId))
                {
                    await MapWebView.EvaluateJavaScriptAsync($"removeMarker('{mapping.Key}');");
                    continue;
                }

                (string borderColor, string fillColor) = GetMarkerColor(_session, mapping.Value);
                await MapWebView.EvaluateJavaScriptAsync($"updateMarkerColor('{mapping.Value.Id}', '{borderColor}', '{fillColor}');");
            }
        });

        _refreshTimer?.Change(1000, Timeout.Infinite);
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

                    if (_session != null)
                    {
                        var result = await MapWebView
                            .EvaluateJavaScriptAsync($"checkProximity({location.Latitude}, {location.Longitude}, 20);");

                        // Remove quotes from result
                        var cleanResult = result?.Trim('"') ?? "";

                        if (!string.IsNullOrEmpty(cleanResult))
                        {
                            var locationIds = cleanResult.Split(',');
                            foreach (var locationId in locationIds)
                            {
                                await CheckLocation(locationId.Trim());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Location error: {ex.Message}");
            }

            await Task.Delay(1000);
        }
    }

    private async Task CheckLocation(string locationId)
    {
        if (_session == null || !_activeLocationMapping.ContainsKey(locationId))
            return;

        long apLocationId = _activeLocationMapping[locationId].ArchipelagoLocationId;

        // Check if already checked in AP
        if (_session.Locations.AllLocationsChecked.Contains(apLocationId))
        {
            System.Diagnostics.Debug.WriteLine($"Location {locationId} already checked in AP");
            return;
        }

        // Get the location details
        var location = _activeLocationMapping[locationId];
        if (location == null)
        {
            System.Diagnostics.Debug.WriteLine($"Could not find setup location for {locationId}");
            return;
        }

        // Check if we have enough keys
        var currentKeyCount = _session.Items.AllItemsReceived.Where(x => x.ItemName == "Progressive Key").Count();
        if (location.KeysRequired > currentKeyCount)
        {
            System.Diagnostics.Debug.WriteLine($"Not enough keys for {location.ArchipelagoLocationName}. Need {location.KeysRequired}, have {currentKeyCount}");
            return;
        }

        // Send check to Archipelago
        _session.Locations.CompleteLocationChecks(apLocationId);

        // Remove marker from map
        await MapWebView.EvaluateJavaScriptAsync($"removeMarker('{locationId}');");

        var locationName = location.ArchipelagoLocationName;
        System.Diagnostics.Debug.WriteLine($"✓ Checked location: {locationName} (ID: {apLocationId})");

    }

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
        if (_session == null)
        {
            if (e.Url.StartsWith("mapclick://"))
                await AddMarker(e);
            else if (e.Url.StartsWith("markerclick://"))
                await RemoveMarker(e);
        }
        else
        {
            if (e.Url.StartsWith("markerclick://"))
                await DisplayLocationDetails(e);
        }
    }

    private async Task DisplayLocationDetails(WebNavigatingEventArgs e)
    {
        e.Cancel = true;
        var locationId = e.Url.Replace("markerclick://", "");
        if (!_activeLocationMapping.TryGetValue(locationId, out var Data))
            return;

        await DisplayAlert(Data.ArchipelagoLocationName, $"Keys Required {Data.KeysRequired}", "OK");

    }

    private async Task AddMarker(WebNavigatingEventArgs e)
    {
        e.Cancel = true;
        var coords = e.Url.Replace("mapclick://", "");
        var parts = coords.Split(',');

        if (parts.Length != 2)
            return;
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

            await MapWebView.EvaluateJavaScriptAsync($"addMarker({lat}, {lng}, '{location.Id}', 'darkorange', 'orange');");

            await SaveSetupLocations();
        }
    }

    private async Task RemoveMarker(WebNavigatingEventArgs e)
    {
        e.Cancel = true;
        var locationId = e.Url.Replace("markerclick://", "");

        bool answer = await DisplayAlert("Remove Marker",
            "Do you want to remove this marker?",
            "Yes", "No");
        if (!answer)
            return;
        var location = _setupLocations.FirstOrDefault(l => l.Id == locationId);
        if (location != null)
        {
            _setupLocations.Remove(location);
            await SaveSetupLocations();
        }

        await MapWebView.EvaluateJavaScriptAsync($"removeMarker('{locationId}');");
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
                    $"addMarker({loc.Latitude}, {loc.Longitude}, '{loc.Id}', 'darkorange', 'orange');");
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

    private async Task<Dictionary<string, APLocation>> LoadSeedMapping(string roomHash)
    {
        var filePath = Path.Combine(FileSystem.AppDataDirectory, $"seed_{roomHash}.json");
        if (File.Exists(filePath))
        {
            var json = await File.ReadAllTextAsync(filePath);
            var mapping = JsonSerializer.Deserialize<Dictionary<string, APLocation>>(json);
            System.Diagnostics.Debug.WriteLine($"Loaded mapping for seed: {roomHash}");
            return mapping;
        }
        System.Diagnostics.Debug.WriteLine($"No mapping found for seed: {roomHash}");
        return null;
    }

    public class ConnectionDetails
    {
        public ConnectionDetails() { }
        public ConnectionDetails(string host, int port, string slotName, string password)
        {
            Host = host;
            Port = port;
            Slot = slotName;
            Password = password;
        }
        public string? Host { get; set; }
        public int? Port { get; set; }
        public string? Slot { get; set; }
        public string? Password { get; set; }
    }

    private async Task<ConnectionDetails?> LoadLastConnectionCache()
    {
        var filePath = Path.Combine(FileSystem.AppDataDirectory, $"connection_cache.json");
        if (File.Exists(filePath))
        {
            var json = await File.ReadAllTextAsync(filePath);
            var mapping = JsonSerializer.Deserialize<ConnectionDetails>(json);
            System.Diagnostics.Debug.WriteLine($"File Content: {json}");
            System.Diagnostics.Debug.WriteLine($"Loaded Connection Cache: {mapping?.Slot}@{mapping?.Host}:{mapping?.Port}");
            return mapping;
        }
        System.Diagnostics.Debug.WriteLine($"Cache Missing");
        return null;
    }

    private async Task SaveLastConnectionCache(ConnectionDetails Details)
    {
        var json = JsonSerializer.Serialize(Details);
        var filePath = Path.Combine(FileSystem.AppDataDirectory, $"connection_cache.json");
        await File.WriteAllTextAsync(filePath, json);
        System.Diagnostics.Debug.WriteLine($"Chaching Connectiong Success: {Details.Slot}@{Details.Host}:{Details.Port}");
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
               await DisconnectFromArchipelago();
            }
        }
        else
        {
            await ShowConnectionDialog();
        }
    }

    private async Task ShowConnectionDialog()
    {
        var ConnectionCache = await LoadLastConnectionCache();

        string HostPlaceHolder = ConnectionCache is null || string.IsNullOrWhiteSpace(ConnectionCache.Host) ?
            "archipelago.gg" : ConnectionCache.Host;
        string PortPlaceholder = (ConnectionCache is null || ConnectionCache.Port is null || ConnectionCache.Port.Value < 1) ?
            "38281" : ConnectionCache.Port.Value.ToString();
        string SlotPlaceholder = (ConnectionCache is null || string.IsNullOrWhiteSpace(ConnectionCache.Slot)) ?
            "" : ConnectionCache.Slot;
        string PassPlaceholder = (ConnectionCache is null || string.IsNullOrWhiteSpace(ConnectionCache.Password)) ?
            "" : ConnectionCache.Password;

        string address = await DisplayPromptAsync("Connect", "Server Address:",
            initialValue: HostPlaceHolder,
            placeholder: "archipelago.gg");

        if (string.IsNullOrWhiteSpace(address))
            return;

        string portStr = await DisplayPromptAsync("Connect", "Port:",
            initialValue: PortPlaceholder,
            keyboard: Keyboard.Numeric);

        if (string.IsNullOrWhiteSpace(portStr) || !int.TryParse(portStr, out int port))
            return;

        string slotName = await DisplayPromptAsync("Connect", "Slot Name:",
            initialValue: SlotPlaceholder,
            placeholder: "Player1");

        if (string.IsNullOrWhiteSpace(slotName))
            return;

        string password = await DisplayPromptAsync("Connect", "Password (optional):",
            initialValue: PassPlaceholder,
            placeholder: "Leave blank if none");

        await ConnectToArchipelago(new ConnectionDetails(address, port, slotName, password ?? ""));
    }

    private async Task ConnectToArchipelago(ConnectionDetails connectionDetails)
    {
        try
        {
            _session = ArchipelagoSessionFactory.CreateSession(connectionDetails.Host??"", connectionDetails.Port??0);
            var result = _session.TryConnectAndLogin("Archipela-Go!", connectionDetails.Slot, 
                Archipelago.MultiClient.Net.Enums.ItemsHandlingFlags.AllItems, password: connectionDetails.Password);

            if (result is LoginFailure failure)
            {
                await DisplayAlert("Connection Failed", string.Join("\n", failure.Errors), "OK");
                _session = null;
                return;
            }

            // Update button to green
            ConnectionButton.Text = "🟢";

            _currentRoomHash = $"{_session.RoomState.Seed}_{connectionDetails.Slot}";

            // Clear markers from map
            await MapWebView.EvaluateJavaScriptAsync("clearAllMarkers();");

            await SaveLastConnectionCache(connectionDetails);

            await DisplayAlert("Connected", "Successfully connected to Archipelago!", "OK");

            Dictionary<string, APLocation>? savedMapping = await LoadSeedMapping(_currentRoomHash);

            if (savedMapping != null)
            {
                _activeLocationMapping = savedMapping;
                System.Diagnostics.Debug.WriteLine($"Loaded existing mapping with {_activeLocationMapping.Count} locations");
            }
            else
            {
                var slotData = _session.DataStorage.GetSlotData();
                if (!slotData.TryGetValue("trips", out var tripsData))
                {
                    await DisplayAlert("Error", "No trips data found in slot data", "OK");
                    await DisconnectFromArchipelago();
                    return;
                }

                if (tripsData is not JObject tripsObj)
                {
                    await DisplayAlert("Error", "Failed to deserialize trips", "OK");
                    await DisconnectFromArchipelago();
                    return;
                }
                var trips = tripsObj.ToObject<Dictionary<string, Trip>>();

                if (trips == null)
                {
                    await DisplayAlert("Error", "Failed to deserialize trips", "OK");
                    await DisconnectFromArchipelago();
                    return;
                }

                if (trips == null || trips.Count == 0)
                {
                    await DisplayAlert("Error", "Failed to parse trips data", "OK");
                    await DisconnectFromArchipelago();
                    return;
                }
                System.Diagnostics.Debug.WriteLine($"Found {trips.Count} trips in slot data");

                System.Diagnostics.Debug.WriteLine($"FullTripsDict {JsonSerializer.Serialize(trips)}");

                if (!await AssignLocationsToTrips(trips))
                {
                    await DisconnectFromArchipelago();
                    return;
                }
            }
            await RenderActiveLocations();
            _session.Items.ItemReceived += OnItemReceived;
            _session.Locations.CheckedLocationsUpdated += OnLocationsChecked;
            await SaveLastConnectionCache(connectionDetails);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to connect: {ex.Message}", "OK");
            _session = null;
        }
    }

    private async Task DisconnectFromArchipelago()
    {
        if (_session != null)
        {
            _session.Items.ItemReceived -= OnItemReceived;
            _session.Locations.CheckedLocationsUpdated -= OnLocationsChecked;
            if (_session.Socket != null && _session.Socket.Connected)
                await _session.Socket.DisconnectAsync();
        }
        _session = null;
        _currentRoomHash = null;
        _activeLocationMapping.Clear();

        // Update button to red
        ConnectionButton.Text = "🔴";

        // Clear markers from map
        await MapWebView.EvaluateJavaScriptAsync("clearAllMarkers();");

        await DisplayAlert("Disconnected", "Disconnected from Archipelago", "OK");

        foreach (var loc in _setupLocations)
        {
            await MapWebView.EvaluateJavaScriptAsync(
                $"addMarker({loc.Latitude}, {loc.Longitude}, '{loc.Id}', 'darkorange', 'orange');");
        }
    }

    private async Task<bool> AssignLocationsToTrips(Dictionary<string, Trip> trips)
    {
        _activeLocationMapping.Clear();

        // Get all AP locations for our game
        var allAPLocations = _session.Locations.AllLocations;

        if (allAPLocations.Count != trips.Count)
        {
            await DisplayAlert("Error", "Could not find trip data for all locations", "OK");
            return false;
        }
        if (_setupLocations.Count < trips.Count)
        {
            await DisplayAlert("Error", $"You have not defined enough location {_setupLocations.Count} " +
                $"for needed trips {trips.Count}.\nCreated at least {trips.Count -_setupLocations.Count} more locations", "OK");
            return false;
        }

        System.Diagnostics.Debug.WriteLine($"Total AP locations: {allAPLocations.Count}");
        System.Diagnostics.Debug.WriteLine($"Total setup locations: {_setupLocations.Count}");
        System.Diagnostics.Debug.WriteLine($"Total trips: {trips.Count}");

        // Randomly shuffle setup locations
        var random = new Random();
        var shuffledSetupLocations = _setupLocations.OrderBy(x => random.Next()).ToList();

        // Assign each trip to a physical location
        int i = 0;
        foreach (var tripEntry in trips)
        {
            var locationName = tripEntry.Key;
            var trip = tripEntry.Value;

            // Get the AP location ID for this location name
            var apLocationId = _session.Locations.GetLocationIdFromName("Archipela-Go!", locationName);

            if (apLocationId == -1)
            {
                await DisplayAlert("Error", $"Could not find AP location ID for {locationName}", "OK");
                return false;
            }

            var setupLocation = shuffledSetupLocations[i];

            // Store the mapping
            _activeLocationMapping[setupLocation.Id] = 
                new APLocation(shuffledSetupLocations[i], apLocationId, locationName, trip.KeyNeeded);

            System.Diagnostics.Debug.WriteLine($"Assigned {locationName} (ID: {apLocationId}, Keys: {trip.KeyNeeded}) to location {setupLocation.Id}");

            i++;
        }

        await SaveSeedMapping(_currentRoomHash);
        return true;
    }
    private async Task RenderActiveLocations()
    {
        if (_session == null) return;

        var checkedLocations = _session.Locations.AllLocationsChecked;
        var CurrentKeyCount = _session.Items.AllItemsReceived.Where(x => x.ItemName == "Progressive Key").Count();

        foreach (var mapping in _activeLocationMapping)
        {
            var setupLocation = mapping.Value;
            if (setupLocation == null)
            {
                System.Diagnostics.Debug.WriteLine($"Warning: Could not find setup location for ID {mapping.Key}");
                continue;
            }

            if (checkedLocations.Contains(mapping.Value.ArchipelagoLocationId))
            {
                System.Diagnostics.Debug.WriteLine($"Location {setupLocation.ArchipelagoLocationName} already checked, skipping");
                continue;
            }
            (string borderColor, string fillColor) = GetMarkerColor(_session, setupLocation);
            await MapWebView.EvaluateJavaScriptAsync(
                $"addMarker({setupLocation.Latitude}, {setupLocation.Longitude}, '{setupLocation.Id}', '{borderColor}', '{fillColor}');");

            System.Diagnostics.Debug.WriteLine($"Rendered {setupLocation.ArchipelagoLocationName} (Keys: {setupLocation.KeysRequired})");
        }
    }

    private (string border, string fill) GetMarkerColor(ArchipelagoSession session, APLocation location)
    {
        var CurrentKeyCount = session.Items.AllItemsReceived.Where(x => x.ItemName == "Progressive Key").Count();
        string borderColor, fillColor;
        bool isHinted = false; // TODO: Add hinted logic later
        System.Diagnostics.Debug.WriteLine($"Debug | Curernt Keys {CurrentKeyCount}| {location.ArchipelagoLocationName} Keys {location.KeysRequired}");
        if (location.KeysRequired > CurrentKeyCount)
        {
            borderColor = "darkred";
            fillColor = "red";
        }
        else if (isHinted)
        {
            borderColor = "darkblue";
            fillColor = "blue";
        }
        else
        {
            borderColor = "darkgreen";
            fillColor = "green";
        }
        return (borderColor, fillColor);
    }

    private void OnItemReceived(Archipelago.MultiClient.Net.Helpers.ReceivedItemsHelper helper)
    {
        _needsRefresh = true;
    }

    private void OnLocationsChecked(System.Collections.ObjectModel.ReadOnlyCollection<long> newCheckedLocations)
    {
        _needsRefresh = true;
    }

}