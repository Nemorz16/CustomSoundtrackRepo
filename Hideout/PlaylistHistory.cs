using System;
using System.Collections.Generic;
namespace CustomHideoutRadio
{
    public sealed class PlaylistHistory
    {
        private readonly List<int> history = new List<int>();
        private readonly int count;
        private readonly Random random;
        private int cursor;
        public int Current => history[cursor];
        public sealed class Snapshot
        {
            internal int[] Entries;
            internal int Cursor;
        }
        public Snapshot Capture() => new Snapshot { Entries = history.ToArray(), Cursor = cursor };
        public void Restore(Snapshot snapshot)
        {
            history.Clear(); history.AddRange(snapshot.Entries); cursor = snapshot.Cursor;
        }
        public PlaylistHistory(int count, bool shuffle, Random random)
        {
            if (count <= 0) throw new ArgumentException("Playlist is empty.");
            this.count = count;
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            history.Add(shuffle ? random.Next(count) : 0);
        }
        public int Next(bool shuffle)
        {
            if (cursor + 1 < history.Count) return history[++cursor];
            int next = (Current + 1) % count;
            if (shuffle && count > 1)
            {
                next = random.Next(count - 1);
                if (next >= Current) next++;
            }
            history.Add(next); cursor++;
            return Current;
        }
        public int Previous(double seconds)
        {
            if (seconds <= 3 && cursor > 0) cursor--;
            return Current;
        }
    }
}
