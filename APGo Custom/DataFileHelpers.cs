using Microsoft.Maui.Platform;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace APGo_Custom
{
    internal class DataFileHelpers
    {
        public const string SetupLocationsPath = "setup_locations.json";
        public const string ConnectionCachePath = "connection_cache.json";
        public static async Task SaveSetupLocations(MainPage parent)
        {
            var json = JsonSerializer.Serialize(parent._setupLocations);
            var filePath = Path.Combine(FileSystem.AppDataDirectory, SetupLocationsPath);
            await File.WriteAllTextAsync(filePath, json);
            System.Diagnostics.Debug.WriteLine($"Saved {parent._setupLocations.Count} locations to: {filePath}");
        }

        public static async Task LoadSetupLocations(MainPage parent, WebView Map)
        {
            var filePath = Path.Combine(FileSystem.AppDataDirectory, SetupLocationsPath);
            System.Diagnostics.Debug.WriteLine($"Looking for locations at: {filePath}");

            if (File.Exists(filePath))
            {
                var json = await File.ReadAllTextAsync(filePath);
                System.Diagnostics.Debug.WriteLine($"File content: {json}");

                parent._setupLocations = JsonSerializer.Deserialize<List<BaseLocation>>(json) ?? new List<BaseLocation>();
                System.Diagnostics.Debug.WriteLine($"Loaded {parent._setupLocations.Count} locations");
                await MarkerHelpers.RenderTemplateLocations(parent, Map);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No saved locations file found");
            }
        }

        public static void ClearSetupLocations()
        {
            var filePath = Path.Combine(FileSystem.AppDataDirectory, SetupLocationsPath);
            if (File.Exists(filePath))
                File.Delete(filePath);
        }

        public static async Task SaveSeedMapping(MainPage parent)
        {
            var json = JsonSerializer.Serialize(parent._activeLocationMapping);
            var filePath = Path.Combine(FileSystem.AppDataDirectory, $"seed_{parent._currentRoomHash}.json");
            await File.WriteAllTextAsync(filePath, json);
            System.Diagnostics.Debug.WriteLine($"Saved mapping for seed: {parent._currentRoomHash}");
        }

        public static async Task<Dictionary<string, APLocation>?> LoadSeedMapping(string roomHash)
        {
            var filePath = Path.Combine(FileSystem.AppDataDirectory, $"seed_{roomHash}.json");
            if (File.Exists(filePath))
            {
                var json = await File.ReadAllTextAsync(filePath);
                var mapping = JsonSerializer.Deserialize<Dictionary<string, APLocation>>(json);
                System.Diagnostics.Debug.WriteLine($"Loaded mapping for seed: {roomHash}");
                return mapping;
            }
            System.Diagnostics.Debug.WriteLine($"No mapping found for seed: {roomHash}");
            return null;
        }

        public static void RemoveSeedMappings()
        {
            if (Directory.Exists(FileSystem.AppDataDirectory))
                foreach (var i in Directory.GetFiles(FileSystem.AppDataDirectory))
                    if (Path.GetFileName(i).StartsWith("seed_") && Path.GetFileName(i).EndsWith(".json"))
                        File.Delete(i);
        }

        public static async Task SaveLastConnectionCache(ConnectionDetails Details)
        {
            var json = JsonSerializer.Serialize(Details);
            var filePath = Path.Combine(FileSystem.AppDataDirectory, ConnectionCachePath);
            await File.WriteAllTextAsync(filePath, json);
            System.Diagnostics.Debug.WriteLine($"Chaching Connectiong Success: {Details.Slot}@{Details.Host}:{Details.Port}");
        }

        public static async Task<ConnectionDetails?> LoadLastConnectionCache()
        {
            var filePath = Path.Combine(FileSystem.AppDataDirectory, ConnectionCachePath);
            if (File.Exists(filePath))
            {
                var json = await File.ReadAllTextAsync(filePath);
                var mapping = JsonSerializer.Deserialize<ConnectionDetails>(json);
                System.Diagnostics.Debug.WriteLine($"File Content: {json}");
                System.Diagnostics.Debug.WriteLine($"Loaded Connection Cache: {mapping?.Slot}@{mapping?.Host}:{mapping?.Port}");
                return mapping;
            }
            System.Diagnostics.Debug.WriteLine($"Cache Missing");
            return null;
        }
        public static void ClearLastConnectionCache()
        {
            var filePath = Path.Combine(FileSystem.AppDataDirectory, ConnectionCachePath);
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }
}
