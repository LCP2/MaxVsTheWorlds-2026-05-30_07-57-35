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

        /// <summary>MV-1133: the gadget's own housing/barrel-support colour (buildV3's "gun" material) —
        /// distinct from <see cref="Dark"/> (the Shoulder Rack's own near-black tubes, untouched by this
        /// ticket) so the two don't drift together by accident.</summary>
        public readonly Material Housing;

        public MaxPalette(Material skin, Material hair, Material tunic, Material tunicDark, Material belt,
                          Material boot, Material sole, Material glove, Material eye, Material pupil,
                          Material dark, Material metal, Material housing)
        {
            Skin = skin; Hair = hair; Tunic = tunic; TunicDark = tunicDark; Belt = belt;
            Boot = boot; Sole = sole; Glove = glove; Eye = eye; Pupil = pupil;
            Dark = dark; Metal = metal; Housing = housing;
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

        /// <summary>MV-1161: an otherwise-empty child of <see cref="Head"/>, carrying the fixed
        /// chin-up pitch (see <c>MaxBody.ChinUpRotation</c>) the whole face/hair-cap geometry above is
        /// authored against. <c>MaxRig</c> parents the flowing hair locks under this instead of
        /// <see cref="Head"/> directly, so they tilt along with the face they grow out of while
        /// <see cref="Head"/> itself keeps carrying only the head-lag yaw.</summary>
        public readonly Transform FaceTilt;

        public MaxBodyResult(MeshRenderer[] gadgetGlow, Transform[] hips, Transform[] knees, GameObject rcdaGadget,
                             GameObject lppeGadget, MeshRenderer[] lppeGlow, GameObject rackMount,
                             MeshRenderer[] rackTubeGlow, Transform gun, Transform armL, Transform armR,
                             Transform handL, Transform handR, Transform head, Transform faceTilt)
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
            FaceTilt = faceTilt;
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
    /// Hair below is a static cap + back-of-head mass only — buildNew()'s own flowing locks are
    /// ported separately, in <see cref="MaxHair"/>/<see cref="MaxHairRig"/> (MV-854), because they are
    /// rebuilt every frame rather than built once here like the rest of this class.
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

            // ---- MV-1133: the belt's one steel buckle, and a diagonal chest strap shoulder-to-hip (the
            // belt itself is now dark webbing rather than brown leather — see MaxRig's Belt colour) —
            // ported literally from buildV3()'s own belt section.
            Add(root, CharacterMeshes.Prism(4, 0.05f, 0.05f, 0.07f, 0.2f, 0f), p.Metal, new Vector3(0f, 0.897f, 0.2f), Quaternion.Euler(90f, 0f, 0f), new Vector3(1f, 0.5f, 1f), "Buckle");
            var chestStrap = new GameObject("ChestStrap");
            chestStrap.transform.SetParent(root, worldPositionStays: false);
            chestStrap.transform.localPosition = new Vector3(0f, 1.13f, 0f);
            chestStrap.transform.localRotation = Quaternion.Euler(0f, 0f, 38f);
            Add(chestStrap.transform, CharacterMeshes.Lathe(new[] { new Vector2(0.2f, -0.03f), new Vector2(0.232f, -0.028f), new Vector2(0.232f, 0.028f), new Vector2(0.2f, 0.03f) }, 28), p.Belt, Vector3.zero, Quaternion.identity, new Vector3(1.06f, 1f, 1f), "ChestStrapBand");

            // ---- head, hair (buildNew(), literal) — wrapped in a "Head" pivot so MaxRig can yaw it
            // independently for the head-lag cue (MV-717). The pivot sits at root's own origin (identity
            // local transform), so every child keeps the exact authored offset below; a pure yaw of the
            // pivot only ever moves X/Z, and every child's X/Z is already relative to the midline (x=0),
            // so the pivot's own height is irrelevant to the result.
            //
            // The goggles, the goggle strap and the two hair bunches are GONE (MV-851 AC2) — visible
            // eyes (white sphere + dark pupil, canted up toward the camera) and heavy brows take their
            // place, and the hair is a low cap + back-of-head mass rather than a lathe-plus-two-bunches.
            // buildNew()'s flowing locks are not built here — MaxRig grows them separately, under this
            // very Head pivot, once it is built (MV-854's MaxHairRig).
            var headGroup = new GameObject("Head");
            headGroup.transform.SetParent(root, worldPositionStays: false);
            var head = headGroup.transform;

            // MV-1161 (Lee, 10 Oct: "a lot of his face is covered" from the play camera's elevated,
            // down-looking angle): every piece below is pitched a fixed 15 degrees chin-up about the
            // neck point (0, 1.474, 0) — ChinUp()/ChinUpRot() apply that rotation to each piece's own
            // authored position/rotation, so the geometry itself is untouched, just re-aimed. This sits
            // BENEATH MaxRig's own head-lag yaw (TickHeadLag only ever rotates the outer "Head" pivot
            // these pieces are children of, about the vertical) rather than replacing it.
            //
            // FaceTilt is an otherwise-empty pivot, at the same fixed tilt, that MaxRig parents the
            // flowing hair locks (MaxHairRig, built separately once this body exists) under instead of
            // the bare Head pivot — so the hair tilts along with the face it grows out of.
            var faceTiltGroup = new GameObject("FaceTilt");
            faceTiltGroup.transform.SetParent(head, worldPositionStays: false);
            faceTiltGroup.transform.localPosition = ChinUp(Vector3.zero);
            faceTiltGroup.transform.localRotation = ChinUpRotation;
            var faceTilt = faceTiltGroup.transform;

            Add(head, CharacterMeshes.Lathe(new[] { new Vector2(0f, 1.474f), new Vector2(0.145f, 1.504f), new Vector2(0.195f, 1.584f), new Vector2(0.205f, 1.704f), new Vector2(0.185f, 1.794f), new Vector2(0f, 1.824f) }, 20), p.Skin, ChinUp(Vector3.zero), ChinUpRotation, Vector3.one);
            foreach (float sx in new[] { -1f, 1f })
            {
                // MV-1161 AC4: eyes 15% larger, same position/cant (now carried through ChinUp/ChinUpRot).
                Add(head, CharacterMeshes.Sphere(14), p.Eye, ChinUp(new Vector3(sx * 0.083f, 1.66f, 0.17f)), ChinUpRot(Quaternion.Euler(-32f, 0f, 0f)), new Vector3(0.092f, 0.104f, 0.055f) * 1.15f);
                Add(head, CharacterMeshes.Sphere(12), p.Pupil, ChinUp(new Vector3(sx * 0.08f, 1.655f, 0.196f)), ChinUpRot(Quaternion.Euler(-32f, 0f, 0f)), new Vector3(0.05f, 0.062f, 0.022f) * 1.15f);
                Add(head, CharacterMeshes.Prism(4, 0.022f, 0.022f, 0.11f, 0.2f, 0f), p.Hair, ChinUp(new Vector3(sx * 0.088f, 1.73f, 0.19f)), ChinUpRot(Quaternion.Euler(-20f, 0f, 90f + sx * 16f)), new Vector3(1f, 1f, 0.8f));
            }
            // MV-1133: the tight cap/back-of-head mass, re-matched to buildV3()'s own numbers (blue-black
            // now, via MaxRig's Hair colour) — the shape itself barely moved from the MV-851 original.
            // MV-1161 AC3: the three lowest profile points raised from 1.74/1.745/1.778 to a 1.78 floor
            // (a trimmed brim, not a redrawn cap) so the cap's own front overhang clears the brow once
            // the whole head is tilted chin-up.
            Add(head, CharacterMeshes.Lathe(new[] { new Vector2(0f, 1.78f), new Vector2(0.165f, 1.785f), new Vector2(0.214f, 1.80f), new Vector2(0.222f, 1.83f), new Vector2(0.2f, 1.885f), new Vector2(0.125f, 1.918f), new Vector2(0f, 1.924f) }, 22), p.Hair, ChinUp(new Vector3(0f, 0f, -0.04f)), ChinUpRotation, Vector3.one);
            Add(head, CharacterMeshes.Sphere(16), p.Hair, ChinUp(new Vector3(0f, 1.73f, -0.075f)), ChinUpRotation, new Vector3(0.42f, 0.4f, 0.34f));

            // ---- arms (MV-1133: full dark-blue sleeves, no skin shown). The dynamic stretch target
            // PoseArm needs (MV-717) is still a single tapered beam per arm — see that section's own
            // doc below — now coloured Tunic instead of Skin, and thicker (MaxRig.SleeveWidth). A static
            // shoulder cap rides the fixed shoulder anchor (buildV3()'s own sphere) so the sleeve reads
            // as continuous with the torso even though the stretch beam itself starts a little below it;
            // the cuff and glove are still built as static decoration on the gun's own hand grips (see
            // the gadget section below), which already ride at the hand end for free.
            Add(root, CharacterMeshes.Sphere(12), p.Tunic, new Vector3(-0.25f, 1.36f, 0.02f), Quaternion.identity, new Vector3(0.14f, 0.13f, 0.14f), "ShoulderCapL");
            Add(root, CharacterMeshes.Sphere(12), p.Tunic, new Vector3(0.25f, 1.36f, 0.02f), Quaternion.identity, new Vector3(0.14f, 0.13f, 0.14f), "ShoulderCapR");
            var armL = Add(root, CharacterMeshes.Beam(1f, 0.5f, 0.4f, 6), p.Tunic, new Vector3(-0.25f, 1.36f, 0.02f), Quaternion.identity, new Vector3(0.096f, 0.44f, 0.096f));
            var armR = Add(root, CharacterMeshes.Beam(1f, 0.5f, 0.4f, 6), p.Tunic, new Vector3(0.25f, 1.36f, 0.02f), Quaternion.identity, new Vector3(0.096f, 0.44f, 0.096f));

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
            BuildGunBody(rcdaRoot.transform, p, gadgetGlow);

            // ---- the LPPE gadget (MV-702) — a laser-pointer-and-drill hybrid, welded to the same ----
            // glove position the RCDA occupies (a hidden gadget swap has to land in exactly the same
            // hands), reusing the RCDA's own grip/stock parts so the two weapons read as siblings from
            // the same toolbox rather than two unrelated props. Boxy Prism housing + a ridged Lathe
            // "coil" replace the RCDA's tank; a tapered hex Prism nose replaces its round barrel; one
            // cyan-white lens (the ticket's "cyan-white lens") stands in for the RCDA's two.
            var lppeGlow = new List<MeshRenderer>(2);
            var lppeRoot = new GameObject("GadgetLppe");
            lppeRoot.transform.SetParent(gunAssembly.transform, worldPositionStays: false);
            BuildGunBody(lppeRoot.transform, p, lppeGlow);
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
            // MV-1133: a darker cuff rides each hand grip alongside the glove — buildV3()'s sleeve ends
            // "a darker cuff and a dark glove", and the hand grips are the only static anchor left at
            // the wrist end once the sleeve itself is a dynamic stretch beam (see the arms section above).
            var handR = new GameObject("HandR").transform;
            handR.SetParent(gunAssembly.transform, worldPositionStays: false);
            handR.localPosition = new Vector3(0.27f, 0.75f, 0.30f);
            Add(handR, CharacterMeshes.Prism(8, 0.095f, 0.09f, 0.045f, 0.2f, 0f), p.TunicDark, Vector3.zero, Quaternion.identity, Vector3.one, "CuffR");
            Add(handR, CharacterMeshes.Sphere(10), p.Glove, Vector3.zero, Quaternion.identity, new Vector3(0.09f, 0.09f, 0.09f));

            var handL = new GameObject("HandL").transform;
            handL.SetParent(gunAssembly.transform, worldPositionStays: false);
            handL.localPosition = new Vector3(0.29f, 0.70f, 0.05f);
            Add(handL, CharacterMeshes.Prism(8, 0.095f, 0.09f, 0.045f, 0.2f, 0f), p.TunicDark, Vector3.zero, Quaternion.identity, Vector3.one, "CuffL");
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
                                     rackRoot, rackTubeGlow, gunAssembly.transform, armL, armR, handL, handR, head,
                                     faceTilt);
        }

        /// <summary>MV-1161: the neck point everything above pitches about, in Head-local space.</summary>
        private static readonly Vector3 ChinUpPivot = new Vector3(0f, 1.474f, 0f);

        /// <summary>MV-1161 AC1: the fixed chin-up pitch, negative like the eyes' own existing -32 degree
        /// cant (see the eye <c>Add</c> call below) — in this rig's convention a negative X rotation
        /// tilts a forward-facing surface UP toward the elevated, down-looking play camera, matching
        /// <c>FixedAngleCameraRig</c>'s own <c>Quaternion.Euler(pitchDegrees, 0, 0)</c> (positive pitch
        /// there tilts the CAMERA down to look at a target below it — the mirror case).</summary>
        private const float ChinUpPitchDegrees = -15f;

        private static readonly Quaternion ChinUpRotation = Quaternion.Euler(ChinUpPitchDegrees, 0f, 0f);

        /// <summary>Rotates an authored Head-local point by <see cref="ChinUpRotation"/> about
        /// <see cref="ChinUpPivot"/> — the point stays exactly where it was when the pitch is ever
        /// changed back to zero.</summary>
        private static Vector3 ChinUp(Vector3 headLocalPoint)
            => ChinUpPivot + ChinUpRotation * (headLocalPoint - ChinUpPivot);

        /// <summary>Composes an authored Head-local rotation with <see cref="ChinUpRotation"/>, so a
        /// part's own tilt (the eyes' -32 degree cant, a brow's facet angle, ...) rotates rigidly along
        /// with the rest of the face instead of staying level while everything around it pitches.</summary>
        private static Quaternion ChinUpRot(Quaternion headLocalRotation)
            => ChinUpRotation * headLocalRotation;

        /// <summary>
        /// MV-1133: the gadget's shared body — a dark housing, a pale steel side plate and barrel, a
        /// flared muzzle, a short stock, an energy cell on top and an emitter at the muzzle — ported
        /// literally from buildV3()'s own <c>g</c> assembly. Built once per weapon submesh (RCDA and
        /// LPPE each get their own copy, called from <see cref="Build"/>) so the housing/barrel read as
        /// the same tool in every world; only the two glow parts' RUNTIME TINT differs by weapon
        /// (<see cref="MaxRig"/> colours <see cref="MaxBodyResult.GadgetGlow"/> and
        /// <see cref="MaxBodyResult.LppeGlow"/> separately). Appended to <paramref name="glow"/> in
        /// build order: index 0 is the energy cell (near the housing), index 1 is the emitter (at the
        /// muzzle) — mirrors the old RCDA tank/muzzle lens ordering <c>MV730MaxArmsGunWaddleTests</c>
        /// already depends on.
        ///
        /// <paramref name="gunRoot"/> is <c>GadgetRcda</c> or <c>GadgetLppe</c> — both already sit at the
        /// gun assembly's own origin (feet-space absolute coordinates). The "GunBody" pivot below shares
        /// buildV3()'s own X/Z anchor (0.2715, _, 0.11), but its Y is re-measured rather than copied
        /// straight from buildV3()'s 0.7565: that number sat the housing's own CENTRE, while the old
        /// RCDA geometry it replaces was authored around where its EMITTER ended up (0.7465) — and
        /// <see cref="MaxRig.BarrelHeight"/>/<c>TheGadgetIsHeldWhereTheWaterActuallyComesFrom</c> judge
        /// the gadget by where its emitter sits at full aim, not by the housing's own centre. 0.701
        /// is buildV3()'s 0.7565 corrected by the measured gap (built, not derived by hand) between the
        /// two conventions, so the real emitter mesh lands within 0.05 m of <c>BarrelHeight(1)</c> the
        /// same way the pre-MV-1133 geometry did.
        /// </summary>
        private static void BuildGunBody(Transform gunRoot, in MaxPalette p, List<MeshRenderer> glow)
        {
            var g = new GameObject("GunBody");
            g.transform.SetParent(gunRoot, worldPositionStays: false);
            g.transform.localPosition = new Vector3(0.2715f, 0.701f, 0.11f);
            g.transform.localRotation = Quaternion.Euler(0f, -15f, 0f);
            var gt = g.transform;

            var z90 = Quaternion.Euler(90f, 0f, 0f);
            Add(gt, CharacterMeshes.Prism(4, 0.082f, 0.076f, 0.27f, 0.12f, 0f), p.Housing, new Vector3(0f, 0f, 0f), z90, Vector3.one, "Housing");
            Add(gt, CharacterMeshes.Prism(4, 0.05f, 0.05f, 0.2f, 0.1f, 0f), p.Metal, new Vector3(0f, 0f, 0f), z90, new Vector3(1.75f, 1f, 0.45f), "SidePlate");
            glow.Add(Lens(gt, CharacterMeshes.Prism(10, 0.04f, 0.04f, 0.16f, 0.08f, 0f), new Vector3(0f, 0.082f, -0.01f), z90, Vector3.one));
            Add(gt, CharacterMeshes.Prism(10, 0.05f, 0.05f, 0.03f, 0.2f, 0f), p.Housing, new Vector3(0f, 0.082f, 0.08f), z90, Vector3.one, "HousingRing1");
            Add(gt, CharacterMeshes.Prism(10, 0.05f, 0.05f, 0.03f, 0.2f, 0f), p.Housing, new Vector3(0f, 0.082f, -0.10f), z90, Vector3.one, "HousingRing2");
            Add(gt, CharacterMeshes.Prism(10, 0.044f, 0.038f, 0.2f, 0.05f, 0f), p.Metal, new Vector3(0f, 0.004f, 0.23f), z90, Vector3.one, "Barrel");
            Add(gt, CharacterMeshes.Prism(10, 0.054f, 0.054f, 0.03f, 0.2f, 0f), p.Housing, new Vector3(0f, 0.004f, 0.18f), z90, Vector3.one, "BarrelRing1");
            Add(gt, CharacterMeshes.Prism(10, 0.05f, 0.05f, 0.03f, 0.2f, 0f), p.Housing, new Vector3(0f, 0.004f, 0.26f), z90, Vector3.one, "BarrelRing2");
            Add(gt, CharacterMeshes.Prism(10, 0.052f, 0.07f, 0.07f, 0.2f, 0f), p.Housing, new Vector3(0f, 0.004f, 0.36f), z90, Vector3.one, "Muzzle");
            glow.Add(Lens(gt, CharacterMeshes.Sphere(14), new Vector3(0f, 0.004f, 0.395f), Quaternion.identity, new Vector3(0.085f, 0.085f, 0.05f)));
            Add(gt, CharacterMeshes.Prism(4, 0.06f, 0.042f, 0.18f, 0.2f, 0f), p.Housing, new Vector3(0f, -0.012f, -0.21f), Quaternion.Euler(98f, 0f, 0f), Vector3.one, "Stock");
            Add(gt, CharacterMeshes.Prism(4, 0.036f, 0.032f, 0.11f, 0.2f, 0f), p.Belt, new Vector3(0f, -0.095f, -0.07f), Quaternion.Euler(18f, 0f, 0f), Vector3.one, "StrapWrap1");
            Add(gt, CharacterMeshes.Prism(4, 0.032f, 0.029f, 0.09f, 0.2f, 0f), p.Belt, new Vector3(0f, -0.085f, 0.12f), Quaternion.Euler(8f, 0f, 0f), Vector3.one, "StrapWrap2");
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
                                     Vector3 at, Quaternion rot, Vector3 scale, string name = "Part")
            => CharacterPart.Add(root, mesh, mat, at, rot, scale, name);

        private static MeshRenderer Lens(Transform root, Mesh mesh,
                                         Vector3 at, Quaternion rot, Vector3 scale)
            => CharacterPart.AddLens(root, mesh, at, rot, scale);
    }
}
