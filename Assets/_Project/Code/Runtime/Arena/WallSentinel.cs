using UnityEngine;
using MaxWorlds.UI;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// The Wall (Blocker) sentinel (MV-362): a solid barrier with HP that closes a chokepoint.
    /// "Blocks robot movement and blocks shots — theirs and Max's" needs no bespoke collision or
    /// line-of-sight code: putting its body on the <see cref="CoverLayer"/> is the exact mechanism
    /// every other sight-blocking prop in the yard already uses (the Water Blaster's own
    /// <c>LineOfSight.Clear</c> check and a robot's <c>LineOfSight.Between</c> both already read that
    /// layer), and a solid, non-trigger, non-CharacterController collider is exactly what
    /// <c>RobotEnemy.OnControllerColliderHit</c> already treats as a wall to route around
    /// (<c>ObstacleSteering</c>) — the same trick <see cref="MaxWorlds.Weapons.ForceFieldBubble"/>
    /// already relies on to stop robot bodies.
    ///
    /// Size (comment 11628 delegated the exact number to whoever built this): 3 m wide, matching a
    /// normal area-gate/passage (<c>world1_config.json</c> authors every ordinary gate at
    /// <c>width: 3</c>) — wide enough to close ONE route through a room without sealing it, since the
    /// game's own narrowest-permitted free channel (<see cref="BackyardCover.MinFreeChannel"/>) is
    /// 6 m, twice this wall's width. 1.8 m tall — the same height as the tallest cover prop already in
    /// the yard (the hedge; see <see cref="Sightlines"/>'s own doc comment on why the LOS sample sits
    /// below the shortest cover), so it reliably blocks the sight-line sample every LOS check in the
    /// game already uses. 0.6 m thick — a slab, not a second wall of the house.
    /// </summary>
    public sealed class WallSentinel : Sentinel
    {
        public const float Width = 3f;
        public const float Height = 1.8f;
        public const float Depth = 0.6f;

        private static readonly Color BodyColor = new Color(0.55f, 0.42f, 0.30f); // greybox timber

        public override SentinelKind Kind => SentinelKind.Wall;
        public override string ReadoutName => "WALL";

        /// <summary>Places and builds the wall. <paramref name="rotation"/> is Max's own facing at
        /// deploy time — the wall's local X (width) axis becomes his right vector, so its wide face
        /// spans left-right across the lane he's facing into, and its local Z (depth) faces him,
        /// matching "deployed at Max's position, not aimed at range".</summary>
        public void Init(Vector3 position, Quaternion rotation, float maxHp)
        {
            transform.SetPositionAndRotation(position, rotation);
            InitHealth(maxHp);
            BuildBody();
            WorldHealthBar.Attach(gameObject, this, Height + 0.5f, 1.8f, alwaysShow: true);

            // Physics.autoSyncTransforms is off project-wide (see ForceFieldBubble/GateSolidityTests)
            // — force a sync so a robot's CharacterController.Move on this very frame already sees it.
            Physics.SyncTransforms();
        }

        private void BuildBody()
        {
            var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vis.name = "Body";
            vis.transform.SetParent(transform, worldPositionStays: false);
            vis.transform.localPosition = new Vector3(0f, Height * 0.5f, 0f);
            vis.transform.localScale = new Vector3(Width, Height, Depth);

            var rend = vis.GetComponent<Renderer>();
            if (rend != null)
            {
                var mpb = new MaterialPropertyBlock();
                rend.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", BodyColor);
                rend.SetPropertyBlock(mpb);
            }

            var col = vis.GetComponent<Collider>();
            // Solid — blocks robot AND Max bodies (MV-378 precedent: a structure that exists to
            // physically block bodies must not be a trigger, or a CharacterController passes through).
            if (col != null) col.isTrigger = false;

            // Blocks shots and sight-lines from either side — the same mechanism every other piece of
            // cover in the yard already relies on, no bespoke line-of-sight code needed.
            CoverLayer.Assign(vis);
        }
    }
}
