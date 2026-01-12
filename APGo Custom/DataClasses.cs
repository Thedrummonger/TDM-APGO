using System.Text.Json.Serialization;

namespace APGo_Custom
{

    public enum GoalSetting
    {
        option_one_hard_travel = 0,
        option_allsanity = 1,
        option_short_macguffin = 2,
        option_long_macguffin = 3,
    }
    public class BaseLocation
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Id => $"{Latitude:F6}_{Longitude:F6}";

        public BaseLocation() { }

        public BaseLocation(double lat, double lng)
        {
            Latitude = lat;
            Longitude = lng;
        }
    }

    public class APLocation : BaseLocation
    {
        public long ArchipelagoLocationId { get; set; } = -1;
        public string ArchipelagoLocationName { get; set; } = "";
        public int KeysRequired { get; set; } = 0;

        public APLocation() { }
        public APLocation(BaseLocation location, long APLocID, string APLocName, int keys)
        {
            Latitude = location.Latitude;
            Longitude = location.Longitude;
            ArchipelagoLocationId = APLocID;
            ArchipelagoLocationName = APLocName;
            KeysRequired = keys;
        }
    }

    public class ConnectionDetails
    {
        public ConnectionDetails() { }
        public ConnectionDetails(string host, int port, string slotName, string password)
        {
            Host = host;
            Port = port;
            Slot = slotName;
            Password = password;
        }
        public string? Host { get; set; }
        public int? Port { get; set; }
        public string? Slot { get; set; }
        public string? Password { get; set; }
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
