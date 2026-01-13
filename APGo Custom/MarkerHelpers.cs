using Archipelago.MultiClient.Net;
using Microsoft.Maui.Platform;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APGo_Custom
{
    internal class MarkerHelpers
    {

        public static async Task AddMarker(MainPage Parent, WebNavigatingEventArgs e, WebView Map)
        {
            e.Cancel = true;
            var coords = e.Url.Replace("mapholdclick://", "").Split('/');

            if (coords.Length != 2)
                return;
            var lat = double.Parse(coords[0]);
            var lng = double.Parse(coords[1]);

            var location = new BaseLocation(lat, lng);
            if (Parent._setupLocations.Any(l => l.Id == location.Id))
            {
                await Parent.DisplayAlert("Duplicate", "A marker already exists at this location!", "OK");
                return;
            }

            bool answer = await Parent.DisplayAlert("Add Location",
            $"Add marker at this location?\nLat: {lat}\nLng: {lng}",
            "Yes", "No");

            if (answer)
            {
                Parent._setupLocations.Add(location);

                await Map.EvaluateJavaScriptAsync($"addMarker({lat}, {lng}, '{location.Id}', 'darkorange', 'orange');");

                await DataFileHelpers.SaveSetupLocations(Parent);
            }
        }

        public static async Task RemoveMarker(MainPage Parent, WebNavigatingEventArgs e, WebView Map)
        {
            e.Cancel = true;
            var locationId = e.Url.Replace("markerclick://", "");

            bool answer = await Parent.DisplayAlert("Remove Marker",
                "Do you want to remove this marker?",
                "Yes", "No");
            if (!answer)
                return;
            var location = Parent._setupLocations.FirstOrDefault(l => l.Id == locationId);
            if (location != null)
            {
                Parent._setupLocations.Remove(location);
                await DataFileHelpers.SaveSetupLocations(Parent);
            }

            await Map.EvaluateJavaScriptAsync($"removeMarker('{locationId}');");
        }


        public static (string border, string fill) GetMarkerColor(ArchipelagoSession session, APLocation location)
        {
            var CurrentKeyCount = session.Items.AllItemsReceived.Where(x => x.ItemName == "Progressive Key").Count();
            var IsUnlocked = location.KeysRequired <= CurrentKeyCount;
            var IsHinted = APLocationHelpers.IsLocationHinted(location, session, out _);
            return (IsHinted, IsUnlocked) switch
            {
                (true, true) => ("darkblue", "blue"),
                (true, false) => ("purple", "mediumpurple"),
                (false, true) => ("darkgreen", "green"),
                (false, false) => ("darkred", "red")
            };
        }

        public static async Task RenderTemplateLocations(MainPage parent, WebView Map)
        {
            foreach (var loc in parent._setupLocations)
            {
                await Map.EvaluateJavaScriptAsync(
                    $"addMarker({loc.Latitude}, {loc.Longitude}, '{loc.Id}', 'darkorange', 'orange');");
            }
        }

        public static async Task RenderActiveLocations(MainPage Parent, WebView Map)
        {
            if (Parent._session == null) return;

            var checkedLocations = Parent._session.Locations.AllLocationsChecked;
            var CurrentKeyCount = Parent._session.Items.AllItemsReceived.Where(x => x.ItemName == "Progressive Key").Count();

            foreach (var mapping in Parent._activeLocationMapping)
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
                (string borderColor, string fillColor) = MarkerHelpers.GetMarkerColor(Parent._session, setupLocation);
                await Map.EvaluateJavaScriptAsync(
                    $"addMarker({setupLocation.Latitude}, {setupLocation.Longitude}, '{setupLocation.Id}', '{borderColor}', '{fillColor}');");

                System.Diagnostics.Debug.WriteLine($"Rendered {setupLocation.ArchipelagoLocationName} (Keys: {setupLocation.KeysRequired})");
            }
        }
    }
}
