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

                _parent.LastKnownLocation = (location.Latitude, location.Longitude);

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await _map.EvaluateJavaScriptAsync($"updateLocationMarker({location.Latitude}, {location.Longitude}, {_parent.SettingsPage.UserSettings.Radius});");
                    APLocationHelpers.CheckLocationsInRange(_parent, _map, location);
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
                    Parent.LastKnownLocation = (location.Latitude, location.Longitude);

                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await Map.EvaluateJavaScriptAsync($"updateLocationMarker({location.Latitude}, {location.Longitude}, {Parent.SettingsPage.UserSettings.Radius});");
                        APLocationHelpers.CheckLocationsInRange(Parent, Map, location);
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
            FocusCurrentLocation(Parent, Map);
        }

        public static async void FocusCurrentLocation(MainPage Parent, WebView Map)
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
                    await Map.EvaluateJavaScriptAsync($"updateLocation({location.Latitude}, {location.Longitude}, {Parent.SettingsPage.UserSettings.Radius});");
                }
            }
            catch (Exception ex)
            {
                await Parent.DisplayAlert("Error", "Could not get location", "OK");
            }
        }

        public static (bool withinRange, double distance) CheckIfWithinRange(MainPage parent, double targetLat, double targetLong, double checkRadius) =>
            parent.LastKnownLocation == null ? (false, -1) :
            CheckIfWithinRange(parent.LastKnownLocation.Value.Lat, parent.LastKnownLocation.Value.Long, targetLat, targetLong, checkRadius);

        public static (bool withinRange, double distance) CheckIfWithinRange(double userLat, double userLong, double targetLat,double targetLong,double checkRadius)
        {
            double distance = CalculateDistance(userLat, userLong, targetLat, targetLong);
            var InRange = distance > -1 && distance <= checkRadius;
            return (InRange, distance);
        }

        public static double CalculateDistance(MainPage parent, double lat2, double lon2) =>
            parent.LastKnownLocation == null ? -1 : CalculateDistance(parent.LastKnownLocation.Value.Lat, parent.LastKnownLocation.Value.Long, lat2, lon2);

        public static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000; // Earth's radius in meters
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private static double ToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }
    }
}
