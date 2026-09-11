namespace MaxWorlds.Weapons
{
    /// <summary>
    /// World 2's Shoulder Rack secondary (MV-694) — its own SECONDARY-family RIG subtree
    /// (<c>s_rkt</c> root, <c>s_sal</c>/<c>s_rld</c> both direct children of it), the same "root cap
    /// plus child stat tracks" shape as <see cref="WaterBalloonTrackKind"/>. Splash reuses
    /// <see cref="WaterBalloonTrackKind.SplashArea"/> (<c>s_spl</c>) directly rather than a fourth
    /// track here — both secondaries are splash weapons off the same shared SECONDARY node, per the
    /// ticket's own "splash 2m (s_spl -&gt; 3.5)".
    /// </summary>
    public enum ShoulderRackTrackKind
    {
        /// <summary>Per-rocket damage. <c>s_rkt</c> is also the rack's own root cap AND the CODE-level
        /// "is the rack usable at all" gate (<see cref="ShoulderRack.Tick"/>).</summary>
        RocketDamage,

        /// <summary>Rockets fired per salvo, 1 at L1 up to 3 at the cap.</summary>
        Salvo,

        /// <summary>Shortens the reload between salvos.</summary>
        Reload,

        /// <summary>MV-768: a rocket splits into 3 bomblets on impact (<c>s_clu</c>, a child of
        /// <c>s_rld</c> on <c>rig_board.world2.json</c>). One level.</summary>
        Cluster,
    }
}
