using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Shared press/drag/release input for a joystick-aimed ability (MV-372: "build this once, in the
    /// shared joystick-ability layer — not separately in the balloon and the teleport"). Water Balloon
    /// and Teleport used to duplicate this beat for beat in their own scripts; this is that duplicated
    /// shape, factored out, plus the arm/disarm abort Lee asked for.
    ///
    /// Lee's design, 12 Aug 2026: dragging away from the centre "arms" the control (it brightens);
    /// dragging back into the centre dead-zone "disarms" it (back to normal) so releasing does nothing —
    /// no fire, no cooldown, no cost. The player can arm/disarm as many times as they like within one
    /// touch before deciding. Any future joystick ability inherits this with no extra work — it only
    /// has to answer "is the ability ready", "what do the aim visuals look like" and "what does release
    /// actually do".
    /// </summary>
    public abstract class AbilityJoystickControlBase : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        /// <summary>Drag distance, screen px, for the full authored throw/blink distance — matches
        /// <c>HudController.AddOnScreenStick</c>'s own 90 px movementRange so every stick on screen
        /// feels the same weight under a thumb.</summary>
        public const float DragRadiusPixels = 90f;

        /// <summary>How far the visual knob itself travels, px — matches the move/aim knobs' own 26 px
        /// offset at full deflection (<c>HudController.UpdateJoysticks</c>).</summary>
        public const float KnobRadiusPixels = 26f;

        /// <summary>Fraction of <see cref="DragRadiusPixels"/> the drag must clear to arm — big enough
        /// that a thumb resting near centre under pressure doesn't arm by accident, small enough that a
        /// real aim is armed well before it reaches a throwable distance. Below this, the thumb reads as
        /// still "at centre" and the control stays disarmed.</summary>
        public const float ArmDeadZoneFraction = 0.15f;

        /// <summary>Ring alpha while armed — deliberately full-bright rather than a small bump over
        /// resting, so the state change reads in peripheral vision mid-fight per the ticket.</summary>
        private const float ArmedRingAlpha = 1f;

        private RectTransform _knob;
        private Transform _origin;
        private Image _rings;
        private float _restingRingAlpha = 0.85f;

        private bool _dragging;
        private Vector2 _pressScreenPos;
        private Vector3 _direction = Vector3.forward;
        private float _distanceFraction;
        private bool _armed;

        /// <summary>True while the ability is being aimed — a test reads this without simulating a real
        /// drag.</summary>
        public bool IsAiming => _dragging;

        /// <summary>The direction the current (or most recently released) drag would fire toward.</summary>
        public Vector3 Direction => _direction;

        /// <summary>How far the current drag has come toward <see cref="DragRadiusPixels"/>, 0..1.</summary>
        protected float DistanceFraction => _distanceFraction;

        /// <summary>True once the drag has cleared <see cref="ArmDeadZoneFraction"/> — releasing now
        /// fires. False while the thumb is still within the centre dead-zone (including the moment of
        /// press) — releasing now is a no-op abort: no fire, no cooldown, no cost. Toggles live as the
        /// drag moves, so the player can change their mind repeatedly within one touch.</summary>
        public bool IsArmed => _armed;

        /// <summary>Wired by the owner right after construction — knob is driven from the drag, origin
        /// is where aim visuals are built from, rings is the joystick's own ring image
        /// (<see cref="AbilityControlArt.JoystickVisual.Rings"/>) that brightens on arm. Rings may be
        /// null (existing tests construct controls without a HUD) — the arm/disarm state machine still
        /// works, it just has nothing to brighten.</summary>
        protected void InitBase(RectTransform knob, Transform origin, Image rings)
        {
            _knob = knob;
            _origin = origin;
            _rings = rings;
            if (_rings != null) _restingRingAlpha = _rings.color.a;
        }

        /// <summary>True once the ability itself is owned. Gates whether a press does anything at all —
        /// the control isn't even on screen while unowned (WV-240's own acquisition gate hides the whole
        /// joystick), so a press reaching here is a defensive no-op, not a readability concern.</summary>
        protected abstract bool IsOwned { get; }

        /// <summary>Owned AND currently spendable — off cooldown and (for Water Balloon) a cell banked.
        /// MV-381: no longer gates whether a press shows the aim preview — a press on cooldown or out of
        /// cells still previews the arc/circle (dimmed red by <see cref="ApplyArmedTint"/>) because the
        /// control IS visible and being touched in that state, and answering a press with total silence
        /// was exactly the bug ("no visible target circle/aim indicator") a player out of cells or on
        /// cooldown would hit on every single press. Only <see cref="Fire"/> at release is still gated on
        /// this.</summary>
        protected abstract bool AbilityReady { get; }

        /// <summary>Show whatever aim preview this ability draws (arc, landing circle, reticle...).</summary>
        protected abstract void ShowAimVisuals();

        /// <summary>Hide the aim preview shown by <see cref="ShowAimVisuals"/>.</summary>
        protected abstract void HideAimVisuals();

        /// <summary>Rebuild the aim preview for the current <see cref="DistanceFraction"/>/<see cref="Direction"/>
        /// and reflect <see cref="IsArmed"/> in it (MV-372 AC5: "targeting visuals reflect the armed
        /// state") — called after every press and every drag.</summary>
        protected abstract void RebuildAimVisual();

        /// <summary>Actually perform the throw/blink/etc. Only ever called on release while
        /// <see cref="IsArmed"/> is true.</summary>
        protected abstract void Fire(Vector3 direction);

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!IsOwned) return;

            _dragging = true;
            _pressScreenPos = eventData.position;
            _direction = InitialFacing();
            _distanceFraction = 0f;
            SetArmed(false);
            ShowAimVisuals();
            RebuildAimVisual();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging) return;

            Vector2 delta = eventData.position - _pressScreenPos;
            _distanceFraction = Mathf.Clamp01(delta.magnitude / DragRadiusPixels);

            if (delta.sqrMagnitude > 1f)
            {
                Vector2 dir2 = delta.normalized;
                _direction = new Vector3(dir2.x, 0f, dir2.y);
                if (_knob != null) _knob.anchoredPosition = dir2 * (_distanceFraction * KnobRadiusPixels);
            }

            SetArmed(_distanceFraction > ArmDeadZoneFraction);
            RebuildAimVisual();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_dragging) return;

            _dragging = false;
            if (_knob != null) _knob.anchoredPosition = Vector2.zero;
            HideAimVisuals();

            // MV-372: released while disarmed (thumb back at/near centre) — abort. No fire, no
            // cooldown, no cost of any kind. MV-381: also abort (no fire) if the ability itself
            // isn't actually spendable right now — the preview was allowed to show, but the throw
            // still can't happen on cooldown or with no cell banked.
            if (_armed && AbilityReady) Fire(_direction);
            SetArmed(false);
        }

        protected virtual void OnDisable()
        {
            _dragging = false;
            HideAimVisuals();
            SetArmed(false);
        }

        /// <summary>Aims the joystick at <paramref name="direction"/> without a drag gesture — MV-373's
        /// auto-fire "moves the joystick" itself before it throws. Moves the visible knob the same way
        /// <see cref="OnDrag"/> does, at the given <paramref name="distanceFraction"/>, so an auto-aimed
        /// throw reads on screen exactly like a manually dragged one.</summary>
        protected void SetAimDirection(Vector3 direction, float distanceFraction)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude > 1e-6f) _direction = flat.normalized;
            _distanceFraction = Mathf.Clamp01(distanceFraction);

            if (_knob != null)
            {
                Vector2 dir2 = new Vector2(_direction.x, _direction.z);
                _knob.anchoredPosition = dir2 * (_distanceFraction * KnobRadiusPixels);
            }
        }

        protected void SetArmed(bool armed)
        {
            if (_armed == armed) return;
            _armed = armed;
            if (_rings == null) return;
            Color c = _rings.color;
            c.a = armed ? ArmedRingAlpha : _restingRingAlpha;
            _rings.color = c;
        }

        private Vector3 InitialFacing()
        {
            if (_origin == null) return Vector3.forward;
            Vector3 f = _origin.forward; f.y = 0f;
            return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
        }

        private static MaterialPropertyBlock s_tintBlock;

        /// <summary>Tints a world-space aim-mesh renderer to match the armed/ready state (MV-372 AC5,
        /// MV-381) — full bright white when armed, dimmed white when merely owned-and-ready, and a dim
        /// red wash when <paramref name="ready"/> is false (on cooldown or no cell banked) so a press in
        /// that state still answers with a preview rather than silence, while visibly reading as "won't
        /// fire yet" rather than as fully armed. Layered on top of whatever per-vertex gradient the mesh
        /// itself already bakes in (e.g. <see cref="MaxWorlds.VFX.WaterBalloonAimMesh"/>'s near/far fade).
        /// Both concrete controls' arc/circle renderers share the same cached white/alpha-blend material
        /// instance (<see cref="MaxWorlds.VFX.VfxMaterials.AlphaBlend"/>) — going through a
        /// <see cref="MaterialPropertyBlock"/> tints only this renderer's draw call rather than every
        /// renderer sharing that material (which <c>renderer.material</c> would also do, on top of
        /// instantiating and leaking a new material every edit-mode call).</summary>
        protected static void ApplyArmedTint(GameObject meshGo, bool armed, bool ready = true)
        {
            if (meshGo == null) return;
            var renderer = meshGo.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            s_tintBlock ??= new MaterialPropertyBlock();
            Color tint = ready
                ? new Color(1f, 1f, 1f, armed ? 1f : 0.4f)
                : new Color(1f, 0.3f, 0.25f, 0.35f);
            renderer.GetPropertyBlock(s_tintBlock);
            s_tintBlock.SetColor("_BaseColor", tint);
            s_tintBlock.SetColor("_Color", tint);
            renderer.SetPropertyBlock(s_tintBlock);
        }
    }
}
