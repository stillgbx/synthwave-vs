using System;
using System.Collections.Generic;
using System.Linq;

namespace SynthWave
{
    /// <summary>A track of the mixtape.</summary>
    public sealed class Track
    {
        public Track(string title, string artist, TimeSpan length)
        {
            Title = title;
            Artist = artist;
            Length = length;
        }

        public string Title { get; }

        public string Artist { get; }

        public TimeSpan Length { get; }
    }

    public sealed class Mixtape
    {
        private const int MaxTracks = 12;
        private readonly List<Track> tracks = new List<Track>();

        public Mixtape(string name) => Name = name;

        public string Name { get; }

        public TimeSpan Duration => tracks.Aggregate(TimeSpan.Zero, (total, track) => total + track.Length);

        // A tape only holds so many tracks.
        public bool Add(Track track)
        {
            if (track == null || tracks.Count >= MaxTracks)
            {
                return false;
            }

            tracks.Add(track);
            return true;
        }

        public override string ToString() => $"{Name}: {tracks.Count} tracks, {Duration.TotalMinutes:0} min";

        public static void Main()
        {
            var tape = new Mixtape("Neon Nights");
            tape.Add(new Track("Nightcall", "Kavinsky", TimeSpan.FromSeconds(258)));
            tape.Add(new Track("Turbo Killer", "Carpenter Brut", TimeSpan.FromSeconds(228)));
            Console.WriteLine(tape);
        }
    }
}
