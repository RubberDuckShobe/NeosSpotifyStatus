using System;
using WebSocketSharp;

namespace NeosSpotifyStatus.TestClient
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var ws = new WebSocket("ws://localhost:1011/neos-spotify-bridge");
            ws.OnMessage += ws_OnMessage;
            ws.Connect();

            Console.ReadLine();
        }

        private static void ws_OnMessage(object sender, MessageEventArgs e)
        {
            Console.WriteLine(e.Data);
        }
    }
}