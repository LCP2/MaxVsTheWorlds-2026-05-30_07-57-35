# MAX vs. THE WORLDS — Runbook

The durable operational notes for the YT-game. Read this when something breaks or you've forgotten how a piece works. Larger durable design context lives in the Confluence space; this file is for *operating* the build.

## Overview

- **What.** Single-player iOS roguelike action-adventure for ages 12–20. Top-down twin-stick combat, big-arena Worlds (~20× viewport) with sub-zones + destructible robot factories + a boss. 13 worlds total (6 at launch). Currently in Phase B — the Backyard vertical slice.
- **Status.** Phase B in progress. YT-32 (scaffold) Done. YT-33 (camera) coded + pending verification.
- **Owner.** Lee.

## Stack & versions

- **Engine:** Unity 6 LTS, currently 6000.4.9f1.
- **Render pipeline:** URP 3D low-poly.
- **Scripting:** IL2CPP, .NET Standard 2.1, ARM64 (iOS target deferred).
- **Input:** Unity Input System (New).
- **Packages pinned:** Cinemachine 3.x; ProBuilder *deferred* (Unity 6.4 incompatibility — revisit at YT-38).
- **Test framework:** Unity Test Framework, EditMode + PlayMode assemblies.

## Repo layout

- `_Project/` — everything we author.
  - `Art/`, `Audio/`, `Code/Runtime/{Core,Player,Combat,Enemies,Factories,CameraRig,UI,Arena}/`, `Code/Editor/`, `Code/Tests/{EditMode,PlayMode}/`, `Prefabs/`, `Scenes/`, `Settings/`, `ScriptableObjects/`.
- `ThirdParty/` — imported kits (Synty/Kenney etc.).
- `Plugins/`.
- `Builds/` — gitignored. cc-verify smoke output goes to `Builds/cc-verify/`.
- `Logs/` — gitignored. Verify and build logs.
- `CC_AUTONOMY.md` — autonomy contract (CC reads first).
- `cc-verify.bat` — verify entry point.
- `docs/RUNBOOK.md` — this file.

## Day-2 operations

### Starting a CC session
- `cd C:\dev\MaxVsTheWorlds`, launch Claude Code.
- First message: `Follow CC_AUTONOMY.md.`
- CC queries `project = YT AND labels = needs-cc`. If empty during transition, CC falls back to numeric YT-13 child order.

### Verifying a change
- From the repo root: `cc-verify.bat`. Pre-req: `UNITY_PATH` env var pointing at Unity.exe.
- Pass = exit 0. Logs land under `Logs/`.
- Expected runtime: ~60–90s.

### Building locally (interactive)
- Open Unity, **File → Build Profiles** → Windows. Build target should be Win64; first scene `_Project/Scenes/Bootstrap.unity`. Output to `Builds/manual/`.

### Building for iOS (deferred)
- Requires Mac + Xcode + iOS Build Support module + Apple Developer membership. Carry-over from Phase A.

## Decisions log

- `2026-05-23` — Engine locked = **Unity 6 LTS** (YT-23 Done). Committed to the standing recommendation rather than a 3-engine bake-off; saves ~4 weeks. Rationale on YT-23.
- `2026-05-23` — Phase B tight-slice scope locked. One of everything (sub-zone path, gadget, enemy, factory, boss). Procgen, full gadget set, Workbench, charms → deferred to M2 Alpha (YT-14).
- `2026-05-23` — iOS device build deferred (no Mac yet); Windows standalone of `Bootstrap.unity` at 60fps is the substitute acceptance for any "device smoke" AC in Phase B.
- `2026-05-23` — ProBuilder 6.0.4 dropped from `manifest.json` due to incompatibility with Unity 6.4 (`ContainerWindow.SetAlpha`). Re-add at YT-38, pinned to a Unity 6.4-compatible version, or substitute with primitives / a free kit.

## Known gotchas

- **OneDrive corrupts Unity `Library/`** — keep the live project under `C:\dev\`, never OneDrive/Dropbox/iCloud. Docs are fine in OneDrive; the project is not.
- **Unity Hub "Manage" dropdown won't show *Add modules* for editors it didn't install itself.** If you ever need to add iOS Build Support to a "located" install, you have to Remove from Hub + delete the editor folder + install fresh.
- **Unity Hub black screen on splash** — usually GPU rendering. Launch with `"Unity Hub.exe" --disable-gpu`. If that fails, wipe `%AppData%\UnityHub` (preserves projects and editor installs).
- **ProBuilder vs Unity 6.4** — see decisions log; check compatibility before re-adding.

## Glossary

- **Slice / tight slice** — Phase B's deliberately-thin vertical slice: one Backyard sub-zone path, one gadget (Water Blaster), one enemy, one factory (Mower Hutch), one boss (Big Bermuda slice version), slice HUD + Result.
- **Curator** — final boss (sleek faceless figure, gold halo ring, single violet portal-light). Locked design.
- **Workbench** — in-run gadget fusion station. SK shop re-flavour.
- **The Void** — final world, where the Curator lives.

## Pointers

- **Confluence space:** [Games](https://codynamics.atlassian.net/wiki/spaces/Games)
- **Phase B Vertical Slice Spec:** [page 12058680](https://codynamics.atlassian.net/wiki/spaces/Games/pages/12058680)
- **Jira project:** [YT](https://codynamics.atlassian.net/browse/YT)
- **Active epic:** [YT-13 M1 Vertical Slice](https://codynamics.atlassian.net/browse/YT-13)
- **CC autonomy contract:** `CC_AUTONOMY.md` at repo root.
- **Cross-project Ops Confluence:** OOP space → Operations (id 13926402).
- **Session handoff (full context):** `Games/MAX-vs-THE-WORLDS_SESSION-HANDOFF.md` in the workspace folder.
