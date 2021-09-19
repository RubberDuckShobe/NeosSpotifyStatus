using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SpotifyAPI.Web;
using WebSocketSharp.Server;

namespace NeosSpotifyStatus
{
    internal static class SpotifyTracker
    {
        public static WebSocketServer wsServer = new WebSocketServer(IPAddress.Loopback, 1011, false);

        private static readonly OAuthClient oAuthClient = new OAuthClient(SpotifyClientConfig.CreateDefault());

        private static readonly ManualResetEventSlim spotifyClientAvailable = new ManualResetEventSlim(false);

        private static DateTime accessExpiry;

        private static AbsoluteTimer.AbsoluteTimer authTimer;

        private static Thread trackingThread;

        public static CurrentlyPlayingContext LastPlayingContext { get; private set; }

        /// <summary>
        /// Gets or sets the playback refresh interval in milliseconds.
        /// </summary>
        public static int RefreshInterval { get; set; } = 30000;

        public static int RepeatNum { get; set; }

        public static SpotifyClient Spotify { get; private set; }

        static SpotifyTracker()
        {
            wsServer.AddWebSocketService<SpotifyPlaybackService>("/neos-spotify-bridge");
            wsServer.Start();

            Console.WriteLine("WebSocket Server running at: " + wsServer.Address + ":" + wsServer.Port);
        }

        public static void ForceFullRefresh()
        {
            LastPlayingContext = null;
            Update();
        }

        public static void Start()
        {
            handleAuthorization();

            spotifyClientAvailable.Wait();

            trackingThread = new Thread(updateLoop);
            trackingThread.Start();
        }

        public static async Task<bool> Update()
        {
            spotifyClientAvailable.Wait();

            // Skip hitting the API when there's no client anyways
            if (wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Count == 0)
                return false;

            var currentPlayback = await Spotify.Player.GetCurrentPlayback();

            if (currentPlayback == null || currentPlayback.Item == null) // move this to individual checks on trackers
            {
                wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast("0");
                return false;
            }

            ContextUpdated?.Invoke(currentPlayback);

            LastPlayingContext = currentPlayback;
            return true;
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
            accessExpiry = DateTime.Now + TimeSpan.FromSeconds(tokenResponse.ExpiresIn);

            Spotify = new SpotifyClient(tokenResponse.AccessToken);

            Console.WriteLine($"Gained Authorization for {(await Spotify.UserProfile.Current()).DisplayName}");
            Console.WriteLine($"Access valid until {accessExpiry.ToLocalTime()}");

            spotifyClientAvailable.Set();
        }

        private static async void handleAuthorization(object _ = null)
        {
            if (string.IsNullOrWhiteSpace(Config.RefreshToken))
            {
                // Get authorization if no refresh token can be loaded
                await gainAuthorization();
            }
            else
            {
                // Try using the refresh token for a new access token or get new auth
                await refreshAuthorization();
            }

            // Set new timer to refresh the access token before it expires
            // Wait until the token expires in two minutes
            var refreshAt = accessExpiry - TimeSpan.FromMinutes(2);
            Console.WriteLine($"Refreshing access at {refreshAt}");

            authTimer?.Dispose();
            authTimer = new AbsoluteTimer.AbsoluteTimer(refreshAt, handleAuthorization, null);
        }

        private static async Task refreshAuthorization()
        {
            spotifyClientAvailable.Reset();

            try
            {
                var refreshResponse = await oAuthClient.RequestToken(
                        new AuthorizationCodeRefreshRequest(Config.ClientId, Config.ClientSecret, Config.RefreshToken));

                accessExpiry = DateTime.Now + TimeSpan.FromSeconds(refreshResponse.ExpiresIn);

                if (!string.IsNullOrWhiteSpace(refreshResponse.RefreshToken))
                    Config.RefreshToken = refreshResponse.RefreshToken;

                Spotify = new SpotifyClient(refreshResponse.AccessToken);

                Console.WriteLine($"Refreshed Access - valid until {accessExpiry.ToLocalTime()}");

                spotifyClientAvailable.Set();
            }
            catch (APIException)
            {
                // Get new authorization if refresh fails
                await gainAuthorization();
            }
        }

        private static async void updateLoop()
        {
            // TODO: Change this to using a Threading.Timer
            var nextWait = 0;

            while (true)
            {
                Thread.Sleep(nextWait);

                var hitApi = await Update();

                // Refresh earlier when the track ends, or to check if no one is connected
                nextWait = hitApi ? Math.Min(RefreshInterval, LastPlayingContext.Item.GetDuration() - LastPlayingContext.ProgressMs + 1000) : 5000;
            }
        }

        public static event Action<CurrentlyPlayingContext> ContextUpdated;
    }
}