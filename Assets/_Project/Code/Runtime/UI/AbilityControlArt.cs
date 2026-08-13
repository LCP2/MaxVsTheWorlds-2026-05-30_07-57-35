using UnityEngine;
using UnityEngine.UI;

namespace MaxWorlds.UI
{
    /// <summary>
    /// The active-ability on-screen controls (v0.5 recut spec §6a, WV-241): Water Balloon's and
    /// Teleport's joysticks, plus a button-style control other button-shaped abilities can reuse.
    /// Each "appears only once acquired, and becomes more prominent as that ability's level rises
    /// (bigger / brighter / more detailed)" per spec — this is the shared building block for that
    /// prominence curve.
    ///
    /// Speaks the same TechRings visual language <c>HudController</c>'s move/aim joysticks already
    /// use, so a new control never looks like a different game bolted onto the HUD.
    ///
    /// Pure UI construction — no input, no cooldown sweep, no ability-state reads. Whoever wires the
    /// joystick drag, button taps and the live cooldown radial (WV-240 — <c>WeaponSystemState</c>'s own
    /// comment calls this "a controls-ticket concern") positions these, gates their visibility on
    /// <c>WeaponSystemState.IsAcquired</c>, and drives the radial fill from
    /// <c>WeaponSystemState.EffectiveCooldownSeconds</c>; this only builds what a control looks like at
    /// a given level.
    /// </summary>
    public static class AbilityControlArt
    {
        /// <summary>A control never reads as fully dim — even a level 1 ability was just unlocked and
        /// has to be findable at a glance, not mistaken for background chrome.</summary>
        public const float MinProminence = 0.4f;

        /// <summary>
        /// How prominent a control reads at <paramref name="level"/> of <paramref name="maxLevel"/>:
        /// <see cref="MinProminence"/> at level 1, rising to 1 at the level cap. A single-level ability
        /// (cap 1) is always fully prominent — there is no "level 1 of 1" to grow into, so it must
        /// never read as half-built.
        /// </summary>
        public static float Prominence(int level, int maxLevel)
        {
            if (maxLevel <= 1) return 1f;
            level = Mathf.Clamp(level, 1, maxLevel);
            float t = Mathf.InverseLerp(1f, maxLevel, level);
            return Mathf.Lerp(MinProminence, 1f, t);
        }

        // ---------- button-style controls ----------

        public readonly struct ButtonVisual
        {
            public readonly RectTransform Root;
            public readonly Image Glow;
            public readonly Image Ring;
            public readonly Image Radial;
            public readonly Text Label;

            public ButtonVisual(RectTransform root, Image glow, Image ring, Image radial, Text label)
            {
                Root = root; Glow = glow; Ring = ring; Radial = radial; Label = label;
            }
        }

        /// <summary>
        /// A button-style control: the same TechRings ring + ready glow + cooldown-radial shape as
        /// HudController's move/aim joysticks, sized and brightened by <see cref="Prominence"/>, plus
        /// a small detail pip per level beyond the first — so a Teleport at L2+ (longer aimed blink)
        /// visibly reads as more built-out than its L1, not just a re-tinted copy of the same button.
        /// </summary>
        public static ButtonVisual BuildButton(RectTransform parent, string name, Vector2 anchoredPos,
            float baseSize, Color color, string label, int level, int maxLevel)
        {
            float prominence = Prominence(level, maxLevel);
            float size = baseSize * Mathf.Lerp(0.82f, 1f, prominence);

            var root = NewRect(name, parent);
            Anchor(root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f));
            root.anchoredPosition = anchoredPos;
            root.sizeDelta = new Vector2(size, size);

            var glow = AddImage(root, HudTextures.TechRings(160, 3), Color.clear, "Glow");
            Stretch(glow.rectTransform, 4f);
            glow.raycastTarget = false;

            var ring = AddImage(root, HudTextures.TechRings(160, 3),
                Fade(color, Mathf.Lerp(0.55f, 1f, prominence)), "Ring");
            Stretch(ring.rectTransform);
            ring.raycastTarget = false;

            var lbl = AddText(root, Mathf.Lerp(20f, 26f, prominence), Fade(color, Mathf.Lerp(0.7f, 1f, prominence)));
            Stretch(lbl.rectTransform);
            lbl.text = label;

            var radial = AddImage(root, HudTextures.Disc(160), new Color(0f, 0f, 0f, 0.5f), "Radial");
            Stretch(radial.rectTransform, -6f);
            radial.type = Image.Type.Filled;
            radial.fillMethod = Image.FillMethod.Radial360;
            radial.fillOrigin = (int)Image.Origin360.Top;
            radial.fillClockwise = true;
            radial.fillAmount = 0f;
            radial.raycastTarget = false;

            AddDetailPips(root, size, color, level, maxLevel);

            return new ButtonVisual(root, glow, ring, radial, lbl);
        }

        // ---------- joystick-style control (Water Balloon) ----------

        public readonly struct JoystickVisual
        {
            public readonly RectTransform Root;
            public readonly Image Rings;
            public readonly RectTransform Knob;
            public readonly Text Label;

            public JoystickVisual(RectTransform root, Image rings, RectTransform knob, Text label)
            {
                Root = root; Rings = rings; Knob = knob; Label = label;
            }
        }

        /// <summary>
        /// The Water Balloon joystick: the same rings-plus-knob shape as the move/aim sticks, sized and
        /// brightened by <see cref="Prominence"/> — level is the ability's whole upgrade (spec §6a:
        /// "level = throw DISTANCE"), so a maxed joystick has to visibly out-shine a freshly-acquired
        /// one. The knob itself is left centred; WV-240 drives it from the drag input. Unlike the
        /// unlabelled move/aim sticks, this one names itself (MV-337) — a caption below the rings, clear
        /// of the knob's own travel, so it never gets covered while the player is aiming a throw.
        /// </summary>
        public static JoystickVisual BuildJoystick(RectTransform parent, string name, Vector2 anchoredPos,
            Color color, string label, int level, int maxLevel)
        {
            float prominence = Prominence(level, maxLevel);
            float baseSize = 200f * Mathf.Lerp(0.8f, 1f, prominence);

            var root = NewRect(name, parent);
            Anchor(root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
            root.anchoredPosition = anchoredPos;
            root.sizeDelta = new Vector2(baseSize, baseSize);

            var rings = AddImage(root, HudTextures.TechRings(160, 3),
                Fade(color, Mathf.Lerp(0.5f, 0.85f, prominence)), "Rings");
            Stretch(rings.rectTransform);
            rings.raycastTarget = false;

            var knob = AddImage(root, HudTextures.Disc(96), Fade(color, 0.9f), "Knob").rectTransform;
            float knobSize = 64f * Mathf.Lerp(0.85f, 1.15f, prominence);
            knob.anchorMin = knob.anchorMax = new Vector2(0.5f, 0.5f);
            knob.pivot = new Vector2(0.5f, 0.5f);
            knob.sizeDelta = new Vector2(knobSize, knobSize);
            knob.anchoredPosition = Vector2.zero;

            AddDetailPips(root, baseSize, color, level, maxLevel);

            var lbl = AddText(root, Mathf.Lerp(14f, 18f, prominence), Fade(color, Mathf.Lerp(0.75f, 1f, prominence)));
            Anchor(lbl.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f));
            lbl.rectTransform.sizeDelta = new Vector2(baseSize, 22f);
            lbl.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            lbl.text = label;
            lbl.fontStyle = FontStyle.Bold;

            return new JoystickVisual(root, rings, knob, lbl);
        }

        // ---------- shared detail ----------

        /// <summary>Small bright pips around a control's rim, one per level beyond the first — the
        /// "more detailed" half of the spec's "bigger / brighter / more detailed" per-level read, on top
        /// of the size and brightness <see cref="Prominence"/> already drives.</summary>
        private static void AddDetailPips(RectTransform root, float size, Color color, int level, int maxLevel)
        {
            int pips = Mathf.Clamp(level - 1, 0, Mathf.Max(0, maxLevel - 1));
            if (pips <= 0) return;

            float radius = size * 0.5f - 6f;
            for (int i = 0; i < pips; i++)
            {
                // Spread evenly starting at the top, going clockwise — matches the cooldown radial's
                // own fill origin so the two read as one dial.
                float angle = 90f - 360f * (i + 1) / (maxLevel);
                float rad = angle * Mathf.Deg2Rad;
                var pos = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius;

                var pip = AddImage(root, HudTextures.Disc(32), color, $"Pip{i}");
                pip.rectTransform.anchorMin = pip.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                pip.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                pip.rectTransform.sizeDelta = new Vector2(10f, 10f);
                pip.rectTransform.anchoredPosition = pos;
                pip.raycastTarget = false;
            }
        }

        private static Color Fade(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);

        // ---------- small UI helpers (mirrors HudController's own) ----------

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image AddImage(Transform parent, Sprite sprite, Color color, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        private static Text AddText(Transform parent, float size, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = HudFont.Get();
            t.fontSize = Mathf.RoundToInt(size);
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static void Anchor(RectTransform r, Vector2 min, Vector2 max, Vector2 pivot)
        {
            r.anchorMin = min; r.anchorMax = max; r.pivot = pivot;
        }

        private static void Stretch(RectTransform r, float padding = 0f)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(-padding, -padding);
            r.offsetMax = new Vector2(padding, padding);
        }
    }
}
