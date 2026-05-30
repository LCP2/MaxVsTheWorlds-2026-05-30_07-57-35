# CC Autonomy Contract — MAX vs. THE WORLDS (YT-game)

> CC's kickoff prompt is *"Follow `CC_AUTONOMY.md`."* Everything below.

## Variables

- **Project key:** `YT`
- **Project slug:** `yt-game`
- **Repo path:** `C:\dev\MaxVsTheWorlds`
- **Spec root:** Confluence space **Games** — Phase B Vertical Slice Spec, page id `12058680` ([link](https://codynamics.atlassian.net/wiki/spaces/Games/pages/12058680)).
- **Active epic:** `YT-13` (M1 Vertical Slice).
- **Stack:** Unity 6 LTS (6000.4.x), URP, 3D low-poly, target iOS (build deferred until Mac available; validate via Windows standalone).
- **Verify script:** `./cc-verify.bat`.

## Claim

On every start, query Jira:

```
project = YT AND labels = needs-cc AND statusCategory != Done ORDER BY priority DESC, key ASC
```

Pick the top ticket. Claim by adding `cc-active`. If none: report *"ready, nothing to claim"* and stop.

> **Transition note:** YT-32 through YT-39 were created before this vocabulary existed and may not carry `needs-cc`. If the labelled queue is empty but YT-13 has open Backlog children, fall back to working them in numeric order (33 → 34 → 35 → 36 → 30 → 37 → 38 → 27 → 31 → 39) until the labels are backfilled.

## Work

Read the ticket description, the Phase B spec (12058680), and any linked Confluence pages. Branch:

```
git checkout -b feat/YT-XX-short-slug
```

Implement to the AC — and nothing beyond. Greybox + free-kit art only (no AI art until Phase C). Add EditMode/PlayMode tests for any non-trivial logic (movement maths, damage calc, factory spawn/destroy, win/lose).

## Self-verify

```
./cc-verify.bat
```

Captures: editor compile, EditMode tests, headless Windows standalone build, log assertion that `targetFrameRate = 60` and `VSyncCount = 0`. Exit 0 = pass.

If it fails on a transient/flake, retry once. If it fails structurally, stop and report.

## Decide

- All AC pass AND no `human-judgment` AC → transition Done; drop `cc-active`; comment summary + PR link; loop.
- `human-judgment` AC remaining → stop; drop `cc-active`; set `needs-lee`; comment exact steps for Lee in Unity (what scene, what to Play, what to look for, what to reply).
- Self-verify failed → drop `cc-active`; `needs-cc` if flake, `needs-spec` if structural.
- Guardrail trip → stop; set `needs-lee`; ask. Specific trips for this project: any engine version change; adding a Unity package not already in `manifest.json`; turning on AI-art generation; expanding a ticket beyond its tight-slice scope.
- Physical-world blocker → stop; `blocked-<reason>` + `needs-lee`. Known examples: `blocked-mac` (iOS device build — carry-over, not a present blocker since Windows standalone is the substitute); `blocked-install` (Lee needs to install something).

## Etiquette

- **Timestamp every response.** Begin each chat reply with a wall-clock prefix in the format `[YYYY-MM-DD HH:MM AEST] ` read from the OS clock. Example: `[2026-05-30 14:23 AEST] Starting on YT-34.` Non-negotiable — Lee uses it to track when work happened across long async sessions.
- Never push to `main`. PRs only. Squash-merge after review.
- Commit messages prefixed with the ticket key: `YT-XX: imperative summary`.
- Jira comments: concise. What was done, how to verify, what's next.
- Don't author docs/READMEs unless the ticket asks.
- If a spec is ambiguous in a way that affects implementation: stop, `needs-spec`, ask.
- Before claiming any verification PASS, **run the verify command and read its real exit code**. Don't infer success from absence of errors.

## Project-specific notes

- **Tight-slice discipline.** Phase B is one Backyard sub-zone path, one gadget (Water Blaster), one enemy, one factory (Mower Hutch), one boss (Big Bermuda — slice version), slice HUD + Result. If anything starts growing, cut to the slice version, note in comments, move on.
- **Camera is fixed angled top-down at ~72°.** Don't add free-look, orbit, or portrait orientation.
- **Greybox + free-kit only** until Phase B's exit verdict. AI art is Phase C, after the loop is proven.
- **Windows standalone is the substitute** for the iPhone smoke build (acceptance per Phase B spec §1 sanctioned deviation). Don't park YT-32 or any Phase B ticket waiting for a Mac — that's a carry-over note, not a present blocker.

## Stack-specific notes (Unity 6 LTS)

- **URP 3D low-poly.** Linear colour space (set at scaffold; don't toggle).
- **IL2CPP + ARM64 + iOS 15** Player settings — set even though iOS device build is deferred.
- **Input System (New)** — not "Both" unless we explicitly need legacy.
- **Cinemachine 3.x** for the camera rig (YT-33).
- **ProBuilder** — deferred. Was incompatible with Unity 6.4 (`ContainerWindow.SetAlpha` removed). Re-add when reaching YT-38 (Backyard greybox), pinned to a 6.4-compatible version, or substitute with primitives / a free kit.
- **TextMeshPro** ships inside `com.unity.ugui` in Unity 6 — no separate install.
- **Asmdef layout:** root namespace `MaxWorlds`. Assemblies: `MaxWorlds.Core`, `MaxWorlds.Gameplay`, `MaxWorlds.Editor`, `MaxWorlds.Tests.EditMode`, `MaxWorlds.Tests.PlayMode`. Don't over-split for the slice.
- **`Application.targetFrameRate = 60` and `QualitySettings.vSyncCount = 0`** — both set in `Bootstrap.cs` Awake; don't change without reason.
- **Repo location:** `C:\dev\MaxVsTheWorlds` — **not** in OneDrive (Unity `Library/` corrupts under sync).
- **Build pipeline:** scenes added in `_Project/Scenes/`. Bootstrap is scene 0. Verify script builds Windows standalone to `Builds/cc-verify/` (gitignored).
