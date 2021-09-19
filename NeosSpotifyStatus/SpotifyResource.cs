using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NeosSpotifyStatus
{
    internal class SpotifyResource
    {
        public string Name { get; }
        public string Uri { get; }

        public SpotifyResource(string name, string uri)
        {
            Name = name;
            Uri = uri;
        }

        public override bool Equals(object obj)
        {
            return obj is SpotifyResource other && Uri.Equals(other.Uri);
        }

        public override int GetHashCode()
        {
            return Uri.GetHashCode();
        }

        public override string ToString()
        {
            return Name;
        }
    }
}