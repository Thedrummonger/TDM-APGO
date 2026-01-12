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
        public static async void StartLocationTracking(MainPage Parent, WebView Map)
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

            if (status != PermissionStatus.Granted)
                return;

            Parent._isTracking = true;

            _ = Task.Run(async () => await TrackingLoop(Parent, Map));
        }

        private static async Task TrackingLoop(MainPage Parent, WebView Map)
        {
            while (Parent._isTracking)
            {
                try
                {
                    var location = await Geolocation.GetLocationAsync(new GeolocationRequest
                    {
                        DesiredAccuracy = GeolocationAccuracy.Best,
                        Timeout = TimeSpan.FromSeconds(10),
                        RequestFullAccuracy = true
                    }); 
                    if (location == null)
                    {
                        await Task.Delay(500);
                        continue;
                    }

                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await Map.EvaluateJavaScriptAsync($"updateLocationMarker({location.Latitude}, {location.Longitude});");
                        APLocationHelpers.CheckLocationProximity(Parent, Map, location);
                    });
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Debug: Error\n{ex.Message}"); }

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
    }
}
