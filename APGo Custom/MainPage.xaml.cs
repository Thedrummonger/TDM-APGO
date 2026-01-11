namespace APGo_Custom;

public partial class MainPage : ContentPage
{
    private bool _isTracking = false;

    public MainPage()
    {
        InitializeComponent();
        LoadMap();
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

    private async void LoadMap()
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
                var lat = parts[0];
                var lng = parts[1];

                bool answer = await DisplayAlert("Add Location",
                $"Add marker at this location?\nLat: {lat}\nLng: {lng}",
                "Yes", "No");

                if (answer)
                {
                    await MapWebView.EvaluateJavaScriptAsync(
                        $"addMarker({lat}, {lng});");
                }
            }
        }
        else if (e.Url.StartsWith("markerclick://"))
        {
            e.Cancel = true;

            var indexStr = e.Url.Replace("markerclick://", "");

            bool answer = await DisplayAlert("Remove Marker",
                "Do you want to remove this marker?",
                "Yes", "No");

            if (answer)
            {
                await MapWebView.EvaluateJavaScriptAsync(
                    $"removeMarker({indexStr});");
            }
        }
    }
}