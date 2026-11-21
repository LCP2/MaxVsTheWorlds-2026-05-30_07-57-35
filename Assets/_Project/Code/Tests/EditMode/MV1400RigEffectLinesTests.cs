using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1400 - THE RIG's detail panel effect line ("Damage per disc  12 → 15"). Sole guard on the
    /// effect line resolving from the gameplay accessors; do not cull. Resolved text only (Tier 2): every
    /// node on every world board is walked at every level, and its line must equal the effect name plus the
    /// value the gameplay accessor returns at that level and the next. Ricochet and the wallet are read off
    /// the live gameplay objects; the rest through the level-parametrised accessors the consumers call.
    /// </summary>
    public sealed class MV1400RigEffectLinesTests
    {
        private static readonly MethodInfo RicochetAwake =
            typeof(Ricochet).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _go;

        [SetUp]
        public void SetUp() { WeaponSystemState.Reset(); RigState.Reset(); PickupWallet.Reset(); }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            WeaponSystemState.Reset(); RigState.Reset(); PickupWallet.Reset();
            RigBoard.UseWorld(0);
            RigBoardLayout.UseWorld(0);
            RigBoardLayout.ResetForTests();
        }

        private static void Select(int worldIndex)
        {
            WorldDefinition row = WorldCatalog.Get(worldIndex);
            RigBoard.UseWorld(worldIndex);
            RigBoardLayout.UseWorld(worldIndex);
            WeaponSystemState.ActivePrimary = row.PrimaryWeapon;
            WeaponSystemState.SecondaryKind = row.SecondaryWeapon;
        }

        private static void SetLevel(RigAbilityLayout node, int level) =>
            RigState.RestoreSnapshot(new Dictionary<string, int> { { node.Id, level } }, new[] { node.Category });

        [Test]
        public void EffectLines_ReadTheGameplayAccessorsOnEveryBoard()
        {
            // AC1: RICOCHET POWER on the City board.
            int city = WorldCatalog.IndexOf(WorldIds.City);
            Select(city);
            RigAbilityLayout power = null;
            foreach (var n in RigBoardLayout.PhoneAbilities) if (n.Id == "p_dmg") power = n;
            Assert.IsNotNull(power, "City board has p_dmg");
            SetLevel(power, 1);
            Assert.AreEqual($"Damage per disc  {Ricochet.PowerDamageByLevel[0]:0} → {Ricochet.PowerDamageByLevel[1]:0}",
                WeaponsScreen.DetailEffectLine(power));
            Assert.AreEqual("Damage per disc  12 → 15", WeaponsScreen.DetailEffectLine(power), "AC1 wording");
            SetLevel(power, 2);
            Assert.AreEqual("Damage per disc  15 → 18", WeaponsScreen.DetailEffectLine(power), "AC1 after buying");

            _go = new GameObject("MV1400 Ricochet");
            Ricochet disc = _go.AddComponent<Ricochet>();
            RicochetAwake.Invoke(disc, null);

            // AC2: every node on every board.
            var problems = new StringBuilder();
            int withRead = 0;
            for (int w = 0; w < WorldCatalog.Count; w++)
            {
                Select(w);
                foreach (var node in RigBoardLayout.PhoneAbilities)
                {
                    if (string.IsNullOrEmpty(node.Effect) || node.Effect.Length > 22)
                        problems.AppendLine($"w{w} {node.Id}: effect name missing or over 22 chars ('{node.Effect}')");
                    int max = RigBoard.MaxLevel(node.Id);
                    for (int lvl = 0; lvl <= max; lvl++)
                    {
                        SetLevel(node, lvl);
                        string line = WeaponsScreen.DetailEffectLine(node);
                        if (!RigEffectLines.TryValue(WeaponSystemState.ActivePrimary, WeaponSystemState.SecondaryKind,
                                node.Id, lvl, out float cur, out string unit))
                        {
                            if (line != "") problems.AppendLine($"w{w} {node.Id} L{lvl}: no gameplay read but line '{line}'");
                            continue;
                        }
                        withRead++;
                        string expect = lvl >= max
                            ? $"{node.Effect}  {RigEffectLines.FormatValue(cur, unit)} MAX"
                            : null;
                        if (expect == null)
                        {
                            RigEffectLines.TryValue(WeaponSystemState.ActivePrimary, WeaponSystemState.SecondaryKind,
                                node.Id, lvl + 1, out float next, out _);
                            expect = $"{node.Effect}  {RigEffectLines.FormatValue(cur, unit)} → {RigEffectLines.FormatValue(next, unit)}";
                        }
                        if (line != expect) problems.AppendLine($"w{w} {node.Id} L{lvl}: '{line}' != '{expect}'");

                        // Live gameplay cross-checks at this level.
                        if (WeaponSystemState.ActivePrimary == WeaponCatalog.PrimaryKind.Ricochet && lvl >= 1)
                        {
                            if (node.Id == "p_dmg" && !Mathf.Approximately(cur, disc.EffectiveDamagePerDisc))
                                problems.AppendLine($"w{w} p_dmg L{lvl}: {cur} vs live {disc.EffectiveDamagePerDisc}");
                            if (node.Id == "p_rof" && !Mathf.Approximately(cur, disc.DiscsPerSecond))
                                problems.AppendLine($"w{w} p_rof L{lvl}: {cur} vs live {disc.DiscsPerSecond}");
                        }
                        if (node.Id == "e_cel" && Mathf.RoundToInt(cur) != PickupWallet.BaseCapacity)
                            problems.AppendLine($"w{w} e_cel L{lvl}: {cur} vs live wallet {PickupWallet.BaseCapacity}");
                        if (node.Id == "u_cst" && Mathf.RoundToInt(cur) != PlayerAbilities.SentinelCost)
                            problems.AppendLine($"w{w} u_cst L{lvl}: {cur} vs live {PlayerAbilities.SentinelCost}");
                    }
                }
            }
            Assert.Greater(withRead, 100, "most nodes resolve a gameplay read");
            Assert.AreEqual("", problems.ToString(), problems.ToString());
        }
    }
}
