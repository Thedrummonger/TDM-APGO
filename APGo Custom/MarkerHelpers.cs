using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Models;

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
            if (Parent._setupLocations.Any(l => l.Id == location.Id) || (Parent.AnchorMarker is not null && Parent.AnchorMarker.Id == location.Id))
            {
                await Parent.DisplayAlert("Duplicate", "A marker already exists at this location!", "OK");
                return;
            }

            var dialog = new CustomDialog("Add Location", $"Add marker at this location?\nLat: {lat}\nLng: {lng}", "Valid Location", "Anchor Location", "Cancel");
            await Parent.Navigation.PushModalAsync(dialog);
            var result = await dialog.ShowAsync();


            if (result == "Valid Location")
            {
                Parent._setupLocations.Add(location);
                await Map.EvaluateJavaScriptAsync($"addMarker({lat}, {lng}, '{location.Id}', 'goldenrod', 'yellow');");
                await DataFileHelpers.SaveSetupLocations(Parent);
            }
            else if (result == "Anchor")
            {
                if (Parent.AnchorMarker != null)
                    await Map.EvaluateJavaScriptAsync($"removeMarker('{Parent.AnchorMarker.Id}');");
                Parent.AnchorMarker = location;
                await Map.EvaluateJavaScriptAsync($"addMarker({lat}, {lng}, '{Parent.AnchorMarker.Id}', 'darkcyan', 'cyan');");
            }
        }

        public static async Task RemoveMarker(MainPage Parent, WebNavigatingEventArgs e, WebView Map)
        {
            e.Cancel = true;
            var locationId = e.Url.Replace("markerclick://", "");

            bool IsAnchor = Parent.AnchorMarker != null && Parent.AnchorMarker.Id == locationId;

            bool answer = await Parent.DisplayAlert("Remove Marker",
                $"Do you want to remove {(IsAnchor ? "the anchor" : "This")} marker?",
                "Yes", "No");
            if (!answer)
                return;
            var location = Parent._setupLocations.FirstOrDefault(l => l.Id == locationId);
            if (location != null)
            {
                Parent._setupLocations.Remove(location);
                await DataFileHelpers.SaveSetupLocations(Parent);
            }
            else if (IsAnchor)
            {
                Parent.AnchorMarker = null;
            }
            await Map.EvaluateJavaScriptAsync($"removeMarker('{locationId}');");
        }


        public static (string border, string fill) GetMarkerColor(ArchipelagoSession session, APLocation location, int CurrentKeyCount, Dictionary<string, Hint> HintCache)
        {
            var IsUnlocked = location.KeysRequired <= CurrentKeyCount;
            var IsHinted = APLocationHelpers.IsLocationHinted(location, session, HintCache, out _);
            return (IsHinted, IsUnlocked) switch
            {
                (true, true) => ("darkblue", "blue"),
                (true, false) => ("purple", "mediumpurple"),
                (false, true) => ("darkgreen", "green"),
                (false, false) => ("darkred", "red")
            };
        }

        public static void RenderTemplateLocations(MainPage parent, WebView Map)
        {
            foreach (var loc in parent._setupLocations)
            {
                _ = Map.EvaluateJavaScriptAsync(
                    $"addMarker({loc.Latitude}, {loc.Longitude}, '{loc.Id}', 'goldenrod', 'yellow');");
            }
            if (parent.AnchorMarker != null)
                _ = Map.EvaluateJavaScriptAsync($"addMarker({parent.AnchorMarker.Latitude}, {parent.AnchorMarker.Longitude}, '{parent.AnchorMarker.Id}', 'darkcyan', 'cyan');");
        }

        public static void RenderActiveLocations(MainPage Parent, WebView Map)
        {
            if (Parent._session == null) return;

            HashSet<long> checkedLocations = [..Parent._session.Locations.AllLocationsChecked];
            var CurrentKeyCount = Parent._session.Items.AllItemsReceived.Where(x => x.ItemName == "Progressive Key").Count();
            var HintCache = Parent._session.CreateHintCache();

            foreach (var mapping in Parent._activeLocationMapping)
            {
                var setupLocation = mapping.Value;

                if (checkedLocations.Contains(setupLocation.ArchipelagoLocationId))
                    continue;

                (string borderColor, string fillColor) = MarkerHelpers.GetMarkerColor(Parent._session, setupLocation, CurrentKeyCount, HintCache);

                _ = Map.EvaluateJavaScriptAsync(
                    $"addMarker({setupLocation.Latitude}, {setupLocation.Longitude}, '{setupLocation.Id}', '{borderColor}', '{fillColor}');");
            }
        }
    }
}
