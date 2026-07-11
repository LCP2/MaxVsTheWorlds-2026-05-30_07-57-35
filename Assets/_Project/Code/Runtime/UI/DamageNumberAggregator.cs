using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Merges damage events into readable numbers (YT-54).
    ///
    /// The problem this solves is specific and severe: the Water Blaster is a sustained volume
    /// weapon that lands a damage tick every 0.1s on EVERY enemy it touches. Spraying a crowd of
    /// 20–30 enemies therefore raises on the order of 200–300 damage events per second, and the
    /// HUD was spawning a floating "4" for every one of them. That isn't feedback, it's a
    /// blizzard — it buries the screen, drowns the numbers that matter, and thrashes the text pool.
    ///
    /// So damage is accumulated per enemy (bucketed by position) over a short window and emitted as
    /// ONE number that counts the total. "4 4 4 4 4 4" becomes "24", which is both quieter and
    /// strictly more informative. A hard cap on numbers per flush protects the worst case.
    ///
    /// Plain C# with an explicit clock, so it is unit-testable with no canvas.
    /// </summary>
    public sealed class DamageNumberAggregator
    {
        public struct Entry
        {
            public Vector3 Position;
            public float Amount;
            public bool Crit;
        }

        private struct Bucket
        {
            public Vector3 Position;
            public float Amount;
            public bool Crit;
            public float OpenedAt;
        }

        /// <summary>World size of a merge cell. Roughly one enemy: big enough that a single enemy's
        /// ticks always land in the same bucket as it walks, small enough that two enemies standing
        /// apart still get their own number.</summary>
        public float CellSize { get; set; } = 1.25f;

        /// <summary>How long damage on one enemy accumulates before it's shown.</summary>
        public float Window { get; set; } = 0.35f;

        /// <summary>Hard ceiling on numbers emitted per flush.</summary>
        public int MaxPerFlush { get; set; } = 8;

        private readonly Dictionary<long, Bucket> _open = new Dictionary<long, Bucket>(64);
        private readonly List<long> _matured = new List<long>(32);

        public int OpenBuckets => _open.Count;

        public void Add(Vector3 position, float amount, bool crit, float now)
        {
            if (amount <= 0f) return;

            long key = KeyFor(position);
            if (_open.TryGetValue(key, out var b))
            {
                b.Amount += amount;
                b.Position = position;          // follow the enemy as it moves
                b.Crit |= crit;                 // one crit in the burst makes the whole number a crit
                _open[key] = b;
            }
            else
            {
                _open[key] = new Bucket
                {
                    Position = position,
                    Amount = amount,
                    Crit = crit,
                    OpenedAt = now,
                };
            }
        }

        /// <summary>Emit every bucket that has been open for longer than <see cref="Window"/>.
        /// Results are appended to <paramref name="results"/>.</summary>
        public void Flush(float now, List<Entry> results)
        {
            _matured.Clear();
            foreach (var kv in _open)
            {
                if (now - kv.Value.OpenedAt >= Window) _matured.Add(kv.Key);
            }
            if (_matured.Count == 0) return;

            // Over the cap, show the biggest hits and drop the rest. Dropping is correct here:
            // the alternative is a screen of numbers nobody can read, and the small ones are the
            // ones a player would never have looked at.
            if (_matured.Count > MaxPerFlush)
            {
                _matured.Sort((a, b) => _open[b].Amount.CompareTo(_open[a].Amount));
            }

            int emitted = 0;
            foreach (var key in _matured)
            {
                var b = _open[key];
                _open.Remove(key);

                if (emitted >= MaxPerFlush) continue;   // still removed, just not drawn
                emitted++;

                results.Add(new Entry { Position = b.Position, Amount = b.Amount, Crit = b.Crit });
            }
        }

        public void Clear() => _open.Clear();

        private long KeyFor(Vector3 p)
        {
            long x = Mathf.RoundToInt(p.x / CellSize);
            long z = Mathf.RoundToInt(p.z / CellSize);
            return (x << 32) ^ (z & 0xFFFFFFFFL);
        }
    }
}
