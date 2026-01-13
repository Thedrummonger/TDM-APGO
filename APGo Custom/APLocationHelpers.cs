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
            if (parent._session == null || !parent._session.Socket.Connected) return false;
            switch (parent.GoalSetting)
            {
                case GoalSetting.option_one_hard_travel:
                    return false; //IDK how this one is calculated, unsupported for now
                case GoalSetting.option_allsanity:
                    //Do some saftey checks for this one to make sure we don't goal early. Just checking AllMissingLocations could be dangerous
                    //If it is not initialized and is 0. Also make sure AllLocations is initialized before comapring to AllLocationsChecked 
                    return
                        parent._session.Locations.AllLocations.Count > 0 && 
                        parent._session.Locations.AllLocationsChecked.Count == parent._session.Locations.AllLocations.Count; ;
                case GoalSetting.option_short_macguffin:
                    return ShortGoal.All(parent.GoalItemsRecieved.Contains);
                case GoalSetting.option_long_macguffin:
                    return LongGoal.All(parent.GoalItemsRecieved.Contains);
                default:
                    return false;
            }
        }

        public static async Task DisplayLocationDetails(MainPage parent, WebNavigatingEventArgs e)
        {
            e.Cancel = true;
            if (parent._session == null || !parent._session.Socket.Connected) return;
            var locationId = e.Url.Replace("markerclick://", "");
            if (!parent._activeLocationMapping.TryGetValue(locationId, out var Data))
                return;
            StringBuilder stringBuilder = new StringBuilder($"Keys Required {Data.KeysRequired}");
            if (Data.IsLocationHinted(parent._session, out var Hint))
            {
                var RecievingPlayer = parent._session.Players.GetPlayerInfo(Hint!.ReceivingPlayer);
                var Item = parent._session.Items.GetItemName(Hint!.ItemId, RecievingPlayer.Game);
                var Important = Hint.ItemFlags.HasFlag(Archipelago.MultiClient.Net.Enums.ItemFlags.Advancement);
                var Usefull = Hint.ItemFlags.HasFlag(Archipelago.MultiClient.Net.Enums.ItemFlags.NeverExclude);
                string Usefullness = Important ? "Progression" : (Usefull ? "Usefull" : "Junk");
                stringBuilder.AppendLine().AppendLine($"Containes {Item} for {RecievingPlayer.Name} playing {RecievingPlayer.Game} ({Usefullness})");
            }

            await parent.DisplayAlert(Data.ArchipelagoLocationName, stringBuilder.ToString(), "OK");

        }

        public static async void CheckLocationProximity(MainPage parent, WebView Map, Location GeoLocation)
        {
            if (parent._session == null)
                return;

            var result = await Map.EvaluateJavaScriptAsync($"checkProximity({GeoLocation.Latitude}, {GeoLocation.Longitude}, {parent.SettingsPage.MarkerRadius});");
            var cleanResult = result?.Trim('"') ?? "";

            if (string.IsNullOrEmpty(cleanResult))
                return;

            var locationIds = cleanResult.Split(',');
            foreach (var locationId in locationIds)
                await CheckLocation(parent, locationId.Trim(), Map);
        }

        public static async Task CheckLocation(MainPage parent, string locationId, WebView Map)
        {
            if (parent._session == null || !parent._activeLocationMapping.ContainsKey(locationId))
                return;

            long apLocationId = parent._activeLocationMapping[locationId].ArchipelagoLocationId;

            // Check if already checked in AP
            if (parent._session.Locations.AllLocationsChecked.Contains(apLocationId))
            {
                System.Diagnostics.Debug.WriteLine($"Location {locationId} already checked in AP");
                return;
            }

            // Get the location details
            var location = parent._activeLocationMapping[locationId];
            if (location == null)
            {
                System.Diagnostics.Debug.WriteLine($"Could not find setup location for {locationId}");
                return;
            }

            // Check if we have enough keys
            var currentKeyCount = parent._session.Items.AllItemsReceived.Where(x => x.ItemName == "Progressive Key").Count();
            if (location.KeysRequired > currentKeyCount)
            {
                System.Diagnostics.Debug.WriteLine($"Not enough keys for {location.ArchipelagoLocationName}. Need {location.KeysRequired}, have {currentKeyCount}");
                return;
            }

            // Send check to Archipelago
            parent._session.Locations.CompleteLocationChecks(apLocationId);

            // Remove marker from map
            await Map.EvaluateJavaScriptAsync($"removeMarker('{locationId}');");

            var locationName = location.ArchipelagoLocationName;
            System.Diagnostics.Debug.WriteLine($"✓ Checked location: {locationName} (ID: {apLocationId})");

        }



        public static async Task<bool> AssignLocationsToTrips(MainPage parent, Dictionary<string, Trip> trips)
        {
            parent._activeLocationMapping.Clear();

            // Get all AP locations for our game
            var allAPLocations = parent._session.Locations.AllLocations;

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
            var shuffledSetupLocations = parent._setupLocations.OrderBy(x => random.Next()).ToList();

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
                parent._activeLocationMapping[setupLocation.Id] =
                    new APLocation(shuffledSetupLocations[i], apLocationId, locationName, trip.KeyNeeded);

                System.Diagnostics.Debug.WriteLine($"Assigned {locationName} (ID: {apLocationId}, Keys: {trip.KeyNeeded}) to location {setupLocation.Id}");

                i++;
            }

            await DataFileHelpers.SaveSeedMapping(parent);
            return true;
        }

        public static bool IsLocationHinted(this APLocation location, ArchipelagoSession session, out Hint? hint)
        {
            var Hints = session.DataStorage.GetHints();
            var CurrentPlayer = session.Players.ActivePlayer.Slot;
            hint = Hints.FirstOrDefault(x => x.FindingPlayer == CurrentPlayer && x.LocationId == location.ArchipelagoLocationId);
            return hint is not null;
        }
    }
}
