using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;
using SpotifyAPI.Web;
using System.Text.RegularExpressions;

namespace NeosSpotifyStatus
{
    internal class MainBehavior : WebSocketBehavior
    {
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

        private static readonly Regex spotifyUriEx = new Regex(@"(?:spotify:|https?:\/\/open\.spotify\.com\/)(episode|track)[:\/]([0-9A-z]+)");

        protected override void OnClose(CloseEventArgs e)
        {
            Console.WriteLine($"A connection was closed! Reason: {e.Reason}, Code: {e.Code}");
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
                        var currentPlayback = await SpotifyTracker.Spotify.Player.GetCurrentPlayback();
                        SpotifyTracker.SendInfo(currentPlayback);
                        break;

                    case 4:
                        SpotifyTracker.RepeatNum = int.Parse(commandData);
                        PlayerSetRepeatRequest repeatRequest = new((PlayerSetRepeatRequest.State)SpotifyTracker.RepeatNum);
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
            var currentPlayback2 = await SpotifyTracker.Spotify.Player.GetCurrentPlayback();
            SpotifyTracker.SendInfo(currentPlayback2);
        }

        protected override void OnOpen()
        {
            Console.WriteLine("New connection opened, list of sessions:");
            Console.WriteLine($"    {string.Join(Environment.NewLine + "    ", Sessions.ActiveIDs)}");
        }
    }
}