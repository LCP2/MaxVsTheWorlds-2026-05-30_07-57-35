using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Weapons;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Slice Result screen (YT-31, spec §4.9). Built entirely in code — a dim overlay with the
    /// VICTORY banner, the run's stat card (time, kills, factory destroyed), and NEXT WORLD (MV-687:
    /// advances to the next world once one exists, otherwise reads NO FURTHER WORLDS). Shown by
    /// <see cref="RunTracker"/> once the boss falls and its payoff finishes; it pauses the game
    /// (timeScale 0). Loads instantly (a code-built canvas), meeting the sub-3-second AC.
    ///
    /// MV-427: Victory-only now. Death no longer ends the run (it respawns Max instead, handled by
    /// <see cref="MaxWorlds.Arena.WorldRunner"/>), so this screen — and its old REPLAY CTA, which
    /// reloaded the whole scene — never has a Defeat outcome to show any more.
    /// </summary>
    public sealed class ResultScreen : MonoBehaviour
    {
        private static readonly Color Dim = new Color(0f, 0f, 0f, 0.72f);
        private static readonly Color Panel = new Color(0.08f, 0.10f, 0.14f, 0.96f);
        private static readonly Color Gold = new Color(0.957f, 0.788f, 0.365f);
        private static readonly Color Bone = new Color(0.96f, 0.94f, 0.86f);
        private static readonly Color CoreCyan = new Color(0.31f, 0.86f, 0.98f);

        // MV-1075: the title used to be a fixed 78pt with horizontalOverflow = Overflow, so
        // "WORLD 1 — BACKYARD SAVED" just rendered 1221px wide against a 720px card. ResolveTitle
        // below shrinks it (best-fit, floor 44pt) and, if it still doesn't fit at the floor, wraps it
        // onto two lines at the world name's own " — " (every authored WorldConfig.world reads
        // "World N — Name", per MV-921's own comment on this file).
        private const float TitleMaxFontSize = 78f;
        private const float TitleMinFontSize = 44f;
        private const float TitleTopOffset = 60f;
        private const float TitleSingleLineBoxHeight = 90f;
        private const float TitleLineGap = 8f;

        /// <summary>Build and show the screen for a finished (Victory) run. Pauses the game.</summary>
        public void Show(RunStats stats)
        {
            EnsureEventSystem();
            BuildCanvas(stats);
            Time.timeScale = 0f; // freeze the run behind the card
            ModalFrameRateGate.Enter();   // MV-574: idle the frame rate — this screen never closes in the slice
        }

        private void BuildCanvas(RunStats stats)
        {
            var go = new GameObject("Result Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200; // above the HUD (100)
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)go.transform;

            var dim = AddImage(root, HudTextures.Solid(), Dim, "Dim");
            Stretch(dim.rectTransform);

            var panel = AddImage(root, HudTextures.RoundedBox(48, 0.12f), Panel, "Panel");
            panel.type = Image.Type.Sliced;
            Center(panel.rectTransform, ResultLayout.PanelWidth, ResultLayout.PanelHeight);

            // MV-1075: nothing from the live world may show through the panel behind the title — the
            // panel is a translucent image on a screen-space overlay canvas, so a world-space
            // WorldHealthBar (Max's, Sentinels', robots') still draws straight through it otherwise.
            WorldHealthBar.SetAllForceHidden(true);

            // MV-427: the only outcome that ever reaches this screen now is Victory — death respawns
            // Max instead of ending the run, so there is no DEFEAT banner/near-miss/REPLAY branch left.
            // MV-841: the banner itself now says which world was saved (Lee: "needs to say 'World
            // [name] Saved'") rather than a bare "VICTORY" plus a separate "{map} cleared" subtitle.
            var title = AddText(panel.rectTransform, TitleMaxFontSize, Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            // MV-687: reads the loaded world's own name, not a "Backyard"-literal — a Victory in
            // World 2 (or beyond) must not still read as if it happened in the Backyard. MV-921: every
            // authored WorldConfig.world already reads "World N — Name" (e.g. "World 1 — Backyard"), so
            // a hardcoded "WORLD " prefix here doubled up into "WORLD WORLD 1 — BACKYARD SAVED".
            var backyardPath = FindFirstObjectByType<BackyardPath>();
            string worldName = backyardPath != null && backyardPath.Map != null ? backyardPath.Map.name : "World";
            string fullTitle = $"{worldName.ToUpperInvariant()} SAVED";

            float titleBoxWidth = ResultLayout.ContentWidth;
            float titleBoxHeight = ResolveTitle(title, fullTitle, titleBoxWidth);
            Top(title.rectTransform, 0f, -TitleTopOffset, titleBoxWidth, titleBoxHeight);

            // The title only grows DOWN into the card when it needs a second line — give it back the
            // room it took, as extra panel height, rather than letting the wrapped line run into the
            // stat rows below it.
            float extraTitleHeight = Mathf.Max(0f, titleBoxHeight - TitleSingleLineBoxHeight);
            if (extraTitleHeight > 0f)
                panel.rectTransform.sizeDelta += new Vector2(0f, extraTitleHeight);

            // Stat rows — the whole world's tally from its first entry to this victory, across any
            // deaths along the way (MV-841: RunProgressState/DeathRunState both checkpoint and
            // restore across a resume, so these never silently reset to zero mid-world).
            float y = -170f - extraTitleHeight;
            AddStatRow(panel.rectTransform, "TIME", RunStats.FormatTime(stats.Elapsed), ref y);
            AddStatRow(panel.rectTransform, "DEATHS", DeathRunState.DeathsTaken.ToString(), ref y);
            AddStatRow(panel.rectTransform, "ROBOTS DESTROYED", stats.Kills.ToString(), ref y);
            AddStatRow(panel.rectTransform, "FACTORIES DESTROYED", stats.FactoriesDestroyed.ToString(), ref y);

            // One CTA now that REPLAY is gone (MV-427) — centred on the panel rather than the old
            // two-button RightButtonX slot. MV-687: live once this Victory actually advanced the
            // active profile's WorldIndex (RunTracker captured that in stats.AdvancesWorld before
            // RecordResult performed it); otherwise it reads NO FURTHER WORLDS and stays disabled.
            bool canAdvance = stats.AdvancesWorld;
            var nextBtn = AddButton(panel.rectTransform, canAdvance ? "NEXT WORLD" : "NO FURTHER WORLDS",
                new Color(0.3f, 0.34f, 0.4f), canAdvance, canAdvance ? (UnityEngine.Events.UnityAction)RunFlow.StartNextWorld : null);
            Bottom(nextBtn, 0f, 40f, ResultLayout.ButtonWidth, ResultLayout.ButtonHeight);

            // MV-698/MV-1074: World 1's finale — collecting the Weapon Core morphs PRIMARY on the spot,
            // during the finale's own WEAPON TAKEN beat (MV-1079), not on next opening THE RIG, which is
            // what this line used to say. MV-1079: by the time this card shows, that beat has already
            // applied the morph, so WeaponSystemState.ActivePrimary already reads the new primary — this
            // line names it by the same WeaponCatalog long name the beat's own banner used, replacing the
            // vaguer "READY IN THE NEXT WORLD" wording.
            if (stats.WeaponCoreGranted)
            {
                var corePrompt = AddText(panel.rectTransform, 22f, CoreCyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                Bottom(corePrompt.rectTransform, 0f, 40f + ResultLayout.ButtonHeight + 14f, ResultLayout.ButtonWidth, 28f);
                corePrompt.text = "NEW WEAPON: " + WeaponCatalog.DisplayName(WeaponSystemState.ActivePrimary);
            }
        }

        /// <summary>
        /// MV-1075: picks the title's final text and font size, and returns the box height it needs.
        /// Best-fit shrink first (78pt down to the 44pt floor, measured against <paramref
        /// name="maxWidth"/> via <see cref="Text.preferredWidth"/> — <c>resizeTextForBestFit</c>'s own
        /// search, <c>TextGenerator.fontSizeUsedForBestFit</c>, is a documented no-op under
        /// <c>-batchmode -nographics</c> (MV-585/MV-593), so this mirrors that search by hand instead).
        /// If even the floor still overflows as one line, wraps onto two lines at the world name's own
        /// " — " (every authored <c>WorldConfig.world</c> reads "World N — Name") and re-runs the same
        /// search against whichever of the two lines is wider.
        /// </summary>
        private static float ResolveTitle(Text title, string fullText, float maxWidth)
        {
            for (int size = (int)TitleMaxFontSize; size >= TitleMinFontSize; size--)
            {
                title.fontSize = size;
                title.text = fullText;
                if (title.preferredWidth <= maxWidth)
                    return Mathf.Max(TitleSingleLineBoxHeight, title.preferredHeight);
            }

            // MV600AsciiOnlyPlayerFacingTextTests scans Runtime/UI/*.cs string literals for a literal
            // non-ASCII byte; this delimiter is never drawn (it only splits the world name apart), so
            // it is built from the codepoint below rather than a literal character in a string
            // literal, to keep that scan honest about which strings are actually player-facing.
            string dash = ((char)0x2014).ToString();
            string[] parts = fullText.Split(new[] { " " + dash + " " }, 2, System.StringSplitOptions.None);
            if (parts.Length != 2)
            {
                // No dash to wrap at (never true for an authored world name) — ship the floor size as
                // a single line rather than crash.
                title.fontSize = (int)TitleMinFontSize;
                title.text = fullText;
                return Mathf.Max(TitleSingleLineBoxHeight, title.preferredHeight);
            }

            string line1 = parts[0];
            string line2 = parts[1];
            for (int size = (int)TitleMaxFontSize; size >= TitleMinFontSize; size--)
            {
                title.fontSize = size;
                title.text = line1;
                float width1 = title.preferredWidth;
                title.text = line2;
                float width2 = title.preferredWidth;
                if (Mathf.Max(width1, width2) <= maxWidth)
                {
                    title.text = line1 + "\n" + line2;
                    return title.preferredHeight + TitleLineGap;
                }
            }

            // The floor still overflows both lines (an unusually long authored name) — ship the floor
            // size anyway; a slightly tight two-line card beats a one-line card hanging off the panel.
            title.fontSize = (int)TitleMinFontSize;
            title.text = line1 + "\n" + line2;
            return title.preferredHeight + TitleLineGap;
        }

        private void AddStatRow(RectTransform panel, string label, string value, ref float y)
        {
            var l = AddText(panel, 24f, new Color(1, 1, 1, 0.7f), TextAnchor.MiddleLeft, FontStyle.Normal);
            Top(l.rectTransform, ResultLayout.StatLabelX, y, ResultLayout.StatCellWidth, 34f);
            l.text = label;
            var v = AddText(panel, 26f, Bone, TextAnchor.MiddleRight, FontStyle.Bold);
            Top(v.rectTransform, ResultLayout.StatValueX, y, ResultLayout.StatCellWidth, 34f);
            v.text = value;
            y -= 42f;
        }

        // --- interaction plumbing ---

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
            var module = es.AddComponent<InputSystemUIInputModule>();
            // Wire the default point/click/navigate actions so buttons are clickable in a
            // project using the new Input System (no editor setup).
            module.AssignDefaultActions();
        }

        private RectTransform AddButton(RectTransform parent, string label, Color color, bool interactable,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = HudTextures.RoundedBox(32, 0.3f);
            img.type = Image.Type.Sliced;
            img.color = color;
            var btn = go.GetComponent<Button>();
            btn.interactable = interactable;
            if (onClick != null) btn.onClick.AddListener(onClick);

            var t = AddText((RectTransform)go.transform, 26f, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(t.rectTransform);
            t.text = label;
            return (RectTransform)go.transform;
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

        private static Text AddText(Transform parent, float size, Color color, TextAnchor align, FontStyle style)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = HudFont.Get();
            t.fontSize = Mathf.RoundToInt(size);
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        // --- layout helpers ---

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        }

        private static void Center(RectTransform r, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = Vector2.zero;
        }

        private static void Top(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = new Vector2(x, y);
        }

        private static void Bottom(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = new Vector2(x, y);
        }
    }
}
