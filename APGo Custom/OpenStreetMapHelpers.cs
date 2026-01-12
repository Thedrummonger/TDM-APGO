using Microsoft.Maui.Platform;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
#if ANDROID
using Android.Gms.Location;
using AndroidX.Core.Location;
#endif

namespace APGo_Custom
{
    internal class OpenStreetMapHelpers
    {
#if ANDROID
        public static async void StartLocationTracking(MainPage Parent, WebView Map)
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

            if (status != PermissionStatus.Granted)
                return;

            Parent._isTracking = true;

            var fusedLocationClient = LocationServices.GetFusedLocationProviderClient(Platform.CurrentActivity);

            var locationRequest = LocationRequest.Create()
                .SetPriority(100)
                .SetInterval(1000)
                .SetFastestInterval(500);

            var locationCallback = new AndroidLocationCallback(Parent, Map);

            await fusedLocationClient.RequestLocationUpdatesAsync(locationRequest, locationCallback);
        }
        private class AndroidLocationCallback : Android.Gms.Location.LocationCallback
        {
            private readonly MainPage _parent;
            private readonly WebView _map;

            public AndroidLocationCallback(MainPage parent, WebView map)
            {
                _parent = parent;
                _map = map;
            }

            public override void OnLocationResult(Android.Gms.Location.LocationResult result)
            {
                if (result?.LastLocation == null || !_parent._isTracking) return;

                var loc = result.LastLocation;
                var location = new Location()
                {
                    Latitude = loc.Latitude,
                    Longitude = loc.Longitude,
                    Accuracy = loc.Accuracy,
                    Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(loc.Time)
                };

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await _map.EvaluateJavaScriptAsync($"updateLocationMarker({location.Latitude}, {location.Longitude});");
                    APLocationHelpers.CheckLocationProximity(_parent, _map, location);
                });
            }
        }

#else
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
#endif

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
