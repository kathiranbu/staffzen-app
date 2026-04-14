namespace APM.StaffZen.Blazor.Configuration
{
    public class ApiSettings
    {
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// Plain HTTP URL used by the mobile GPS tracker on the local network.
        /// The dev HTTPS certificate is only valid for "localhost" — phones on
        /// the LAN must use HTTP to avoid SSL handshake failures (ERR_EMPTY_RESPONSE).
        /// Example: "http://192.168.0.176:5080"  ← set this to your laptop's LAN IP.
        /// If empty, falls back to BaseUrl.
        /// </summary>
        public string HttpBaseUrl { get; set; } = string.Empty;
    }
}
