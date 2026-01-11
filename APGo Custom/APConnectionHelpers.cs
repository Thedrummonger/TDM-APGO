using Archipelago.MultiClient.Net;
using Microsoft.Maui.Platform;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
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
            try
            {
                parent._session = ArchipelagoSessionFactory.CreateSession(connectionDetails.Host ?? "", connectionDetails.Port ?? 0);
                var result = parent._session.TryConnectAndLogin("Archipela-Go!", connectionDetails.Slot,
                    Archipelago.MultiClient.Net.Enums.ItemsHandlingFlags.AllItems, password: connectionDetails.Password);

                if (result is LoginFailure failure)
                {
                    await parent.DisplayAlert("Connection Failed", string.Join("\n", failure.Errors), "OK");
                    parent._session = null;
                    return;
                }

                // Update button to green
                ConnetionButton.Text = "🟢";

                parent._currentRoomHash = $"{parent._session.RoomState.Seed}_{connectionDetails.Slot}";

                // Clear markers from map
                await Map.EvaluateJavaScriptAsync("clearAllMarkers();");

                await DataFileHelpers.SaveLastConnectionCache(connectionDetails);

                await parent.DisplayAlert("Connected", "Successfully connected to Archipelago!", "OK");

                Dictionary<string, APLocation>? savedMapping = await DataFileHelpers.LoadSeedMapping(parent._currentRoomHash);

                async Task<bool> ShouldLoadSavedData()
                {
                    return await parent.DisplayAlert("Load Save File","Do you want to loadteh saved seed?","Yes", "No");
                }

                if (savedMapping != null && await ShouldLoadSavedData())
                {
                    parent._activeLocationMapping = savedMapping;
                    System.Diagnostics.Debug.WriteLine($"Loaded existing mapping with {parent._activeLocationMapping.Count} locations");
                }
                else
                {
                    var slotData = parent._session.DataStorage.GetSlotData();
                    if (!slotData.TryGetValue("trips", out var tripsData))
                    {
                        await parent.DisplayAlert("Error", "No trips data found in slot data", "OK");
                        await DisconnectFromArchipelago(parent, Map, ConnetionButton);
                        return;
                    }

                    if (tripsData is not JObject tripsObj)
                    {
                        await parent.DisplayAlert("Error", "Failed to deserialize trips", "OK");
                        await DisconnectFromArchipelago(parent, Map, ConnetionButton);
                        return;
                    }
                    var trips = tripsObj.ToObject<Dictionary<string, Trip>>();

                    if (trips == null)
                    {
                        await parent.DisplayAlert("Error", "Failed to deserialize trips", "OK");
                        await DisconnectFromArchipelago(parent, Map, ConnetionButton);
                        return;
                    }

                    if (trips == null || trips.Count == 0)
                    {
                        await parent.DisplayAlert("Error", "Failed to parse trips data", "OK");
                        await DisconnectFromArchipelago(parent, Map, ConnetionButton);
                        return;
                    }
                    System.Diagnostics.Debug.WriteLine($"Found {trips.Count} trips in slot data");

                    System.Diagnostics.Debug.WriteLine($"FullTripsDict {JsonSerializer.Serialize(trips)}");

                    if (!await APLocationHelpers.AssignLocationsToTrips(parent, trips))
                    {
                        await DisconnectFromArchipelago(parent, Map, ConnetionButton);
                        return;
                    }
                }
                await MarkerHelpers.RenderActiveLocations(parent, Map);
                parent._session.Items.ItemReceived += parent.OnItemReceived;
                parent._session.Locations.CheckedLocationsUpdated += parent.OnLocationsChecked;
                await DataFileHelpers.SaveLastConnectionCache(connectionDetails);
            }
            catch (Exception ex)
            {
                await parent.DisplayAlert("Error", $"Failed to connect: {ex.Message}", "OK");
                parent._session = null;
            }
        }

        public static async Task DisconnectFromArchipelago(MainPage parent, WebView Map, Button ConnetionButton)
        {
            if (parent._session != null)
            {
                parent._session.Items.ItemReceived -= parent.OnItemReceived;
                parent._session.Locations.CheckedLocationsUpdated -= parent.OnLocationsChecked;
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

            await parent.DisplayAlert("Disconnected", "Disconnected from Archipelago", "OK");

            await MarkerHelpers.RenderTemplateLocations(parent, Map);
        }
    }
}
