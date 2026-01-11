namespace APGo_Custom
{
    public class Location
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Id => $"{Latitude:F6}_{Longitude:F6}";

        public Location(){}

        public Location(double lat, double lng)
        {
            Latitude = lat;
            Longitude = lng;
        }
    }
}
