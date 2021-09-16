using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SpotifyAPI.Web;

namespace NeosSpotifyStatus
{
    internal struct SpotifyAccessToken
    {
        public readonly int ExpiresIn;
        public readonly string Token;

        public SpotifyAccessToken(string token, int expiresIn)
        {
            Token = token;
            ExpiresIn = expiresIn;
        }

        public SpotifyAccessToken(AuthorizationCodeTokenResponse auth)
            : this(auth.AccessToken, auth.ExpiresIn)
        { }

        public SpotifyAccessToken(AuthorizationCodeRefreshResponse refresh)
            : this(refresh.AccessToken, refresh.ExpiresIn)
        { }
    }
}