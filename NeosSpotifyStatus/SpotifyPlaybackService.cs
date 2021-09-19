using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;
using SpotifyAPI.Web;
using System.Text.RegularExpressions;
using System.Globalization;

namespace NeosSpotifyStatus
{
    internal class SpotifyPlaybackService : WebSocketBehavior
    {
        private static readonly Regex spotifyUriEx = new Regex(@"(?:spotify:|https?:\/\/open\.spotify\.com\/)(episode|track)[:\/]([0-9A-z]+)");

        protected override void OnClose(CloseEventArgs e)
        {
            Console.WriteLine($"A connection was closed! Reason: {e.Reason}, Code: {e.Code}");

            SpotifyTracker.PlayableChanged -= handleChangedResource;
            SpotifyTracker.CreatorChanged -= handleChangedResources;
            SpotifyTracker.CoverChanged -= sendMessage;
            SpotifyTracker.GroupingChanged -= handleChangedResource;
            SpotifyTracker.ProgressChanged -= handleChangedInt;
            SpotifyTracker.DurationChanged -= handleChangedInt;
            SpotifyTracker.IsPlayingChanged -= handleChangedBool;
            SpotifyTracker.RepeatStateChanged -= handleChangedInt;
            SpotifyTracker.IsShuffledChanged -= handleChangedBool;
        }

        protected override async void OnMessage(MessageEventArgs e)
        {
            //I'm not going to simply get the playback data on every message received
            //That would be a lot of unneeded requests
            //and I'm already getting ratelimited
            int commandCode = int.Parse(e.Data[0].ToString());
            string commandData = e.Data.Remove(0, 1);
            Console.WriteLine($"Command {commandCode} received, data: {commandData}");
            try
            {
                switch (commandCode)
                {
                    case 0:
                        var playback = await SpotifyTracker.Spotify.Player.GetCurrentPlayback();
                        if (playback == null)
                        {
                            Console.WriteLine("No playback detected!");
                            break;
                        }
                        if (playback.IsPlaying)
                        {
                            await SpotifyTracker.Spotify.Player.PausePlayback();
                        }
                        else
                        {
                            await SpotifyTracker.Spotify.Player.ResumePlayback();
                        }
                        break;

                    case 1:
                        await SpotifyTracker.Spotify.Player.SkipPrevious();
                        break;

                    case 2:
                        await SpotifyTracker.Spotify.Player.SkipNext();
                        break;

                    case 3:
                        // Do nothing because update is done at the end anyways
                        break;

                    case 4:
                        var targetState = SpotifyHelper.GetState(SpotifyTracker.LastPlayingContext.RepeatState).Next();

                        if (int.TryParse(commandData, out var repeatNum))
                            targetState = (PlayerSetRepeatRequest.State)repeatNum;

                        var repeatRequest = new PlayerSetRepeatRequest(targetState);
                        await SpotifyTracker.Spotify.Player.SetRepeat(repeatRequest);
                        break;

                    case 5:
                        var playback2 = await SpotifyTracker.Spotify.Player.GetCurrentPlayback();
                        if (playback2 == null)
                        {
                            Console.WriteLine("No playback detected!");
                            break;
                        }
                        bool doShuffle;
                        if (playback2.ShuffleState)
                        {
                            doShuffle = false;
                        }
                        else
                        {
                            doShuffle = true;
                        }
                        PlayerShuffleRequest shuffleRequest = new PlayerShuffleRequest(doShuffle);
                        await SpotifyTracker.Spotify.Player.SetShuffle(shuffleRequest);
                        break;

                    case 6:
                        PlayerSeekToRequest seekRequest = new(int.Parse(commandData));
                        await SpotifyTracker.Spotify.Player.SeekTo(seekRequest);
                        break;

                    case 7:
                        var match = spotifyUriEx.Match(commandData);
                        if (!match.Success)
                            break;

                        var addRequest = new PlayerAddToQueueRequest($"{match.Groups[0]}:{match.Groups[1]}");
                        await SpotifyTracker.Spotify.Player.AddToQueue(addRequest);
                        // Send parse / add confirmation?
                        // Maybe general toast command
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception caught: {ex}");
            }

            await Task.Delay(500);

            SpotifyTracker.Update();
        }

        protected override void OnOpen()
        {
            Console.WriteLine("New connection opened, list of sessions:");
            Console.WriteLine($"    {string.Join(Environment.NewLine + "    ", Sessions.ActiveIDs)}");

            SpotifyTracker.PlayableChanged += handleChangedResource;
            SpotifyTracker.CreatorChanged += handleChangedResources;
            SpotifyTracker.CoverChanged += sendMessage;
            SpotifyTracker.GroupingChanged += handleChangedResource;
            SpotifyTracker.ProgressChanged += handleChangedInt;
            SpotifyTracker.DurationChanged += handleChangedInt;
            SpotifyTracker.IsPlayingChanged += handleChangedBool;
            SpotifyTracker.RepeatStateChanged += handleChangedInt;
            SpotifyTracker.IsShuffledChanged += handleChangedBool;

            Console.WriteLine($"Added event handlers for {ID}");

            Task.Run(() =>
            {
                Task.Delay(5000);
                SpotifyTracker.ForceFullRefresh();
            });
        }

        private void handleChangedBool(SpotifyInfo info, bool value)
        {
            sendMessage(info, value.ToString(CultureInfo.InvariantCulture));
        }

        private void handleChangedInt(SpotifyInfo info, int value)
        {
            sendMessage(info, value.ToString(CultureInfo.InvariantCulture));
        }

        private void handleChangedResource(SpotifyInfo info, SpotifyResource resource)
        {
            sendMessage(info, resource.Name);
            //sendMessage(info & SpotifyInfo.ResourceUri, resource.Uri);
        }

        private void handleChangedResources(SpotifyInfo info, IEnumerable<SpotifyResource> resources)
        {
            sendMessage(info, string.Join(", ", resources.Select(res => res.Name)));
            //sendMessage(info & SpotifyInfo.ResourceUri, string.Join(", ", resources.Select(res => res.Uri)));
        }

        private void sendMessage(SpotifyInfo info, string data)
        {
            Console.WriteLine($"Sending {info.ToUpdateInt()}{data} to {ID}");
            Task.Run(() => Send($"{info.ToUpdateInt()}{data}"));
        }

        /*
         * List of the prefixes/commands received from Neos via WebSockets:
         * 0 - Pause/Resume
         * 1 - Previous track
         * 2 - Next track
         * 3 - Re-request info
         * 4 - Repeat status change
         * 5 - Toggle shuffle
         * 6 - Seek to position
         * 7 - Add Item to Queue
        */
    }
}