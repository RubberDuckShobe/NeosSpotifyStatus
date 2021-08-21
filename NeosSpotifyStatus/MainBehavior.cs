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
    class MainBehavior : WebSocketBehavior
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

        protected override void OnMessage(MessageEventArgs e) {
            //I'm not going to simply get the playback data on every message received
            //That would be a lot of unneeded requests
            //and I'm already getting ratelimited
            int commandCode = int.Parse(e.Data[0].ToString());
            string commandData = e.Data.Remove(0, 1);
            Console.WriteLine($"Command {commandCode} received, data: {commandData}");
            try {
                switch (commandCode) {
                    case 0:
                        var playback = Program.spotify.Player.GetCurrentPlayback().Result;
                        if (playback == null) {
                            Console.WriteLine("No playback detected!");
                            break;
                        }
                        if (playback.IsPlaying) {
                            Program.spotify.Player.PausePlayback().Wait();
                        }
                        else {
                            Program.spotify.Player.ResumePlayback().Wait();
                        }
                        break;
                    case 1:
                        Program.spotify.Player.SkipPrevious().Wait();
                        break;
                    case 2:
                        Program.spotify.Player.SkipNext().Wait();
                        break;
                    case 3:
                        Program.SendInfo();
                        break;
                    case 4:
                        Program.repeatNum = int.Parse(commandData);
                        PlayerSetRepeatRequest repeatRequest = new((PlayerSetRepeatRequest.State)Program.repeatNum);
                        Program.spotify.Player.SetRepeat(repeatRequest).Wait();
                        break;
                    case 5:
                        var playback2 = Program.spotify.Player.GetCurrentPlayback().Result;
                        if (playback2 == null) {
                            Console.WriteLine("No playback detected!");
                            break;
                        }
                        bool doShuffle;
                        if (playback2.ShuffleState) {
                            doShuffle = false;
                        }
                        else {
                            doShuffle = true;
                        }
                        PlayerShuffleRequest shuffleRequest = new PlayerShuffleRequest(doShuffle);
                        Program.spotify.Player.SetShuffle(shuffleRequest).Wait();
                        break;
                    case 6:
                        PlayerSeekToRequest seekRequest = new(int.Parse(commandData));
                        Program.spotify.Player.SeekTo(seekRequest).Wait();
                        break;
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"Exception caught: {ex}");
                Program.SendInfo();
            }
        }

        protected override void OnOpen() {
            Console.WriteLine("New connection opened, list of sessions:");
            Console.WriteLine(string.Join("\n", Sessions.ActiveIDs.ToArray()));
        }

        protected override void OnClose(CloseEventArgs e) {
            Console.WriteLine($"A connection was closed! Reason: {e.Reason}, Code: {e.Code}");
        }
    }
}