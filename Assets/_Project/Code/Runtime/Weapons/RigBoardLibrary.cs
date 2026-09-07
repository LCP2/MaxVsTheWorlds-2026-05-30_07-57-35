namespace MaxWorlds.Weapons
{
    /// <summary>
    /// Which <c>rig_board*.json</c> resource backs THE RIG for a given world (MV-689) — the
    /// <see cref="MaxWorlds.Arena.WorldLibrary"/> counterpart for THE RIG's own data file. World 1
    /// plays the RCDA/Water Balloon board (<see cref="World1ResourcePath"/>); World 2 onward plays
    /// the LPPE/Shoulder Rack board (<see cref="World2ResourcePath"/>) once the Weapon Core morph has
    /// run — every world past 1 currently resolves to the same file, there being only two worlds
    /// today. <see cref="RigBoard"/> (model) and <see cref="MaxWorlds.UI.RigBoardLayout"/> (UI) are
    /// the only two readers of <c>rig_board*.json</c> and both resolve their active resource through
    /// this class rather than hard-coding a path.
    /// </summary>
    public static class RigBoardLibrary
    {
        public const string World1ResourcePath = "UI/rig_board";
        public const string World2ResourcePath = "UI/rig_board.world2";

        /// <summary>The <c>Resources</c> path for <paramref name="worldIndex"/>'s board — 0 (World 1)
        /// gets the RCDA/Water Balloon board, anything >= 1 gets the LPPE/Shoulder Rack board.</summary>
        public static string ForWorld(int worldIndex) =>
            worldIndex >= 1 ? World2ResourcePath : World1ResourcePath;
    }
}
