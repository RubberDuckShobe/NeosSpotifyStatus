using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SpotifyAPI.Web;
using WebSocketSharp.Server;

namespace NeosSpotifyStatus
{
    /*
     * List of the prefixes/commands sent to Neos via WebSockets:
     * 0 - clear everything
     * 1 - Title
     * 2 - Artist(s)
     * 3 - Cover URL
     * 4 - Album name
     * 5 - Current song position (in ms)
     * 6 - Total song duration (in ms)
     * 7 - Is playing
     * 8 - Repeat state
     * 9 - Shuffle state
     */

    internal static class SpotifyTracker
    {
        public static WebSocketServer wsServer = new WebSocketServer(IPAddress.Loopback, 1011, false);
        private static readonly OAuthClient oAuthClient = new OAuthClient(SpotifyClientConfig.CreateDefault());
        private static readonly ManualResetEventSlim spotifyClientAvailable = new ManualResetEventSlim(false);
        private static DateTime accessExpiry;
        private static Thread renewingThread;
        private static Thread trackingThread;

        /// <summary>
        /// Gets or sets the playback refresh interval in milliseconds.
        /// </summary>
        public static int RefreshInterval { get; set; } = 30000;

        public static int RepeatNum { get; set; }
        public static SpotifyClient Spotify { get; private set; }

        static SpotifyTracker()
        {
            wsServer.AddWebSocketService<MainBehavior>("/neos-spotify-bridge");
            wsServer.Start();

            Console.WriteLine("WebSocket Server running at: " + wsServer.Address + ":" + wsServer.Port);
        }

        public static int SendInfo(CurrentlyPlayingContext currentPlayback)
        {
            switch (currentPlayback.Item)
            {
                case FullTrack track:
                    TimeSpan duration = new TimeSpan(0, 0, 0, 0, track.DurationMs);
                    TimeSpan position = new TimeSpan(0, 0, 0, 0, currentPlayback.ProgressMs);

                    // Send messages to Neos
                    wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast($"1{track.Name}");
                    wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast($"2{string.Join<string>(", ", track.Artists.Select(artist => artist.Name))}");
                    wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast($"3{track.Album.Images[0].Url}");
                    wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast($"4{track.Album.Name}");
                    wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast($"5{currentPlayback.ProgressMs}");
                    wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast($"6{track.DurationMs}");
                    wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast($"7{currentPlayback.IsPlaying}");
                    switch (currentPlayback.RepeatState)
                    {
                        case "off":
                            RepeatNum = 2;
                            break;

                        case "context":
                            RepeatNum = 1;
                            break;

                        case "track":
                            RepeatNum = 0;
                            break;
                    }
                    wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast($"8{RepeatNum}");
                    wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast($"9{currentPlayback.ShuffleState}");

                    return track.DurationMs - currentPlayback.ProgressMs;

                case FullEpisode episode:
                    //Podcasts not supported (yet)
                    return episode.DurationMs - currentPlayback.ProgressMs;

                default:
                    return int.MaxValue;
            }
        }

        public static void Start()
        {
            renewingThread = new Thread(authorizationTracker);
            renewingThread.Start();

            spotifyClientAvailable.Wait();

            trackingThread = new Thread(updateLoop);
            trackingThread.Start();
        }

        private static async void authorizationTracker()
        {
            if (string.IsNullOrWhiteSpace(Config.RefreshToken))
            {
                // Get authorization if no refresh token can be loaded
                await gainAuthorization();
            }
            else
            {
                try
                {
                    // Try using the refresh token for a new access token
                    await refreshAuthorization();
                }
                catch (APIUnauthorizedException)
                {
                    // Get new authorization if refresh fails
                    await gainAuthorization();
                }
            }

            // Keep refreshing the access token before it expires
            while (true)
            {
                // Wait until the token expires in two minutes
                var refreshIn = accessExpiry - DateTime.UtcNow - TimeSpan.FromMinutes(2);
                Console.WriteLine($"Refreshing access in {refreshIn}");
                Thread.Sleep(refreshIn);

                await refreshAuthorization();
            }
        }

        private static async Task gainAuthorization()
        {
            spotifyClientAvailable.Reset();

            var code = await SpotifyAuthorization.RequestAuthorization();

            var tokenResponse = await oAuthClient.RequestToken(
              new AuthorizationCodeTokenRequest(
                Config.ClientId, Config.ClientSecret, code, new Uri("http://localhost:5000/callback")
              )
            );

            Config.RefreshToken = tokenResponse.RefreshToken;
            accessExpiry = tokenResponse.CreatedAt + TimeSpan.FromSeconds(tokenResponse.ExpiresIn);

            Spotify = new SpotifyClient(tokenResponse.AccessToken);

            Console.WriteLine($"Gained Authorization for {(await Spotify.UserProfile.Current()).DisplayName}");
            Console.WriteLine($"Access valid until {accessExpiry.ToLocalTime()}");

            spotifyClientAvailable.Set();
        }

        private static async Task refreshAuthorization()
        {
            spotifyClientAvailable.Reset();

            var refreshResponse = await oAuthClient.RequestToken(
                    new AuthorizationCodeRefreshRequest(Config.ClientId, Config.ClientSecret, Config.RefreshToken));

            accessExpiry = refreshResponse.CreatedAt + TimeSpan.FromSeconds(refreshResponse.ExpiresIn);
            Config.RefreshToken = refreshResponse.RefreshToken;

            Spotify = new SpotifyClient(refreshResponse.AccessToken);

            Console.WriteLine($"Refreshed Access - valid until {accessExpiry.ToLocalTime()}");

            spotifyClientAvailable.Set();
        }

        private static async void updateLoop()
        {
            var nextWait = 0;

            while (true)
            {
                Thread.Sleep(nextWait);

                // Skip hitting the API when there's no client anyways
                if (wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Count == 0)
                {
                    nextWait = 5000;
                    continue;
                }

                var currentPlayback = await Spotify.Player.GetCurrentPlayback();

                if (currentPlayback == null)
                {
                    wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast("0");
                    nextWait = 5000;
                    continue;
                }

                var remainingDuration = SendInfo(currentPlayback) + 1000;

                // Refresh earlier when the track ends
                nextWait = Math.Min(RefreshInterval, remainingDuration);
            }
        }
    }
}