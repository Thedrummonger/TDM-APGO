using System.Text.Json.Serialization;

namespace APGo_Custom
{
    public class Location
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Id => $"{Latitude:F6}_{Longitude:F6}";

        public Location() { }

        public Location(double lat, double lng)
        {
            Latitude = lat;
            Longitude = lng;
        }
    }

    public class APLocation : Location
    {
        public long ArchipelagoLocationId { get; set; } = -1;
        public string ArchipelagoLocationName { get; set; } = "";
        public int KeysRequired { get; set; } = 0;

        public APLocation() { }
        public APLocation(Location location, long APLocID, string APLocName, int keys)
        {
            Latitude = location.Latitude;
            Longitude = location.Longitude;
            ArchipelagoLocationId = APLocID;
            ArchipelagoLocationName = APLocName;
            KeysRequired = keys;
        }
    }

    public class Trip
    {
        [Newtonsoft.Json.JsonProperty("distance_tier")]
        public int DistanceTier { get; set; }

        [Newtonsoft.Json.JsonProperty("key_needed")]
        public int KeyNeeded { get; set; }

        [Newtonsoft.Json.JsonProperty("speed_tier")]
        public int SpeedTier { get; set; }

        public Trip() { }
    }
}
