using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;
using SpotifyAPI.Web;

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
         * 5 - Toggle repeat
         * 6 - Seek to position
        */

        protected override void OnClose(CloseEventArgs e)
        {
            Console.WriteLine($"A connection was closed! Reason: {e.Reason}, Code: {e.Code}");
        }

        protected override void OnMessage(MessageEventArgs e)
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
                        var playback = SpotifyTracker.Spotify.Player.GetCurrentPlayback().Result;
                        if (playback == null)
                        {
                            Console.WriteLine("No playback detected!");
                            break;
                        }
                        if (playback.IsPlaying)
                        {
                            SpotifyTracker.Spotify.Player.PausePlayback().Wait();
                        }
                        else
                        {
                            SpotifyTracker.Spotify.Player.ResumePlayback().Wait();
                        }
                        break;

                    case 1:
                        SpotifyTracker.Spotify.Player.SkipPrevious().Wait();
                        break;

                    case 2:
                        SpotifyTracker.Spotify.Player.SkipNext().Wait();
                        break;

                    case 3:
                        var currentPlayback = SpotifyTracker.Spotify.Player.GetCurrentPlayback().Result;
                        SpotifyTracker.SendInfo(currentPlayback);
                        break;

                    case 4:
                        SpotifyTracker.RepeatNum = int.Parse(commandData);
                        PlayerSetRepeatRequest repeatRequest = new((PlayerSetRepeatRequest.State)SpotifyTracker.RepeatNum);
                        SpotifyTracker.Spotify.Player.SetRepeat(repeatRequest).Wait();
                        break;

                    case 5:
                        var playback2 = SpotifyTracker.Spotify.Player.GetCurrentPlayback().Result;
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
                        SpotifyTracker.Spotify.Player.SetShuffle(shuffleRequest).Wait();
                        break;

                    case 6:
                        PlayerSeekToRequest seekRequest = new(int.Parse(commandData));
                        SpotifyTracker.Spotify.Player.SeekTo(seekRequest).Wait();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception caught: {ex}");
                var currentPlayback = SpotifyTracker.Spotify.Player.GetCurrentPlayback().Result;
                SpotifyTracker.SendInfo(currentPlayback);
            }
        }

        protected override void OnOpen()
        {
            Console.WriteLine("New connection opened, list of sessions:");
            Console.WriteLine($"    {string.Join(Environment.NewLine + "    ", Sessions.ActiveIDs)}");
        }
    }
}