// MV-851: ported literally from function buildNew() in
// C:\Dev\MaxVsTheWorlds-Images\max-lookdev.html ("New · title reveal", Lee's approved prototype) —
// the title-reveal redesign that replaces the MV-669 hoodie/goggles body below. Change the
// prototype and re-port by hand; there is no longer an auto-emit step for this body.
using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>Max's materials, for the MV-851 title-reveal body: dark-blue tunic and shorts, bare
    /// arms and shins, dark boots and gloves, a static hair cap. His gadget glow is still the only
    /// COOL light in the whole cast, against every robot's warm eye — <see cref="Metal"/> and the
    /// gadget's own runtime-tinted lenses (see <see cref="MaxRig"/>) are the only cool things on him.</summary>
    public readonly struct MaxPalette
    {
        public readonly Material Skin, Hair, Tunic, TunicDark, Belt, Boot, Sole, Glove, Eye, Pupil, Dark, Metal;

        public MaxPalette(Material skin, Material hair, Material tunic, Material tunicDark, Material belt,
                          Material boot, Material sole, Material glove, Material eye, Material pupil,
                          Material dark, Material metal)
        {
            Skin = skin; Hair = hair; Tunic = tunic; TunicDark = tunicDark; Belt = belt;
            Boot = boot; Sole = sole; Glove = glove; Eye = eye; Pupil = pupil;
            Dark = dark; Metal = metal;
        }
    }

    /// <summary>What a built body hands back: the gadget glow the rig tints, and the hip pivots the run
    /// cycle drives (MV-474) — the same Eyes/Wheels split <see cref="RobotBodies.Body"/> already uses,
    /// so a director never has to hunt geometry down by name.
    ///
    /// MV-702 adds the LPPE/Shoulder Rack integration points: <see cref="RcdaGadget"/>/
    /// <see cref="LppeGadget"/> are the two gadget submeshes <c>MaxRig</c> toggles on
    /// <c>WeaponSystemState.ActivePrimary</c> (both built once, never rebuilt — a SetActive flip is
    /// cheaper than tearing down and re-emitting the fused body mesh on every primary swap), and
    /// <see cref="RackMount"/>/<see cref="RackTubeGlow"/> are the Shoulder Rack's mount, migrated here
    /// from its old placeholder home directly on <c>ShoulderRack</c> (see that class's history).</summary>
    public readonly struct MaxBodyResult
    {
        public readonly MeshRenderer[] GadgetGlow;
        public readonly Transform[] Hips;

        /// <summary>MV-851: one knee pivot per leg, child of the matching <see cref="Hips"/> entry —
        /// the joint the new bare-shin/boot geometry hangs off, so <c>MaxRig.TickRun</c> has a second
        /// hinge to flex on top of the hip's own thigh swing (the old body had no knee at all).</summary>
        public readonly Transform[] Knees;

        /// <summary>The RCDA's own gadget submesh — active by default (the RCDA is Max's run-start
        /// primary).</summary>
        public readonly GameObject RcdaGadget;

        /// <summary>The LPPE's gadget submesh — inactive by default, shown once
        /// <c>WeaponSystemState.ActivePrimary</c> reads <c>Lppe</c> (MV-689's World 2 morph).</summary>
        public readonly GameObject LppeGadget;

        /// <summary>The LPPE's own cyan-white lens — tinted separately from the RCDA's water-cyan tank
        /// glow so the two weapons don't share a light colour.</summary>
        public readonly MeshRenderer[] LppeGlow;

        /// <summary>The Shoulder Rack's 3-tube mount — inactive by default (unbought), shown once
        /// <c>ShoulderRack.IsBought</c> is true.</summary>
        public readonly GameObject RackMount;

        /// <summary>The three tube-tip lenses, tinted by <c>MaxRig</c> from dim (just fired) to bright
        /// (fully reloaded) as the rack reloads.</summary>
        public readonly MeshRenderer[] RackTubeGlow;

        /// <summary>MV-717: the moving parts MV-451's fused mesh dropped. All five (six, counting
        /// <see cref="Head"/>) are children of whatever <c>root</c> was built under, and <c>MaxRig</c>
        /// re-parents each one under its own torso pivot — see that class's <c>Build</c> for why.
        ///
        /// <see cref="Gun"/> wraps the whole gadget assembly (both submeshes and both hand grips) at
        /// its rest pose, so <c>TickGadget</c> can move and rotate one transform and carry everything
        /// welded to it along for free. <see cref="ArmL"/>/<see cref="ArmR"/> are the sleeve meshes
        /// <c>PoseArm</c> stretches every frame — a single tapered beam each, not the old static
        /// sleeve-plus-forearm pair, because a stretch target needs one transform to own the position,
        /// rotation AND scale it is given. <see cref="HandL"/>/<see cref="HandR"/> are the grip points
        /// <c>PoseArm</c> reaches for, parented under <see cref="Gun"/> so the hands cannot come off it.
        /// <see cref="Head"/> wraps the skull/hair/goggle geometry so <c>MaxRig</c> can yaw it
        /// independently for the head-lag cue.</summary>
        public readonly Transform Gun, ArmL, ArmR, HandL, HandR, Head;

        public MaxBodyResult(MeshRenderer[] gadgetGlow, Transform[] hips, Transform[] knees, GameObject rcdaGadget,
                             GameObject lppeGadget, MeshRenderer[] lppeGlow, GameObject rackMount,
                             MeshRenderer[] rackTubeGlow, Transform gun, Transform armL, Transform armR,
                             Transform handL, Transform handR, Transform head)
        {
            GadgetGlow = gadgetGlow;
            Hips = hips;
            Knees = knees;
            RcdaGadget = rcdaGadget;
            LppeGadget = lppeGadget;
            LppeGlow = lppeGlow;
            RackMount = rackMount;
            RackTubeGlow = rackTubeGlow;
            Gun = gun;
            ArmL = armL;
            ArmR = armR;
            HandL = handL;
            HandR = handR;
            Head = head;
        }
    }

    /// <summary>
    /// Max's body, in metres, feet at y = 0 and +Z where he faces.
    ///
    /// MV-851 replaces the MV-669 hoodie/goggles body with the title-reveal look: a dark-blue
    /// sleeveless tunic and shorts, bare arms and shins, slim dark boots, dark gloves, visible eyes
    /// and knees. The legs and torso/arms/head sections below are ported literally from
    /// <c>buildNew()</c> in the approved prototype (<c>max-lookdev.html</c>) — same builders
    /// (<see cref="CharacterMeshes.Lathe"/>/<see cref="CharacterMeshes.Prism"/>/
    /// <see cref="CharacterMeshes.Beam"/>/<see cref="CharacterMeshes.Sphere"/>, identical maths,
    /// Unity's left-handed coordinates against the prototype's three.js right-handed one). Two
    /// integration points the prototype doesn't cover are carried over unchanged from the pre-MV-851
    /// body, per the ticket's own "Keep" list:
    ///
    ///   * THE GADGET (RCDA/LPPE, Shoulder Rack mount) is untouched — still off the midline, still
    ///     boxy, so it still reads as a tool and not a person.
    ///   * THE HIP AND KNEE PIVOTS are not literal <c>buildNew()</c> geometry either — MV-851 adds a
    ///     real knee joint (the old body had none), and both pivots are still hinges <c>TickRun</c>
    ///     swings/flexes (MV-474, MV-851) rather than drawn parts.
    ///
    /// Hair is a static cap + back-of-head mass only (buildNew()'s flowing locks are the follow-up
    /// ticket, MV-854) — see the head section below.
    /// </summary>
    public static class MaxBody
    {
        /// <summary>Build Max under <paramref name="root"/> (feet at y = 0 in <paramref name="root"/>'s
        /// space). <paramref name="hipY"/> is <c>MaxRig.HipY</c> — the waist height the two returned hip
        /// pivots sit at, so <c>TickRun</c>'s existing stride rotation (MV-474) has something to turn
        /// again. Returns the gadget glow, the hips and the knees so the rig can drive all three.</summary>
        public static MaxBodyResult Build(Transform root, in MaxPalette p, float hipY)
        {
            var gadgetGlow = new List<MeshRenderer>(2);

            // ---- legs (buildNew(), literal) — a real knee joint, which the pre-MV-851 body never had:
            // the hip pivot swings the whole leg (thigh + shorts hem), and a knee pivot underneath it
            // flexes the shin/boot/sole on top of that, so TickRun has two hinges to drive instead of
            // one rigid pole from the hip. kneeY is buildNew()'s own local constant, not derived from
            // hipY — it is the knee's ABSOLUTE height in feet-space, same convention hipY already uses.
            const float kneeY = 0.44f;
            var hipL = Hip(root, "HipL", new Vector3(-0.11f, hipY, 0f));
            var hipR = Hip(root, "HipR", new Vector3(0.11f, hipY, 0f));
            var hips = new[] { hipL, hipR };

            var kneeL = Hip(hipL, "KneeL", new Vector3(0f, kneeY - hipY, 0f));
            var kneeR = Hip(hipR, "KneeR", new Vector3(0f, kneeY - hipY, 0f));
            var knees = new[] { kneeL, kneeR };

            foreach (var (hip, knee) in new[] { (hipL, kneeL), (hipR, kneeR) })
            {
                Add(hip, CharacterMeshes.Beam(hipY - kneeY + 0.02f, 0.11f, 0.10f, 8), p.Tunic, new Vector3(0f, -(hipY - kneeY) / 2f + 0.01f, 0f), Quaternion.identity, Vector3.one);
                Add(hip, CharacterMeshes.Prism(8, 0.125f, 0.118f, 0.07f, 0.2f, 0f), p.TunicDark, new Vector3(0f, kneeY - hipY + 0.06f, 0f), Quaternion.identity, Vector3.one);
                Add(knee, CharacterMeshes.Beam(kneeY - 0.28f, 0.062f, 0.05f, 8), p.Skin, new Vector3(0f, -(kneeY - 0.28f) / 2f, 0f), Quaternion.identity, Vector3.one);
                Add(knee, CharacterMeshes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.122f, 0.012f), new Vector2(0.132f, 0.06f), new Vector2(0.118f, 0.09f), new Vector2(0f, 0.095f) }, 16), p.Sole, new Vector3(0f, -kneeY, 0.035f), Quaternion.identity, new Vector3(1f, 1f, 1.2f));
                Add(knee, CharacterMeshes.Lathe(new[] { new Vector2(0f, 0.07f), new Vector2(0.112f, 0.085f), new Vector2(0.114f, 0.14f), new Vector2(0.085f, 0.2f), new Vector2(0.068f, 0.29f), new Vector2(0.072f, 0.305f), new Vector2(0f, 0.31f) }, 16), p.Boot, new Vector3(0f, -kneeY, 0.012f), Quaternion.identity, new Vector3(1f, 1f, 1.08f));
            }

            // ---- tunic, collar, belt, neck (buildNew(), literal) — no hood, no jacket: one sleeveless
            // tunic thick at the shoulders, a dark-blue collar and hem, a brown belt with one pouch.
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0f, 0.66f), new Vector2(0.215f, 0.66f), new Vector2(0.22f, 0.70f), new Vector2(0.2f, 0.84f), new Vector2(0.185f, 0.98f), new Vector2(0.205f, 1.16f), new Vector2(0.225f, 1.30f), new Vector2(0.205f, 1.41f), new Vector2(0.14f, 1.47f), new Vector2(0f, 1.48f) }, 24), p.Tunic, Vector3.zero, Quaternion.identity, Vector3.one);
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.15f, 1.40f), new Vector2(0.205f, 1.40f), new Vector2(0.21f, 1.45f), new Vector2(0.14f, 1.50f), new Vector2(0.1f, 1.5f) }, 20), p.TunicDark, Vector3.zero, Quaternion.identity, Vector3.one);
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0f, 0.86f), new Vector2(0.2f, 0.865f), new Vector2(0.205f, 0.94f), new Vector2(0f, 0.945f) }, 24), p.Belt, Vector3.zero, Quaternion.identity, Vector3.one);
            Add(root, CharacterMeshes.Prism(4, 0.06f, 0.055f, 0.12f, 0.22f, 0f), p.Belt, new Vector3(0.15f, 0.84f, 0.13f), Quaternion.identity, new Vector3(1f, 1f, 0.7f));
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0f, 1.44f), new Vector2(0.07f, 1.45f), new Vector2(0.075f, 1.5f), new Vector2(0f, 1.51f) }, 12), p.Skin, Vector3.zero, Quaternion.identity, Vector3.one);

            // ---- head, hair (buildNew(), literal) — wrapped in a "Head" pivot so MaxRig can yaw it
            // independently for the head-lag cue (MV-717). The pivot sits at root's own origin (identity
            // local transform), so every child keeps the exact authored offset below; a pure yaw of the
            // pivot only ever moves X/Z, and every child's X/Z is already relative to the midline (x=0),
            // so the pivot's own height is irrelevant to the result.
            //
            // The goggles, the goggle strap and the two hair bunches are GONE (MV-851 AC2) — visible
            // eyes (white sphere + dark pupil, canted up toward the camera) and heavy brows take their
            // place, and the hair is a low cap + back-of-head mass rather than a lathe-plus-two-bunches.
            // buildNew()'s flowing locks are deliberately not ported here — hair movement is MV-854.
            var headGroup = new GameObject("Head");
            headGroup.transform.SetParent(root, worldPositionStays: false);
            var head = headGroup.transform;
            Add(head, CharacterMeshes.Lathe(new[] { new Vector2(0f, 1.474f), new Vector2(0.145f, 1.504f), new Vector2(0.195f, 1.584f), new Vector2(0.205f, 1.704f), new Vector2(0.185f, 1.794f), new Vector2(0f, 1.824f) }, 20), p.Skin, Vector3.zero, Quaternion.identity, Vector3.one);
            foreach (float sx in new[] { -1f, 1f })
            {
                Add(head, CharacterMeshes.Sphere(14), p.Eye, new Vector3(sx * 0.083f, 1.66f, 0.17f), Quaternion.Euler(-32f, 0f, 0f), new Vector3(0.092f, 0.104f, 0.055f));
                Add(head, CharacterMeshes.Sphere(12), p.Pupil, new Vector3(sx * 0.08f, 1.655f, 0.196f), Quaternion.Euler(-32f, 0f, 0f), new Vector3(0.05f, 0.062f, 0.022f));
                Add(head, CharacterMeshes.Prism(4, 0.022f, 0.022f, 0.11f, 0.2f, 0f), p.Hair, new Vector3(sx * 0.088f, 1.73f, 0.19f), Quaternion.Euler(-20f, 0f, 90f + sx * 16f), new Vector3(1f, 1f, 0.8f));
            }
            Add(head, CharacterMeshes.Lathe(new[] { new Vector2(0f, 1.735f), new Vector2(0.17f, 1.74f), new Vector2(0.222f, 1.775f), new Vector2(0.232f, 1.83f), new Vector2(0.21f, 1.89f), new Vector2(0.13f, 1.925f), new Vector2(0f, 1.93f) }, 22), p.Hair, new Vector3(0f, 0f, -0.035f), Quaternion.identity, Vector3.one);
            Add(head, CharacterMeshes.Sphere(16), p.Hair, new Vector3(0f, 1.72f, -0.06f), Quaternion.identity, new Vector3(0.43f, 0.4f, 0.36f));

            // ---- arms (buildNew(), literal) — bare skin, no sleeves. The dynamic stretch target
            // PoseArm needs (MV-717) is still a single tapered beam per arm — see that section's own
            // doc below — coloured Skin now instead of the retired HoodieShade; the shoulder cap, cuff
            // and glove buildNew() draws as static parts riding a fixed shoulder pivot don't fit that
            // dynamic rig (there is no persistent shoulder transform to hang them on — see MaxRig.
            // PoseArms), so the cuff/glove are built as static decoration on the gun's own hand grips
            // instead (see the gadget section below), which already ride at the hand end for free. The
            // shoulder cap is left out — a minor decorative loss the human staging check (AC5) can flag
            // if it reads as a gap.
            var armL = Add(root, CharacterMeshes.Beam(1f, 0.5f, 0.4f, 6), p.Skin, new Vector3(-0.25f, 1.36f, 0.02f), Quaternion.identity, new Vector3(0.096f, 0.44f, 0.096f));
            var armR = Add(root, CharacterMeshes.Beam(1f, 0.5f, 0.4f, 6), p.Skin, new Vector3(0.25f, 1.36f, 0.02f), Quaternion.identity, new Vector3(0.096f, 0.44f, 0.096f));

            // ---- the RCDA gadget (not in the approved block — ported from the pre-MV-669 body) -----
            //
            // Revision 2's re-emitted block moved the right glove to (0.325, 0.769, 0.03) — the block's
            // header states this explicitly. That is a further (+0.0171, -0.0315, +0.0016) from
            // revision 1's glove (0.3079, 0.8005, 0.0284), which the gadget below was already re-seated
            // to (MV-669's first approved-geometry commit). This section is that same revision-1
            // geometry translated once more by that delta so it stays welded to the new glove; rotations
            // and scales are still untouched. Combined with the revision-1 shift, the gadget has now
            // moved a total of (+0.0665, -0.2835, -0.04) from the true pre-MV-669 hand (0.2585, 1.0525,
            // 0.07) — down to hip height, matching the GDD's "holds the gadget two-handed at the hip".
            //
            // MV-702: parented under its own container so MaxRig can SetActive it off once the LPPE
            // morph (MV-689) flips WeaponSystemState.ActivePrimary — both gadgets are built once, at
            // Awake, and swapped by visibility rather than torn down and re-emitted.
            //
            // MV-717: that container is itself wrapped in "Gun" — sitting at root's own origin
            // (identity local transform), so every child below keeps the exact authored offset it
            // always had. MaxRig re-parents Gun onto a pivot placed at GunHipPos/GunHipRot (both
            // already expressed in the same torso space this whole gadget was designed for) using
            // worldPositionStays — Unity solves the "what local offset keeps this rendering exactly
            // where it already is" arithmetic, so nothing here has to.
            var gunAssembly = new GameObject("Gun");
            gunAssembly.transform.SetParent(root, worldPositionStays: false);
            var rcdaRoot = new GameObject("GadgetRcda");
            rcdaRoot.transform.SetParent(gunAssembly.transform, worldPositionStays: false);
            Add(rcdaRoot.transform, CharacterMeshes.Prism(4, 0.055f, 0.05f, 0.3f, 0.12f, 0f), p.Metal, new Vector3(0.2715f, 0.7565f, 0.11f), Quaternion.Euler(82f, -15f, 0f), Vector3.one);
            Add(rcdaRoot.transform, CharacterMeshes.Prism(4, 0.042f, 0.038f, 0.1f, 0.2f, 0f), p.Dark, new Vector3(0.2715f, 0.7515f, 0.3f), Quaternion.Euler(82f, -15f, 0f), Vector3.one);
            Add(rcdaRoot.transform, CharacterMeshes.Prism(6, 0.03f, 0.026f, 0.06f, 0.25f, 0f), p.Metal, new Vector3(0.2715f, 0.7485f, 0.365f), Quaternion.Euler(82f, -15f, 0f), Vector3.one);
            gadgetGlow.Add(Lens(rcdaRoot.transform, CharacterMeshes.Lathe(new[] { new Vector2(0.068f, 0f), new Vector2(0.082f, 0.035f), new Vector2(0.082f, 0.15f), new Vector2(0.064f, 0.19f) }, 16), new Vector3(0.2665f, 0.8415f, 0.05f), Quaternion.Euler(78f, -15f, 0f), Vector3.one));
            Add(rcdaRoot.transform, CharacterMeshes.Lathe(new[] { new Vector2(0.04f, 0f), new Vector2(0.046f, 0.015f), new Vector2(0.038f, 0.03f) }, 12), p.Dark, new Vector3(0.2665f, 0.8415f, 0.23f), Quaternion.Euler(78f, -15f, 0f), Vector3.one);
            Add(rcdaRoot.transform, CharacterMeshes.Prism(4, 0.04f, 0.034f, 0.13f, 0.2f, 0f), p.Dark, new Vector3(0.3095f, 0.6765f, 0f), Quaternion.Euler(22f, -15f, 0f), Vector3.one);
            Add(rcdaRoot.transform, CharacterMeshes.Prism(4, 0.034f, 0.03f, 0.08f, 0.22f, 0f), p.Dark, new Vector3(0.2495f, 0.7065f, 0.23f), Quaternion.Euler(26f, -15f, 0f), Vector3.one);
            Add(rcdaRoot.transform, CharacterMeshes.Prism(4, 0.048f, 0.044f, 0.035f, 0.15f, 0f), p.Boot, new Vector3(0.2715f, 0.7615f, 0.185f), Quaternion.Euler(82f, -15f, 0f), Vector3.one);
            gadgetGlow.Add(Lens(rcdaRoot.transform, CharacterMeshes.Sphere(14), new Vector3(0.2715f, 0.7465f, 0.405f), Quaternion.identity, new Vector3(0.055f, 0.055f, 0.038f)));

            // ---- the LPPE gadget (MV-702) — a laser-pointer-and-drill hybrid, welded to the same ----
            // glove position the RCDA occupies (a hidden gadget swap has to land in exactly the same
            // hands), reusing the RCDA's own grip/stock parts so the two weapons read as siblings from
            // the same toolbox rather than two unrelated props. Boxy Prism housing + a ridged Lathe
            // "coil" replace the RCDA's tank; a tapered hex Prism nose replaces its round barrel; one
            // cyan-white lens (the ticket's "cyan-white lens") stands in for the RCDA's two.
            var lppeGlow = new List<MeshRenderer>(1);
            var lppeRoot = new GameObject("GadgetLppe");
            lppeRoot.transform.SetParent(gunAssembly.transform, worldPositionStays: false);
            Add(lppeRoot.transform, CharacterMeshes.Prism(4, 0.06f, 0.05f, 0.32f, 0.1f, 0f), p.Metal, new Vector3(0.2715f, 0.7565f, 0.11f), Quaternion.Euler(82f, -15f, 0f), Vector3.one);
            Add(lppeRoot.transform, CharacterMeshes.Lathe(new[] { new Vector2(0.036f, 0f), new Vector2(0.05f, 0.018f), new Vector2(0.036f, 0.036f), new Vector2(0.05f, 0.054f), new Vector2(0.036f, 0.072f), new Vector2(0.05f, 0.09f), new Vector2(0.036f, 0.108f), new Vector2(0f, 0.118f) }, 14), p.Dark, new Vector3(0.2715f, 0.75f, 0.24f), Quaternion.Euler(82f, -15f, 0f), Vector3.one);
            Add(lppeRoot.transform, CharacterMeshes.Prism(6, 0.032f, 0.012f, 0.09f, 0.3f, 10f), p.Metal, new Vector3(0.2715f, 0.7485f, 0.365f), Quaternion.Euler(82f, -15f, 0f), Vector3.one);
            Add(lppeRoot.transform, CharacterMeshes.Prism(4, 0.04f, 0.034f, 0.13f, 0.2f, 0f), p.Dark, new Vector3(0.3095f, 0.6765f, 0f), Quaternion.Euler(22f, -15f, 0f), Vector3.one);
            Add(lppeRoot.transform, CharacterMeshes.Prism(4, 0.034f, 0.03f, 0.08f, 0.22f, 0f), p.Dark, new Vector3(0.2495f, 0.7065f, 0.23f), Quaternion.Euler(26f, -15f, 0f), Vector3.one);
            Add(lppeRoot.transform, CharacterMeshes.Prism(4, 0.048f, 0.044f, 0.035f, 0.15f, 0f), p.Boot, new Vector3(0.2715f, 0.7615f, 0.185f), Quaternion.Euler(82f, -15f, 0f), Vector3.one);
            lppeGlow.Add(Lens(lppeRoot.transform, CharacterMeshes.Sphere(14), new Vector3(0.2715f, 0.7465f, 0.405f), Quaternion.identity, new Vector3(0.05f, 0.05f, 0.036f)));
            lppeRoot.SetActive(false);   // RCDA is Max's run-start primary; MaxRig flips these on ActivePrimary

            // ---- MV-717: the two grip points, parented under the gun so the hands cannot come off it
            // no matter how it is posed. MV-730 (Lee: "his left arm comes right across his body to hold
            // the device... to fire the weapon his right hand should come up above waist height") swaps
            // which grip each hand takes: measured in the built rig at full aim, the coordinates near
            // the tank/nose resolve to world y ~0.87-1.0 (clear of the waist) while the ones near the
            // trigger/stock resolve to world y ~0.75-0.90 (barely above it) — an artefact of how far
            // each point sits from the shoulder-roll pivot, not something visible in these local numbers
            // alone. HandR now takes the tank/nose coordinates, so the RIGHT hand is the one that
            // visibly rises to drive the gadget; HandL takes the trigger/stock coordinates, so the LEFT
            // hand stays low and reads as bracing the gadget rather than crossing up to operate it.
            //
            // MV-851: recoloured Glove (buildNew()'s dark glove, not the gadget's own Dark housing
            // colour) — this knuckle ball is what stands in for buildNew()'s static cuff+glove now that
            // the arm itself is bare skin (see the arms section above for why the cuff/glove couldn't
            // just ride a static shoulder pivot instead).
            var handR = new GameObject("HandR").transform;
            handR.SetParent(gunAssembly.transform, worldPositionStays: false);
            handR.localPosition = new Vector3(0.27f, 0.75f, 0.30f);
            Add(handR, CharacterMeshes.Sphere(10), p.Glove, Vector3.zero, Quaternion.identity, new Vector3(0.09f, 0.09f, 0.09f));

            var handL = new GameObject("HandL").transform;
            handL.SetParent(gunAssembly.transform, worldPositionStays: false);
            handL.localPosition = new Vector3(0.29f, 0.70f, 0.05f);
            Add(handL, CharacterMeshes.Sphere(10), p.Glove, Vector3.zero, Quaternion.identity, new Vector3(0.09f, 0.09f, 0.09f));

            // ---- the Shoulder Rack mount (MV-702) — migrated from ShoulderRack's own placeholder ----
            // GameObject (see that class's history) onto MaxRig proper, so it stops being tinted by
            // CharacterSkinDirector (which claims every renderer under Max's own IDamageable — see
            // MaxRig's class doc). Right shoulder, outboard of the arm beam at (0.275, 1.204, 0.01).
            var rackTubeGlow = new MeshRenderer[3];
            var rackRoot = new GameObject("ShoulderRackMount");
            rackRoot.transform.SetParent(root, worldPositionStays: false);
            Add(rackRoot.transform, CharacterMeshes.Prism(4, 0.09f, 0.08f, 0.3f, 0.08f, 0f), p.Metal, new Vector3(0.33f, 1.28f, 0.02f), Quaternion.Euler(0f, -15f, 0f), Vector3.one);
            for (int i = 0; i < 3; i++)
            {
                float yOff = (i - 1) * 0.075f;
                Add(rackRoot.transform, CharacterMeshes.Beam(0.22f, 0.032f, 0.028f, 8), p.Dark, new Vector3(0.33f, 1.28f + yOff, 0.16f), Quaternion.Euler(90f, -15f, 0f), Vector3.one);
                rackTubeGlow[i] = Lens(rackRoot.transform, CharacterMeshes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.03f, 0.012f), new Vector2(0.024f, 0.03f) }, 10), new Vector3(0.33f, 1.28f + yOff, 0.275f), Quaternion.Euler(90f, -15f, 0f), Vector3.one);
            }
            rackRoot.SetActive(false);   // shown only once ShoulderRack.IsBought (AC2, carried over from MV-694)

            return new MaxBodyResult(gadgetGlow.ToArray(), hips, knees, rcdaRoot, lppeRoot, lppeGlow.ToArray(),
                                     rackRoot, rackTubeGlow, gunAssembly.transform, armL, armR, handL, handR, head);
        }

        /// <summary>An empty rotation handle — the hinge <see cref="MaxRig.TickRun"/> swings a foot
        /// from, since nothing in the generated mesh is itself a joint.</summary>
        private static Transform Hip(Transform root, string name, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, worldPositionStays: false);
            go.transform.localPosition = at;
            return go.transform;
        }

        private static Transform Add(Transform root, Mesh mesh, Material mat,
                                     Vector3 at, Quaternion rot, Vector3 scale)
            => CharacterPart.Add(root, mesh, mat, at, rot, scale);

        private static MeshRenderer Lens(Transform root, Mesh mesh,
                                         Vector3 at, Quaternion rot, Vector3 scale)
            => CharacterPart.AddLens(root, mesh, at, rot, scale);
    }
}
