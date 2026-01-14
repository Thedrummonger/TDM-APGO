using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Models;
using Microsoft.Maui.Platform;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APGo_Custom
{
    public static class APLocationHelpers
    {
        public const string LongGoal = "Archipela-Go!";
        public const string ShortGoal = "Ap-Go!";
        public static bool HasGoal(MainPage parent)
        {
            if (!parent.HasActiveAP) return false;
            switch (parent.GoalSetting)
            {
                case GoalSetting.option_one_hard_travel:
                    //if (parent._activeLocationMapping.Count == 0) return false;
                    //var AllCheckedLocations = parent._session!.Locations.AllLocationsChecked;
                    //var MaxDistanceTier = parent._activeLocationMapping.Select(x => x.Value.DistanceTier).Max();
                    //var AllMaxDistanceLocations = parent._activeLocationMapping.Values.Where(x => x.DistanceTier == MaxDistanceTier);
                    //return AllMaxDistanceLocations.Any(x => AllCheckedLocations.Contains(x.ArchipelagoLocationId));
                case GoalSetting.option_allsanity:
                    if (parent._session!.Locations.AllLocations.Count == 0) return false;
                    return parent._session.Locations.AllLocationsChecked.Count == parent._session.Locations.AllLocations.Count;
                case GoalSetting.option_short_macguffin:
                    return ShortGoal.All(parent.GoalItemsRecieved.Contains);
                case GoalSetting.option_long_macguffin:
                    return LongGoal.All(parent.GoalItemsRecieved.Contains);
                default:
                    return false;
            }
        }

        public static async Task DisplayLocationDetails(MainPage parent, WebView Map, WebNavigatingEventArgs e)
        {
            e.Cancel = true;
            if (!parent.HasActiveAP) return;
            var locationId = e.Url.Replace("markerclick://", "");
            if (!parent._activeLocationMapping.TryGetValue(locationId, out var Data))
                return;
            StringBuilder stringBuilder = new StringBuilder($"Keys Required {Data.KeysRequired}\nDistance Tier {Data.DistanceTier}");
            if (Data.IsLocationHinted(parent._session!, parent._session!.CreateHintCache(), out var Hint))
            {
                var RecievingPlayer = parent._session!.Players.GetPlayerInfo(Hint!.ReceivingPlayer);
                var Item = parent._session.Items.GetItemName(Hint!.ItemId, RecievingPlayer.Game);
                var Important = Hint.ItemFlags.HasFlag(Archipelago.MultiClient.Net.Enums.ItemFlags.Advancement);
                var Usefull = Hint.ItemFlags.HasFlag(Archipelago.MultiClient.Net.Enums.ItemFlags.NeverExclude);
                string Usefullness = Important ? "Progression" : (Usefull ? "Usefull" : "Junk");
                stringBuilder.AppendLine().AppendLine($"Containes {Item} for {RecievingPlayer.Name} playing {RecievingPlayer.Game} ({Usefullness})");
            }

            //await parent.DisplayAlert(Data.ArchipelagoLocationName, stringBuilder.ToString(), "OK");
            var dialog = new CustomDialog(Data.ArchipelagoLocationName, stringBuilder.ToString(), "Get Hint", "Check Location", "Close");

            await parent.Navigation.PushModalAsync(dialog);
            var result = await dialog.ShowAsync();

            if (!parent.HasActiveAP) return;

            if (result == "Get Hint")
            {
                parent._session!.Say($"!hint_location {Data.ArchipelagoLocationName}");
            }
            else if (result == "Check Location")
            {
                var (withinRange, Distance) = OpenStreetMapHelpers.CheckIfWithinRange(parent, Data.Latitude, Data.Longitude, parent.SettingsPage?.MarkerRadius ?? 0);
                if (withinRange)
                {
                    if (!await TryCheckLocation(parent, Data.Id, Map))
                        await parent.DisplayAlert("Could not check location!", $"Missing {Data.KeysRequired - GetCurrentKeyCount(parent)} Keys for this location!", "ok");
                }
                else
                {
                    await parent.DisplayAlert("Could not Check Location!", $"Location was not within range!\n\nMove ~{Math.Round(Distance - (parent.SettingsPage?.MarkerRadius ?? 0))} meters closer!", "ok");
                }

            }

        }

        public static async void CheckLocationsInRange(MainPage parent, WebView Map, Location GeoLocation)
        {
            if (parent._session == null)
                return;

            var result = await Map.EvaluateJavaScriptAsync($"checkProximity({GeoLocation.Latitude}, {GeoLocation.Longitude}, {parent.SettingsPage.MarkerRadius});");
            var cleanResult = result?.Trim('"') ?? "";

            if (string.IsNullOrEmpty(cleanResult))
                return;

            var locationIds = cleanResult.Split(',');
            foreach (var locationId in locationIds)
                await TryCheckLocation(parent, locationId.Trim(), Map);
        }

        public static int GetCurrentKeyCount(MainPage mainPage)
        {
            if (!mainPage.HasActiveAP)
                return -1;
            return mainPage._session!.Items.AllItemsReceived.Where(x => x.ItemName == "Progressive Key").Count();
        }

        public static bool CanLocationBeChecked(MainPage mainPage, string LocationHash) => CanLocationBeChecked(mainPage, LocationHash, out _);
        public static bool CanLocationBeChecked(MainPage mainPage, string LocationHash, out APLocation? location)
        {
            location = null;
            if (!mainPage.HasActiveAP)
                return false;
            if (!mainPage._activeLocationMapping.TryGetValue(LocationHash, out var data))
                return false;
            location = data;
            return CanLocationBeChecked(mainPage, data);
        }
        public static bool CanLocationBeChecked(MainPage mainPage, APLocation location)
        {
            if (!mainPage.HasActiveAP)
                return false;
            if (mainPage._session!.Locations.AllLocationsChecked.Contains(location.ArchipelagoLocationId))
            {
                System.Diagnostics.Debug.WriteLine($"Location {location.ArchipelagoLocationName} already checked in AP");
                return false;
            }
            var currentKeyCount = GetCurrentKeyCount(mainPage);
            if (location.KeysRequired > currentKeyCount)
            {
                System.Diagnostics.Debug.WriteLine($"Not enough keys for {location.ArchipelagoLocationName}. Need {location.KeysRequired}, have {currentKeyCount}");
                return false;
            }
            return true;
        }

        public static async Task<bool> TryCheckLocation(MainPage parent, string locationId, WebView Map)
        {
            if (!CanLocationBeChecked(parent, locationId, out var location))
                return false;

            // Send check to Archipelago
            parent._session!.Locations.CompleteLocationChecks(location!.ArchipelagoLocationId);

            // Remove marker from map
            await Map.EvaluateJavaScriptAsync($"removeMarker('{locationId}');");

            System.Diagnostics.Debug.WriteLine($"✓ Checked location: {location.ArchipelagoLocationName} (ID: {location.ArchipelagoLocationId})");

            return true;
        }



        public static async Task<bool> AssignLocationsToTrips(MainPage parent, Dictionary<string, Trip> trips)
        {
            parent._activeLocationMapping.Clear();

            if (!parent.HasActiveAP)
            {
                await parent.DisplayAlert("Error", "Must be connected to an Archipelago Session", "OK");
                return false;
            }

            if (parent.LastKnownLocation == null)
            {
                await parent.DisplayAlert("Error", "Could not get current location", "OK");
                return false;
            }

            // Get all AP locations for our game
            var allAPLocations = parent._session!.Locations.AllLocations;

            if (allAPLocations.Count != trips.Count)
            {
                await parent.DisplayAlert("Error", "Could not find trip data for all locations", "OK");
                return false;
            }
            if (parent._setupLocations.Count < trips.Count)
            {
                await parent.DisplayAlert("Error", $"You have not defined enough location {parent._setupLocations.Count} " +
                    $"for needed trips {trips.Count}.\nCreated at least {trips.Count - parent._setupLocations.Count} more locations", "OK");
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"Total AP locations: {allAPLocations.Count}");
            System.Diagnostics.Debug.WriteLine($"Total setup locations: {parent._setupLocations.Count}");
            System.Diagnostics.Debug.WriteLine($"Total trips: {trips.Count}");

            // Randomly shuffle setup locations
            var random = new Random();
            var shuffledSetupLocations = parent._setupLocations
                .OrderBy(x => random.Next())
                .Take(trips.Count)
                .OrderBy(x => OpenStreetMapHelpers.CalculateDistance(parent, x.Latitude, x.Longitude))
                .ToArray();

            trips = trips.OrderBy(x => x.Value.DistanceTier).ToDictionary();


            // Assign each trip to a physical location
            int i = 0;
            foreach (var tripEntry in trips)
            {
                var locationName = tripEntry.Key;
                var trip = tripEntry.Value;

                // Get the AP location ID for this location name
                var apLocationId = parent._session.Locations.GetLocationIdFromName("Archipela-Go!", locationName);

                if (apLocationId == -1)
                {
                    await parent.DisplayAlert("Error", $"Could not find AP location ID for {locationName}", "OK");
                    return false;
                }

                var setupLocation = shuffledSetupLocations[i];

                // Store the mapping
                parent._activeLocationMapping[setupLocation.Id] = new(setupLocation, apLocationId, locationName, trip.KeyNeeded, trip.DistanceTier);

                System.Diagnostics.Debug.WriteLine($"Assigned {locationName} (ID: {apLocationId}, Keys: {trip.KeyNeeded}, tier: {trip.DistanceTier}) to location {setupLocation.Id}");

                i++;
            }

            await DataFileHelpers.SaveSeedMapping(parent);
            return true;
        }

        public static bool IsLocationHinted(this APLocation location, ArchipelagoSession session, Dictionary<string, Hint> HintCache, out Hint? hint)
        {
            var CurrentPlayer = session.Players.ActivePlayer.Slot;
            return HintCache.TryGetValue($"{CurrentPlayer}|{location.ArchipelagoLocationId}", out hint);
        }

        public static Dictionary<string, Hint> CreateHintCache(this ArchipelagoSession session) =>
            session.DataStorage.GetHints().ToDictionary(x => $"{x.FindingPlayer}|{x.LocationId}", x => x);
    }
}
