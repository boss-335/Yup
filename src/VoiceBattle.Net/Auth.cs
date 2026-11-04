using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace VoiceBattle.Net
{
    /// <param name="Ok">Accepted.</param>
    /// <param name="SteamId">Verified 64-bit Steam id (null for unauthenticated dev servers).</param>
    /// <param name="Reason">Why it was refused.</param>
    public sealed record AuthResult(bool Ok, string? SteamId, string? Reason)
    {
        public static AuthResult Anonymous => new AuthResult(true, null, null);
        public static AuthResult Refused(string why) => new AuthResult(false, null, why);
    }

    /// <summary>Decides who may play on this server. Runs off the server lock (may do network I/O).</summary>
    public interface IAuthenticator
    {
        Task<AuthResult> Validate(Hello hello);
    }

    /// <summary>Development / LAN: anyone may join, nobody is verified.</summary>
    public sealed class NoAuthenticator : IAuthenticator
    {
        public Task<AuthResult> Validate(Hello hello) => Task.FromResult(AuthResult.Anonymous);
    }

    /// <summary>
    /// Verifies Steam session tickets with the Steamworks Web API
    /// (ISteamUserAuth/AuthenticateUserTicket, partner endpoint). The publisher Web API key comes from the
    /// environment (VB_STEAM_WEBAPI_KEY) and never lives in the repository. Players who are VAC- or
    /// publisher-banned, or who do not own the app (ownersteamid mismatch on family sharing is allowed), are refused.
    /// </summary>
    public sealed class SteamWebApiAuthenticator : IAuthenticator
    {
        public const string DefaultEndpoint = "https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/";
        private readonly HttpClient _http;
        private readonly string _key;
        private readonly uint _appId;
        private readonly string _identity;
        private readonly string _endpoint;

        public SteamWebApiAuthenticator(HttpClient http, string webApiKey, uint appId, string identity, string endpoint = DefaultEndpoint)
        {
            if (string.IsNullOrWhiteSpace(webApiKey)) throw new ArgumentException("Steam Web API key missing (set VB_STEAM_WEBAPI_KEY)");
            _http = http; _key = webApiKey; _appId = appId; _identity = identity; _endpoint = endpoint;
        }

        public async Task<AuthResult> Validate(Hello hello)
        {
            string ticket = hello.SteamTicket ?? "";
            if (ticket.Length == 0 || ticket.Length > 4096 || !IsHex(ticket)) return AuthResult.Refused("steam_ticket_required");
            string url = $"{_endpoint}?key={Uri.EscapeDataString(_key)}&appid={_appId}&ticket={ticket}&identity={Uri.EscapeDataString(_identity)}";
            string body;
            try
            {
                using var resp = await _http.GetAsync(url).ConfigureAwait(false);
                body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return AuthResult.Refused($"steam_http_{(int)resp.StatusCode}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return AuthResult.Refused("steam_unreachable");
            }
            return Parse(body);
        }

        /// <summary>Parses the Web API response (public for tests).</summary>
        public static AuthResult Parse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var r = doc.RootElement.GetProperty("response");
                if (r.TryGetProperty("error", out var err))
                    return AuthResult.Refused("steam_rejected:" + (err.TryGetProperty("errordesc", out var d) ? d.GetString() : "error"));
                var p = r.GetProperty("params");
                if (p.GetProperty("result").GetString() != "OK") return AuthResult.Refused("steam_rejected");
                if (p.TryGetProperty("vacbanned", out var vac) && vac.GetBoolean()) return AuthResult.Refused("vac_banned");
                if (p.TryGetProperty("publisherbanned", out var pub) && pub.GetBoolean()) return AuthResult.Refused("publisher_banned");
                string? id = p.GetProperty("steamid").GetString();
                return string.IsNullOrEmpty(id) ? AuthResult.Refused("steam_rejected") : new AuthResult(true, id, null);
            }
            catch (Exception ex) when (ex is JsonException or System.Collections.Generic.KeyNotFoundException or InvalidOperationException)
            {
                return AuthResult.Refused("steam_bad_response");
            }
        }

        private static bool IsHex(string s)
        {
            foreach (var c in s) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            return s.Length % 2 == 0;
        }
    }
}
