using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NeosSpotifyStatus;

internal class Program
{
    private static void Main()
    {
        Console.Title = "Neos Spotify Status";

        if (File.Exists(Config.ConfigBackupFile))
        {
            Console.WriteLine("There's an existing config_old.ini, stopping to prevent overwriting it.");
            Console.WriteLine("Delete it once you have fixed the config.ini.");
            Console.WriteLine("Make sure to enter your Spotify Application's Client ID and Secret into the config.ini.");
            Console.WriteLine("Make sure 'http://localhost:5000/callback' is set as your Spotify Application's redirect-URI!");
            Console.ReadLine();
            return;
        }

        if (!Config.Loaded)
        {
            Console.WriteLine("The config.ini was missing completely or had missing entries; generated default config.");
            Console.WriteLine("Previous config.ini was moved to config_old.ini, if it existed.");
            Console.WriteLine("Make sure to enter your Spotify Application's Client ID and Secret.");
            Console.WriteLine("Make sure 'http://localhost:5000/callback' is set as your Spotify Application's redirect-URI!");
            Console.ReadLine();
            return;
        }

        SpotifyTracker.Start();

        Console.ReadLine();
    }
}