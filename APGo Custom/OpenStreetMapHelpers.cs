using Microsoft.Maui.Platform;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APGo_Custom
{
    internal class OpenStreetMapHelpers
    {

        public static async Task TrackLocationAsync(MainPage Parent, WebView Map)
        {
            while (Parent._isTracking)
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
                        await Map.EvaluateJavaScriptAsync($"updateLocationMarker({location.Latitude}, {location.Longitude});");

                        if (Parent._session != null)
                        {
                            var result = await Map
                                .EvaluateJavaScriptAsync($"checkProximity({location.Latitude}, {location.Longitude}, 20);");

                            var cleanResult = result?.Trim('"') ?? "";

                            if (!string.IsNullOrEmpty(cleanResult))
                            {
                                var locationIds = cleanResult.Split(',');
                                foreach (var locationId in locationIds)
                                {
                                    await APLocationHelpers.CheckLocation(Parent, locationId.Trim(), Map);
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

        public static async Task LoadMapAsync(MainPage Parent, WebView Map)
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync("map.html");
            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync();
            var htmlSource = new HtmlWebViewSource
            {
                Html = html
            };
            Map.Source = htmlSource;
            Map.Navigating += Parent.OnMapNavigating;

            // Wait for the map to actually load
            var tcs = new TaskCompletionSource<bool>();
            Map.Navigated += (s, e) =>
            {
                Parent._mapLoaded = true;
                tcs.TrySetResult(true);
            };
            await tcs.Task;
            await Task.Delay(500); // Small delay to ensure JavaScript is ready
        }

        public static async void StartLocationTracking(MainPage Parent, WebView Map)
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }

            if (status == PermissionStatus.Granted)
            {
                Parent._isTracking = true;
                _ = TrackLocationAsync(Parent, Map);
            }
        }
    }
}
