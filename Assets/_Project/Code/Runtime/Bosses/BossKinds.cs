using System;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.VFX;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// One table of boss kinds (MV-1145) — replaces the string-equality chain
    /// <see cref="MapRuntime.BuildBoss"/> used to run ("sludgequeen"? "anchorhead"? else Big Bermuda)
    /// with a single lookup from a lower-case kind name to the recipe that builds it and the name its
    /// HUD bar shows. A fourth world's boss is a new row here, not a new branch in <c>MapRuntime</c>.
    /// </summary>
    public static class BossKinds
    {
        public const string BigBermuda = "bigbermuda";
        public const string Anchorhead = "anchorhead";
        public const string Sludgequeen = "sludgequeen";
        public const string Helmsman = "helmsman";

        public sealed class Row
        {
            public readonly string HudName;
            public readonly Func<GameObject, MapEntity, MapData, MonoBehaviour> Build;

            public Row(string hudName, Func<GameObject, MapEntity, MapData, MonoBehaviour> build)
            {
                HudName = hudName;
                Build = build;
            }
        }

        private static readonly Dictionary<string, Row> _rows = new Dictionary<string, Row>
        {
            [BigBermuda] = new Row("BIG BERMUDA", (body, e, map) =>
            {
                var bigBermuda = body.AddComponent<BigBermudaBoss>(); // RequireComponent adds the CharacterController
                bigBermuda.SetWakeArea(map.ZoneAt(e.x, e.z).Footprint);

                // MV-573: every boss gets its own rig, bound to it alone, right here — the old
                // scene-load singleton (BigBermudaRig.Install) built exactly one rig per scene and
                // bailed the moment any rig existed, so only the FIRST boss on a multi-boss map ever
                // grew a body and every other one stood there as the bare greybox cube above.
                BigBermudaRig.CreateFor(bigBermuda);
                return bigBermuda;
            }),

            [Anchorhead] = new Row("ANCHORHEAD", (body, e, map) =>
            {
                // MV-1018: Anchorhead stays a BigBermudaBoss underneath its own companion behaviour
                // (HP bar, BossCensus, victory, the finale orb/door all keep working through it,
                // unchanged) — only the companion behaviour, the generated body and the HUD name differ.
                var bigBermuda = body.AddComponent<BigBermudaBoss>();
                bigBermuda.SetWakeArea(map.ZoneAt(e.x, e.z).Footprint);
                bigBermuda.SetDisplayName("ANCHORHEAD");

                var anchor = body.AddComponent<AnchorheadBoss>();
                AnchorheadRig.CreateFor(bigBermuda, anchor);
                return bigBermuda;
            }),

            [Sludgequeen] = new Row("SLUDGEQUEEN", (body, e, map) =>
            {
                var sludgequeen = body.AddComponent<SludgequeenBoss>(); // RequireComponent adds the CharacterController
                Rect footprint = map.ZoneAt(e.x, e.z).Footprint;
                sludgequeen.SetWakeArea(footprint);
                sludgequeen.SetArenaBounds(footprint);
                SludgequeenRig.CreateFor(sludgequeen);
                return sludgequeen;
            }),

            [Helmsman] = new Row("HELMSMAN", (body, e, map) =>
            {
                // MV-1239: behaviour and placement only — the look (seated-then-torn-free body) is
                // MV-1242's own ticket, so unlike the rows above this builds no rig yet and rides the
                // greybox cube every boss starts as (same "bare fixture with no rig bound" fallback
                // SludgequeenBoss's own cannon-mouth/chute-foot transforms already degrade to).
                var helmsman = body.AddComponent<HelmsmanBoss>(); // RequireComponent adds the CharacterController
                helmsman.SetWakeArea(map.ZoneAt(e.x, e.z).Footprint);
                return helmsman;
            }),
        };

        public static IReadOnlyCollection<string> KnownKinds => _rows.Keys;

        public static bool TryGet(string kind, out Row row) => _rows.TryGetValue(kind, out row);

        /// <summary>Which row builds a boss (MV-1145 rule 3): the authored <paramref name="kind"/> wins
        /// if it is set; otherwise <paramref name="entityId"/> itself, if that id happens to be a
        /// registered kind (keeps "anchorhead" and "sludgequeen" working with no config edit);
        /// otherwise Big Bermuda.</summary>
        public static string Resolve(string kind, string entityId)
        {
            if (!string.IsNullOrEmpty(kind)) return kind;
            if (!string.IsNullOrEmpty(entityId) && _rows.ContainsKey(entityId)) return entityId;
            return BigBermuda;
        }
    }
}
