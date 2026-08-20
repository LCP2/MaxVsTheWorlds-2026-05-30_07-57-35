# CLAUDE.md — MAX vs THE WORLDS (Unity mobile game)

Follows **The Codynamics Development Standard** (Confluence space **DM**). Read the For Claude pages first, every chat.
Landing: https://codynamics.atlassian.net/wiki/spaces/DM/pages/32374806/For+Claude

## The money rule (non-negotiable)
Never poll on a timer, watch progress on a schedule, or run QA/deploy from a chat session. CI does the
mechanical work; the worker loop checks Jira with a free REST poll. Tokens buy judgement only — shaping,
review, debugging. Releases ship from a **tag push**, never from a chat driving the browser.

## Folders
- **Code (this repo):** `C:\Dev\MaxVsTheWorlds` — git remote `LCP2/MaxVsTheWorlds-2026-05-30_07-57-35`. Local disk only; never sync to OneDrive.
- **Worker infra:** `C:\Dev\MAx CCs` — `run-cc.ps1` runs a single worker with one checkout, `cc-web`. `start-gameplay.bat` / `start-art.bat` are retired (`.retired`).
- **Secrets:** GitHub Actions secrets, plus one `ATLASSIAN_API_TOKEN` env var on the dev PC (set once via `setx`). Never in the repo.

## Parameters
- Jira: project **YT**.
- Build-ready label: **`needs-cc-web`**. Human-block `needs-lee`.
- Verify / QA: `cc-verify.bat` (and `cc-verify-playmode.bat` for play-mode tests). Read the real exit code; don't proceed on red.
- Worker contract: **`CC_AUTONOMY.md`**. (`CC_AUTONOMY_ART.md` is retired.)
- Deploy: **TestFlight** via Fastlane (`ios-testflight.yml`).

## Integration branch — MAX uses `main` (by design)
The standard's default is a two-branch split (workers on `staging`, promote-only `main`). **MAX intentionally
integrates on `main`:** workers push `main`, `build.yml` runs the full QA on every push, and **nothing ships
until Lee pushes a release tag** — so the tag is the release lock and a separate `staging` branch would add no
safety here. This is MAX's documented integration branch, not a deviation to "fix". A broken worker commit
fails CI visibly but can never reach testers.

## Releases
- Cut a release by pushing a **git tag `vX.Y.Z`** — this triggers `ios-testflight.yml`, which builds and
  uploads to TestFlight. **The tag IS the version:** `v0.4.36` ships as marketing version `0.4.36` (the
  Fastfile reads the tag). No browser, no "Run workflow" button.
- Continue the TestFlight sequence (currently `0.4.x`): `v0.4.36`, `v0.4.37`, …; bump the middle number when a
  new milestone starts (and keep `BuildStamp.MilestoneVersion` in the game code in sync).
- Track what's in a release with a **per-release label** `v<version>` on every ticket in it — filter the
  board's built-in **Label** chip. No Jira Versions, no quick filters.

## Never (worker)
- Never edit CI / workflow / signing files (`.github/workflows/`, `fastlane/`). If a ticket needs that, set it
  `BLOCKED` and add `needs-lee`.
- Never enter credentials, accept store agreements, or run a deploy. Shipping is Lee's tag push, full stop.
