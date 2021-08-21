using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NeosSpotifyStatus;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using WebSocketSharp.Server;
using IniParser;
using IniParser.Model;

class Program
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

    public static SpotifyClient spotify;
    private static EmbedIOAuthServer _server;
    public static WebSocketServer wsServer = new WebSocketServer(System.Net.IPAddress.Loopback, 1011, false);
    public static int repeatNum;
    static FileIniDataParser parser = new FileIniDataParser();
    static IniData data = parser.ReadFile("config.ini");

    public static async void SendInfo() {
        var currentPlayback = await spotify.Player.GetCurrentPlayback();
        if (currentPlayback == null) {
            Console.WriteLine("No playback detected!");
            wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast("0");
            return;
        }
        if (currentPlayback.Item is FullTrack track) {
            List<string> artists = new List<string>();
            foreach (SimpleArtist artist in track.Artists.ToArray()) {
                artists.Add(artist.Name);
            }

            TimeSpan duration = new TimeSpan(0, 0, 0, 0, track.DurationMs);
            TimeSpan position = new TimeSpan(0, 0, 0, 0, currentPlayback.ProgressMs);
            var covers = track.Album.Images.ToArray();

            //Send messages to Neos
            wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast($"1{track.Name}");
            wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast($"2{string.Join<string>(", ", artists)}");
            wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast($"3{covers[0].Url}");
            wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast($"4{track.Album.Name}");
            wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast($"5{currentPlayback.ProgressMs}");
            wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast($"6{track.DurationMs}");
            wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast($"7{currentPlayback.IsPlaying}");
            switch (currentPlayback.RepeatState) {
                case "off":
                    repeatNum = 2;
                    break;
                case "context":
                    repeatNum = 1;
                    break;
                case "track":
                    repeatNum = 0;
                    break;
            }
            wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast($"8{repeatNum}");
            wsServer.WebSocketServices["/neos-spotify-bridge"].Sessions.Broadcast($"9{currentPlayback.ShuffleState}");
        }
        if (currentPlayback.Item is FullEpisode episode) {
            //Podcasts not supported (yet)
        }
    }

    private static async Task OnAuthorizationCodeReceived(object sender, AuthorizationCodeResponse response) {
        await _server.Stop();

        var config = SpotifyClientConfig.CreateDefault();
        var tokenResponse = await new OAuthClient(config).RequestToken(
          new AuthorizationCodeTokenRequest(
            data["Spotify"]["ClientID"], data["Spotify"]["ClientSecret"], response.Code, new Uri("http://localhost:5000/callback")
          )
        );

        spotify = new SpotifyClient(tokenResponse.AccessToken);

        var user = await spotify.UserProfile.Current();
        Console.WriteLine($"Logged in as {user.DisplayName}");

        while (true) {
            System.Threading.Thread.Sleep(12500);
            SendInfo();
        }
    }

    private static async Task OnErrorReceived(object sender, string error, string state) {
        Console.WriteLine($"Aborting authorization, error received: {error}");
        await _server.Stop();
    }

    static async Task Main() {
        Console.Title = "Neos Spotify Status";

        

        // Make sure "http://localhost:5000/callback" is in your spotify application as redirect uri!
        _server = new EmbedIOAuthServer(new Uri("http://localhost:5000/callback"), 5000);
        await _server.Start();

        _server.AuthorizationCodeReceived += OnAuthorizationCodeReceived;
        _server.ErrorReceived += OnErrorReceived;

        var request = new LoginRequest(_server.BaseUri, data["Spotify"]["ClientID"], LoginRequest.ResponseType.Code) {
            Scope = new List<string> { Scopes.UserReadCurrentlyPlaying, Scopes.UserModifyPlaybackState, Scopes.UserReadPlaybackState, Scopes.UserReadPlaybackPosition, Scopes.UserLibraryRead, Scopes.UserReadPrivate }
        };
        BrowserUtil.Open(request.ToUri());
        
        Console.WriteLine("WebSocket Server running at: " + wsServer.Address + ":" + wsServer.Port);
        wsServer.AddWebSocketService<MainBehavior>("/neos-spotify-bridge");
        wsServer.Start();

        Console.ReadLine();
    }
}