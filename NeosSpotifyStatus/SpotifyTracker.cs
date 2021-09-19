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
    internal static class SpotifyTracker
    {
        public static WebSocketServer wsServer = new WebSocketServer(IPAddress.Loopback, 1011, false);

        private static readonly List<ChangeTracker> changeTrackers = new List<ChangeTracker>()
        {
            new ChangeTracker(nC => PlayableChanged?.Invoke(SpotifyInfo.Playable, nC.Item.GetResource()),
                (oC, nC) => !oC.Item.GetResource().Equals(nC.Item.GetResource())),
            new ChangeTracker(nC => CreatorChanged?.Invoke(SpotifyInfo.Creator, nC.Item.GetCreators()),
                (oC, nC) =>
                {
                    var oCreators = oC.Item.GetCreators();
                    var nCreators = nC.Item.GetCreators();

                    return oCreators.Count() != nCreators.Count()
                    || oCreators.Except(nCreators).Any()
                    || nCreators.Except(oCreators).Any();
                }),
            new ChangeTracker(nC => CoverChanged?.Invoke(SpotifyInfo.Cover, nC.Item.GetCover()),
                (oC, nC) => oC.Item.GetCover() != nC.Item.GetCover()),
            new ChangeTracker(nC => GroupingChanged?.Invoke(SpotifyInfo.Grouping, nC.Item.GetGrouping()),
                (oC, nC) => !oC.Item.GetGrouping().Equals(nC.Item.GetGrouping())),
            new ChangeTracker(nC => ProgressChanged?.Invoke(SpotifyInfo.Progress, nC.ProgressMs),
                (oC, nC) => oC.ProgressMs != nC.ProgressMs),
            new ChangeTracker(nC => DurationChanged?.Invoke(SpotifyInfo.Duration, nC.Item.GetDuration()),
                (oC, nC) => oC.Item.GetDuration() != nC.Item.GetDuration()),
            new ChangeTracker(nC => IsPlayingChanged?.Invoke(SpotifyInfo.IsPlaying, nC.IsPlaying),
                (oC, nC) => oC.IsPlaying != nC.IsPlaying),
            new ChangeTracker(nC => RepeatStateChanged?.Invoke(SpotifyInfo.RepeatState, (int)SpotifyHelper.GetState(nC.RepeatState)),
                (oC, nC) => oC.RepeatState != nC.RepeatState),
            new ChangeTracker(nC => IsShuffledChanged?.Invoke(SpotifyInfo.IsShuffled, nC.ShuffleState),
                (oC, nC) => oC.ShuffleState != nC.ShuffleState),
        };

        private static readonly OAuthClient oAuthClient = new OAuthClient(SpotifyClientConfig.CreateDefault());

        private static readonly ManualResetEventSlim spotifyClientAvailable = new ManualResetEventSlim(false);

        private static DateTime accessExpiry;

        private static Thread renewingThread;

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
            renewingThread = new Thread(authorizationTracker);
            renewingThread.Start();

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

            sendOutUpdates(currentPlayback);

            LastPlayingContext = currentPlayback;
            return true;
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

            if (!string.IsNullOrWhiteSpace(refreshResponse.RefreshToken))
                Config.RefreshToken = refreshResponse.RefreshToken;

            Spotify = new SpotifyClient(refreshResponse.AccessToken);

            Console.WriteLine($"Refreshed Access - valid until {accessExpiry.ToLocalTime()}");

            spotifyClientAvailable.Set();
        }

        private static void sendOutUpdates(CurrentlyPlayingContext newPlayingContext)
        {
            foreach (var changeTracker in changeTrackers.Where(changeTracker => LastPlayingContext == null || changeTracker.TestChanged(LastPlayingContext, newPlayingContext)))
                changeTracker.InvokeEvent(newPlayingContext);
        }

        private static async void updateLoop()
        {
            var nextWait = 0;

            while (true)
            {
                Thread.Sleep(nextWait);

                var hitApi = await Update();

                // Refresh earlier when the track ends, or to check if no one is connected
                nextWait = hitApi ? Math.Min(RefreshInterval, LastPlayingContext.Item.GetDuration() - LastPlayingContext.ProgressMs + 1000) : 5000;
            }
        }

        public static event SpotifyTrackerChangedHandler<string> CoverChanged;

        public static event SpotifyTrackerChangedHandler<IEnumerable<SpotifyResource>> CreatorChanged;

        public static event SpotifyTrackerChangedHandler<int> DurationChanged;

        public static event SpotifyTrackerChangedHandler<SpotifyResource> GroupingChanged;

        public static event SpotifyTrackerChangedHandler<bool> IsPlayingChanged;

        public static event SpotifyTrackerChangedHandler<bool> IsShuffledChanged;

        public static event SpotifyTrackerChangedHandler<SpotifyResource> PlayableChanged;

        public static event SpotifyTrackerChangedHandler<int> ProgressChanged;

        public static event SpotifyTrackerChangedHandler<int> RepeatStateChanged;

        public delegate void SpotifyTrackerChangedHandler<in T>(SpotifyInfo info, T newValue);

        private class ChangeTracker
        {
            public Action<CurrentlyPlayingContext> InvokeEvent { get; }

            public Func<CurrentlyPlayingContext, CurrentlyPlayingContext, bool> TestChanged { get; }

            public ChangeTracker(Action<CurrentlyPlayingContext> invokeEvent, Func<CurrentlyPlayingContext, CurrentlyPlayingContext, bool> testChanged)
            {
                InvokeEvent = invokeEvent;
                TestChanged = testChanged;
            }
        }
    }
}