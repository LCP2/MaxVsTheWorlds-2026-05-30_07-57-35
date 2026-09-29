namespace MaxWorlds.Weapons
{
    /// <summary>
    /// Which <c>rig_board*.json</c> resource backs THE RIG for a given world (MV-689, MV-1017) — the
    /// <see cref="MaxWorlds.Arena.WorldLibrary"/> counterpart for THE RIG's own data file. World 1
    /// plays the RCDA/Water Balloon board (<see cref="World1ResourcePath"/>); World 2 plays the
    /// LPPE/Shoulder Rack board (<see cref="World2ResourcePath"/>) once the Weapon Core morph has run;
    /// World 3 plays its own UNDERTOW/Shoulder Rack board (<see cref="World3ResourcePath"/>) — MV-1017
    /// gave World 3 its own file rather than reusing World 2's, since UNDERTOW doesn't read World 2's
    /// LPPE-only tracks (p_rof/p_frk/p_cap). <see cref="RigBoard"/> (model) and
    /// <see cref="MaxWorlds.UI.RigBoardLayout"/> (UI) are the only two readers of <c>rig_board*.json</c>
    /// and both resolve their active resource through this class rather than hard-coding a path.
    /// </summary>
    public static class RigBoardLibrary
    {
        public const string World1ResourcePath = "UI/rig_board";
        public const string World2ResourcePath = "UI/rig_board.world2";
        public const string World3ResourcePath = "UI/rig_board.world3";

        /// <summary>The <c>Resources</c> path for <paramref name="worldIndex"/>'s own board — a
        /// per-index lookup table, not a <c>&gt;=</c> catch-all (MV-1017: World 3 needs its own entry
        /// now that it no longer shares World 2's file).</summary>
        private static readonly string[] s_pathsByWorldIndex =
        {
            World1ResourcePath,
            World2ResourcePath,
            World3ResourcePath,
        };

        public static string ForWorld(int worldIndex) => s_pathsByWorldIndex[worldIndex];
    }
}
