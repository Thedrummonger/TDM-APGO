using Archipelago.MultiClient.Net;
using Microsoft.Maui.Platform;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace APGo_Custom
{
    internal class APConnectionHelpers
    {

        public static async Task ShowConnectionDialog(MainPage parent, WebView Map, Button ConnetionButton)
        {
            var ConnectionCache = await DataFileHelpers.LoadLastConnectionCache();

            string HostPlaceHolder = ConnectionCache is null || string.IsNullOrWhiteSpace(ConnectionCache.Host) ?
                "archipelago.gg" : ConnectionCache.Host;
            string PortPlaceholder = (ConnectionCache is null || ConnectionCache.Port is null || ConnectionCache.Port.Value < 1) ?
                "38281" : ConnectionCache.Port.Value.ToString();
            string SlotPlaceholder = (ConnectionCache is null || string.IsNullOrWhiteSpace(ConnectionCache.Slot)) ?
                "" : ConnectionCache.Slot;
            string PassPlaceholder = (ConnectionCache is null || string.IsNullOrWhiteSpace(ConnectionCache.Password)) ?
                "" : ConnectionCache.Password;

            string address = await parent.DisplayPromptAsync("Connect", "Server Address:",
                initialValue: HostPlaceHolder,
                placeholder: "archipelago.gg");

            if (string.IsNullOrWhiteSpace(address))
                return;

            string portStr = await parent.DisplayPromptAsync("Connect", "Port:",
                initialValue: PortPlaceholder,
                keyboard: Keyboard.Numeric);

            if (string.IsNullOrWhiteSpace(portStr) || !int.TryParse(portStr, out int port))
                return;

            string slotName = await parent.DisplayPromptAsync("Connect", "Slot Name:",
                initialValue: SlotPlaceholder,
                placeholder: "Player1");

            if (string.IsNullOrWhiteSpace(slotName))
                return;

            string password = await parent.DisplayPromptAsync("Connect", "Password (optional):",
                initialValue: PassPlaceholder,
                placeholder: "Leave blank if none");

            await ConnectToArchipelago(parent, Map, new ConnectionDetails(address, port, slotName, password ?? ""), ConnetionButton);
        }

        public static async Task ConnectToArchipelago(MainPage parent, WebView Map, ConnectionDetails connectionDetails, Button ConnetionButton)
        {
            ConnetionButton.Text = "🟡";
            try
            {
                parent._chatMessageQueue.Enqueue($"Connecting to {connectionDetails.Slot}@{connectionDetails.Host ?? ""}:{connectionDetails.Port ?? 0}");
                parent._session = ArchipelagoSessionFactory.CreateSession(connectionDetails.Host ?? "", connectionDetails.Port ?? 0);
                var result = parent._session.TryConnectAndLogin("Archipela-Go!", connectionDetails.Slot,
                    Archipelago.MultiClient.Net.Enums.ItemsHandlingFlags.AllItems, password: connectionDetails.Password);

                if (result is LoginFailure failure)
                {
                    await parent.DisplayAlert("Connection Failed", string.Join("\n", failure.Errors), "OK");
                    parent._session = null;
                    ConnetionButton.Text = "🔴";
                    return;
                }

                parent._currentRoomHash = $"{parent._session.RoomState.Seed}_{connectionDetails.Slot}";

                // Clear markers from map
                await Map.EvaluateJavaScriptAsync("clearAllMarkers();");

                await DataFileHelpers.SaveLastConnectionCache(connectionDetails);

                parent._chatMessageQueue.Enqueue($"Successfully connected to Archipelago!");
                //await parent.DisplayAlert("Connected", "Successfully connected to Archipelago!", "OK");

                Dictionary<string, APLocation>? savedMapping = await DataFileHelpers.LoadSeedMapping(parent._currentRoomHash);

                if (savedMapping != null)
                {
                    parent._activeLocationMapping = savedMapping;
                    System.Diagnostics.Debug.WriteLine($"Loaded existing mapping with {parent._activeLocationMapping.Count} locations");
                }
                else
                {
                    var slotData = parent._session.DataStorage.GetSlotData();
                    if (!slotData.TryGetValue("trips", out var tripsData) || tripsData is not JObject tripsObj)
                    {
                        parent._chatMessageQueue.Enqueue($"Could not parse trips data in slot data!");
                        //await parent.DisplayAlert("Error", "Could not parse trips data in slot data", "OK");
                        await DisconnectFromArchipelago(parent, Map, ConnetionButton, true);
                        return;
                    }
                    var trips = tripsObj.ToObject<Dictionary<string, Trip>>();
                    if (!slotData.TryGetValue("goal", out var goalData) || goalData is not long goalVal)
                    {
                        parent._chatMessageQueue.Enqueue($"Could not parse Goal data in slot data!");
                        //await parent.DisplayAlert("Error", "Could not parse Goal data in slot data", "OK");
                        await DisconnectFromArchipelago(parent, Map, ConnetionButton, true);
                        return;
                    }
                    Debug.WriteLine($"goalData int {goalVal}");
                    parent.GoalSetting = (GoalSetting)goalVal;

                    if (trips == null || trips.Count == 0)
                    {
                        parent._chatMessageQueue.Enqueue($"Failed to parse trips data!");
                        //await parent.DisplayAlert("Error", "Failed to parse trips data", "OK");
                        await DisconnectFromArchipelago(parent, Map, ConnetionButton, true);
                        return;
                    }
                    System.Diagnostics.Debug.WriteLine($"Found {trips.Count} trips in slot data");

                    System.Diagnostics.Debug.WriteLine($"FullTripsDict {JsonSerializer.Serialize(trips)}");

                    if (!await APLocationHelpers.AssignLocationsToTrips(parent, trips))
                    {
                        await DisconnectFromArchipelago(parent, Map, ConnetionButton, true);
                        return;
                    }
                }
                await MarkerHelpers.RenderActiveLocations(parent, Map);
                parent._session.Items.ItemReceived += parent.OnItemReceived;
                parent._session.Locations.CheckedLocationsUpdated += parent.OnLocationsChecked;
                parent._session.MessageLog.OnMessageReceived += parent.OnArchipelagoMessageReceived;
                await DataFileHelpers.SaveLastConnectionCache(connectionDetails);

                // Update button to green
                ConnetionButton.Text = "🟢";
            }
            catch (Exception ex)
            {
                parent._chatMessageQueue.Enqueue($"Failed to connect: {ex.Message}");
                //await parent.DisplayAlert("Error", $"Failed to connect: {ex.Message}", "OK");
                await DisconnectFromArchipelago(parent, Map, ConnetionButton, true);
                parent._session = null;
            }
        }

        public static async Task DisconnectFromArchipelago(MainPage parent, WebView Map, Button ConnetionButton, bool Early = false)
        {
            if (parent._session != null)
            {
                if (!Early)
                {
                    parent._session.Items.ItemReceived -= parent.OnItemReceived;
                    parent._session.Locations.CheckedLocationsUpdated -= parent.OnLocationsChecked;
                    parent._session.MessageLog.OnMessageReceived -= parent.OnArchipelagoMessageReceived;
                }
                if (parent._session.Socket != null && parent._session.Socket.Connected)
                    await parent._session.Socket.DisconnectAsync();
            }
            parent._session = null;
            parent._currentRoomHash = null;
            parent._activeLocationMapping.Clear();

            // Update button to red
            ConnetionButton.Text = "🔴";

            // Clear markers from map
            await Map.EvaluateJavaScriptAsync("clearAllMarkers();");

            parent._chatMessageQueue.Enqueue($"Disconnected from Archipelago");
            //await parent.DisplayAlert("Disconnected", "Disconnected from Archipelago", "OK");

            await MarkerHelpers.RenderTemplateLocations(parent, Map);
        }
    }
}
