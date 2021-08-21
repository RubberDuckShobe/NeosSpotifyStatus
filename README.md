# NeosSpotifyStatus
Shows Spotify status inside Neos using WebSocket. Also allows for controlling playback (Spotify Premium only)

## Server Setup
1. Get the latest release of the server [here](https://github.com/RubberDuckShobe/NeosSpotifyStatus/releases) and extract it somewhere.
2. Create a Spotify application on the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard/applications).
3. Go to the settings of your Spotify application and add ``http://localhost:5000/callback`` as a Redirect URI.
4. Put your application's Client ID and Client Secret into the config.ini file of the server program.
5. Run the program.
6. Sign in with your Spotify account in the browser window that opened and grant the application access.

## Neos Setup
To actually be able to use this, you will need to get the item from my public folder.
1. Copy this link: ``neosrec:///U-m1nt-/R-39e82928-a499-445a-be77-ea44720867ef``
2. Paste it into Neos by pressing Ctrl+V or by opening your Dash menu and pressing the "Paste content from clipboard" button.
3. Grab the item that just spawned, open your context menu and save it. **(Note: You may need to return to the root of your own inventory to save the folder.)**
4. Open your inventory and enter the folder.
5. Spawn the item.
6. While the server program is running, click on the button that says "Connect to WebSocket".
7. Wait for a bit. This shouldn't take too long, though.
