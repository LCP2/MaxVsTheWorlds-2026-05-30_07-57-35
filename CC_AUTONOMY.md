# CC Autonomy Contract — MAX vs. THE WORLDS (MV-game)

> CC's kickoff prompt is *"Follow `CC_AUTONOMY.md`."* Everything below.

## NEVER IDLE — the standing rule (read before anything else)

The moment you finish a ticket (merged / handed off / proposal posted), **immediately pick the next actionable `needs-cc` ticket** (highest priority first, then key ascending) and keep going **without stopping**. Re-check the backlog after every completion.

Only STOP and wait for Lee when one of these is true:

- (a) there are genuinely **no** actionable `needs-cc` tickets left in the backlog,
- (b) the only remaining items are `needs-lee` / explicitly blocked on a Lee decision, or
- (c) you hit a blocker you cannot resolve yourself.

When you post something for Lee to review (a concept sheet, a proposal, any `needs-lee` handoff), **do not stop** — move straight on to the next actionable ticket while you wait for his reply. The safety contract is unchanged: `cc-verify` green before merge, auto-merge on green, CI-on-`main` as the net, git/merge hygiene, drop `cc-active`. This rule removes only the "stop and idle between tickets" behaviour.


## Design standard — READ FIRST, applies to every ticket

Before claiming or working any ticket, read the **Design Principles & Craft Bible**: https://codynamics.atlassian.net/wiki/spaces/Games/pages/25002019

It is the canonical craft standard for MAX vs THE WORLDS. Every change you ship must comply with it. If a ticket's acceptance criteria conflict with the Craft Bible, flag it in a ticket comment instead of shipping. When principles tension against each other, the tie-breaker order is: readability > game feel > visual richness. Non-negotiable on every build: 60fps on iOS/WebGL, and readable on a 6-inch screen.
## Variables

- **Project key:** `MV`
- **Project slug:** `mv-game`
- **Repo path:** `C:\dev\MaxVsTheWorlds`
- **Spec root:** Confluence space **Games** — Phase B Vertical Slice Spec, page id `12058680` ([link](https://codynamics.atlassian.net/wiki/spaces/Games/pages/12058680)).
- **Active epic:** `MV-13` (M1 Vertical Slice).
- **Stack:** Unity 6 LTS (6000.4.x), URP, 3D low-poly, target iOS (build deferred until Mac available; validate via Windows standalone).
- **Verify script:** `./cc-verify.bat`.

## Claim

**On every start, run the git-merge hygiene step first:** `sh scripts/setup-git-merge.sh`. It's idempotent and keeps Unity YAML (`.unity`/`.prefab`/`.asset`) merging **headless** — git's 3-way text merge writes conflict markers instead of the Smart-Merge GUI that once blocked an autonomous merge (MV-103; see `docs/GIT_MERGE_SETUP.md`). The art-stream contract must carry the same step.

**Jira cloudId is `5c3e53d8-fa09-4c72-a99f-9adf958e5fb9`** (site `https://codynamics.atlassian.net`, project key `MV`) — use it directly on every Jira MCP call, do NOT call `getAccessibleAtlassianResources` to rediscover it. Also recorded in `memory/jira_cloud_id.md`.

**Never leave a ticket In Progress without a commit; and reclaim your own orphaned in-flight tickets before pulling new work.** Before querying "Ready for Dev", first check for a ticket carrying `needs-cc-web` that is in "Developing" with no commit on `main` referencing it (`git log --grep=MV-XX`). There is a single-instance mutex, so any such ticket is by definition yours-but-interrupted, not another worker's — RESUME it rather than pulling a fresh one. Only fall through to a fresh "Ready for Dev" pull when no orphaned in-flight ticket exists. (This exists because MV-189 and MV-186 were stranded In Progress by runs that ended without committing and had to be hand-reset.)

Then query Jira:

```
project = MV AND labels = needs-cc-web AND statusCategory != Done ORDER BY priority DESC, key ASC
```

Pick the top ticket. Claim by adding `cc-active`. If none: report *"ready, nothing to claim"* and stop.

> **Transition note:** MV-32 through MV-39 were created before this vocabulary existed and may not carry `needs-cc`. If the labelled queue is empty but MV-13 has open Backlog children, fall back to working them in numeric order (33 → 34 → 35 → 36 → 30 → 37 → 38 → 27 → 31 → 39) until the labels are backfilled.

## Work

**Design image first.** Check `C:\Dev\MaxVsTheWorlds-Images` for a file named after this ticket (`<KEY>.png` / `.jpg` / `.jpeg` / `.webp`, e.g. `MV-123.png`). If one exists, **Read it** and build to match that design — the image is the source of truth for layout, framing, proportion, colour and readability, ahead of any prose in the ticket. If the ticket references a design image that is not in that folder, treat it as a missing asset: stop, set `needs-lee`, and say what is missing. You have full read/write to the folder, so you may also save generated or annotated images back there, named by the ticket key.

Read the ticket description, the Phase B spec (12058680), and any linked Confluence pages. Branch:

```
git checkout -b feat/MV-XX-short-slug
```

Implement to the AC — and nothing beyond. Greybox + free-kit art only (no AI art until Phase C). Add EditMode/PlayMode tests for any non-trivial logic (movement maths, damage calc, factory spawn/destroy, win/lose).

## Self-verify

```
./cc-verify.bat
```

Captures: editor compile, EditMode tests, headless Windows standalone build, log assertion that `targetFrameRate = 60` and `VSyncCount = 0`. Exit 0 = pass.

**Run `cc-verify.bat` synchronously in the foreground and WAIT for it to finish within this turn (use a long bash timeout).** You run in one-shot `-p` mode — there are NO background notifications, so if the build gets backgrounded and you end your turn to "wait for it", the run ends with no commit and the ticket stalls. **This is why MAX tickets have not been completing.** Never pipe `cc-verify` through `| tail`; read its real exit code directly.

If it fails on a transient/flake, retry once. If it fails structurally, stop and report.

## Play-check — the playability gate (`cc-verify` is NOT enough), and why the worker never waits for it

`cc-verify` proves the build compiles, EditMode tests pass, it builds headlessly, and it holds 60fps. It does **NOT** prove the game is playable — an unplayable opening passes it clean. So before any **TestFlight / store build**, the accumulated pile of merged tickets must ALSO pass a **play-check**:

- **Boot the actual build and play the first ~60 seconds.** Confirm the core loop works: you can spawn, move, fire, deal/take damage as recent tickets imply, and reach the menu — with no blocker in the opening.
- Run it against the live **WebGL Pages build** (`build.yml` → the "play the latest" link) in a browser.
- **Never cut a TestFlight / store build without a green play-check on that exact build.** A green `cc-verify` alone is **not** authorisation to release a beta. iOS is a manual, chat-triggered release (KB → *Manual actions*); before triggering `ios-testflight`, confirm the play-check passed on the build being shipped. An unplayable build is never handed to testers.

**This is not a per-ticket gate for the worker.** The worker's own build has not deployed yet when its turn ends — it can only ever see a previous, unrelated deploy — so checking the live WebGL Pages build as a precondition for finishing *this* ticket is structurally impossible and was the root cause of a push/cancel loop (MV-346). The worker never waits for, watches, or polls a CI run, and never schedules a wakeup to check one. `cc-verify` green locally is the worker's complete gate; the play-check happens later, separately, against the accumulated pile before a TestFlight ship.

## Decide

- All AC pass AND no `human-judgment` AC → transition **In PDN**; drop `cc-active`; comment summary + PR link; **do not wait for or check the CI/deploy run** — loop straight to the next ticket.
- `human-judgment` AC remaining → stop; drop `cc-active`; set `needs-lee`; comment exact steps for Lee in Unity (what scene, what to Play, what to look for, what to reply).
- Self-verify failed → drop `cc-active`; `needs-cc` if flake, `needs-spec` if structural.
- Guardrail trip → stop; set `needs-lee`; ask. Specific trips for this project: any engine version change; adding a Unity package not already in `manifest.json`; turning on AI-art generation; expanding a ticket beyond its tight-slice scope.
- Physical-world blocker → stop; `blocked-<reason>` + `needs-lee`. Known examples: `blocked-mac` (iOS device build — carry-over, not a present blocker since Windows standalone is the substitute); `blocked-install` (Lee needs to install something).

## Etiquette

- **Timestamp every response.** Begin each chat reply with a wall-clock prefix in the format `[YYYY-MM-DD HH:MM AEST] ` read from the OS clock. Example: `[2026-05-30 14:23 AEST] Starting on MV-34.` Non-negotiable — Lee uses it to track when work happened across long async sessions.
- **Auto-merge on green — do NOT wait for Lee.** Work on a `feat/MV-XX-*` branch and open a PR for the record, but once `cc-verify` passes you squash-merge it to `main` yourself (`gh pr merge --squash --delete-branch <pr>`, or a plain git squash-merge + push if `gh` is unavailable). Do not park a verified ticket waiting for a human review/merge. The CI build that runs on `main` after the merge is the safety net — if it goes red, stop and set `needs-lee`. Hold for a human only when an AC is tagged `human-judgment` or a guardrail trips.
- Commit messages prefixed with the ticket key: `MV-XX: imperative summary`.
- Jira comments: concise. What was done, how to verify, what's next.
- Don't author docs/READMEs unless the ticket asks.
- If a spec is ambiguous in a way that affects implementation: stop, `needs-spec`, ask.
- Before claiming any verification PASS, **run the verify command and read its real exit code**. Don't infer success from absence of errors.

## Project-specific notes

- **Tight-slice discipline.** Phase B is one Backyard sub-zone path, one gadget (Water Blaster), one enemy, one factory (Mower Hutch), one boss (Big Bermuda — slice version), slice HUD + Result. If anything starts growing, cut to the slice version, note in comments, move on.
- **Camera is fixed angled top-down at ~72°.** Don't add free-look, orbit, or portrait orientation.
- **Greybox + free-kit only** until Phase B's exit verdict. AI art is Phase C, after the loop is proven.
- **Windows standalone is the substitute** for the iPhone smoke build (acceptance per Phase B spec §1 sanctioned deviation). Don't park MV-32 or any Phase B ticket waiting for a Mac — that's a carry-over note, not a present blocker.

## Stack-specific notes (Unity 6 LTS)

- **URP 3D low-poly.** Linear colour space (set at scaffold; don't toggle).
- **IL2CPP + ARM64 + iOS 15** Player settings — set even though iOS device build is deferred.
- **Input System (New)** — not "Both" unless we explicitly need legacy.
- **Cinemachine 3.x** for the camera rig (MV-33).
- **ProBuilder** — deferred. Was incompatible with Unity 6.4 (`ContainerWindow.SetAlpha` removed). Re-add when reaching MV-38 (Backyard greybox), pinned to a 6.4-compatible version, or substitute with primitives / a free kit.
- **TextMeshPro** ships inside `com.unity.ugui` in Unity 6 — no separate install.
- **Asmdef layout:** root namespace `MaxWorlds`. Assemblies: `MaxWorlds.Core`, `MaxWorlds.Gameplay`, `MaxWorlds.Editor`, `MaxWorlds.Tests.EditMode`, `MaxWorlds.Tests.PlayMode`. Don't over-split for the slice.
- **`Application.targetFrameRate = 60` and `QualitySettings.vSyncCount = 0`** — both set in `Bootstrap.cs` Awake; don't change without reason.
- **Repo location:** `C:\dev\MaxVsTheWorlds` — **not** in OneDrive (Unity `Library/` corrupts under sync).
- **Build pipeline / boot scene (IMPORTANT):** The WebGL build boots **scene 0** of `ProjectSettings/EditorBuildSettings.asset`, so scene 0 MUST be the current *playable* scene (currently `Assets/_Project/Scenes/Backyard_Slice.unity`) — NOT the empty `Bootstrap.unity` smoke-test scene. Whenever you add or reorder scenes, keep the playable scene at index 0 (or make `Bootstrap` load it via `SceneManager`), and after any change confirm the deployed WebGL link shows the playable scene, not a bare cube. Verify script builds Windows standalone to `Builds/cc-verify/` (gitignored).
- **Scenes & wiring - code-driven only.** Follow `docs/CODE_DRIVEN_SCENES.md`: assemble scenes and prefabs in code (Bootstrap + ScriptableObjects), never via manual Inspector wiring. A feature that needs hand-wiring in the editor to run is not done - it must build headlessly in CI and show up on the WebGL play link.
