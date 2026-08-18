namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Caps how many Rusher/Blinker-kind robots may be mid-attack (Telegraph or Lunge) at once
    /// (MV-428) — the readability fix's Change 2. One telegraphed dash is a fair, dodgeable tell;
    /// six at once from six angles is noise, and the tell is invisible inside a crowd. A robot that
    /// can't get a token keeps closing and pressuring at its normal move speed instead of
    /// committing, and takes a token the instant one frees.
    ///
    /// Field-wide rather than literally keyed per area index: MAX vs THE WORLDS only ever has one
    /// arena's robots actively fighting at a time (the previous arena is cleared behind Max on death
    /// — MV-427 — and later arenas haven't spawned yet), so a field-wide cap and a per-area cap are
    /// the same cap in practice, without a robot needing to carry an area index it doesn't otherwise
    /// track anywhere else.
    ///
    /// Static and instance-free on purpose, same idiom as <see cref="RobotEnemy.Active"/>'s registry
    /// — acquire the instant a robot commits (Chase → Telegraph), release the instant it stops
    /// committing (→ Recover, or death), so the pool can never leak a slot to a robot that got
    /// destroyed mid-lunge.
    /// </summary>
    public static class LungeTokenPool
    {
        private static int _held;

        /// <summary>Robots currently holding a token (mid-Telegraph or mid-Lunge).</summary>
        public static int Held => _held;

        /// <summary>Takes a token if one is free under <paramref name="cap"/>. Returns false, with no
        /// side effect, if the pool is already full.</summary>
        public static bool TryAcquire(int cap)
        {
            if (_held >= cap) return false;
            _held++;
            return true;
        }

        /// <summary>Hands a token back. Floors at zero so a caller that never actually held one (it
        /// died before ever acquiring) can't drive the count negative.</summary>
        public static void Release()
        {
            if (_held > 0) _held--;
        }

        /// <summary>Empties the pool. Called alongside <see cref="RobotEnemy.ResetRegistry"/> whenever
        /// a level starts building (or a test tears down), so a stale count never survives into the
        /// next run.</summary>
        public static void Reset() => _held = 0;
    }
}
