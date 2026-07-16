# CC Autonomy Contract — ART / TECH-ART STREAM (MAX vs. THE WORLDS)

> Kickoff: `Follow CC_AUTONOMY_ART.md.`
> This is the SECOND, parallel workstream — code-side technical art. The gameplay stream runs separately under `CC_AUTONOMY.md`. Do NOT do gameplay-logic work; that's the other stream's job.

## Design standard — READ FIRST, applies to every ticket

Before claiming or working any ticket, read the **Design Principles & Craft Bible**: https://codynamics.atlassian.net/wiki/spaces/Games/pages/25002019

It is the canonical craft standard for MAX vs THE WORLDS. Every change you ship must comply with it — the Look & Feel and Juice sections are especially yours. If a ticket's acceptance criteria conflict with the Craft Bible, flag it in a ticket comment instead of shipping. When principles tension against each other, the tie-breaker order is: readability > game feel > visual richness. Non-negotiable on every build: 60fps on iOS/WebGL, and readable on a 6-inch screen.

## Shared rules
Follow ALL of `CC_AUTONOMY.md` for conventions: auto-merge on green (squash-merge your own verified branch to `main`, don't wait for Lee), run `cc-verify` before merge, the code-driven scene/prefab convention (`docs/CODE_DRIVEN_SCENES.md`), the boot-scene rule, commit/Jira etiquette, and "run the verify command — never infer success."

## This stream's queue (DIFFERENT from the gameplay one)

**On every start, first run the git-merge hygiene step:** `sh scripts/setup-git-merge.sh` (idempotent). It keeps Unity YAML (`.unity`/`.prefab`/`.asset`) merging **headless** — git's 3-way text merge writes conflict markers instead of the Smart-Merge GUI that once blocked an autonomous merge (YT-103; see `docs/GIT_MERGE_SETUP.md`).

On every start, query Jira:

```
project = YT AND labels = needs-cc-art AND statusCategory != Done ORDER BY priority DESC, key ASC
```

Claim the top ticket (add label `cc-active`). If the `needs-cc-art` queue is empty, STOP and report — do NOT fall back to gameplay (`needs-cc`) tickets. Those belong to the other stream.

### Handing a ticket back — REMOVE `needs-cc-art`

`needs-cc-art` is what puts a ticket in the queue above, so **the ticket does not leave the queue until you remove that label.** Whenever you let go of a ticket — shipped, blocked, or handed to Lee — drop BOTH `cc-active` AND `needs-cc-art`, then add whatever label says who holds it next (`needs-lee`, `needs-spec`, `blocked-*`). Setting `needs-lee` on its own is not a handoff; it just means the ticket is now waiting for Lee *and* still claimable by you.

Before you claim, sanity-check the top ticket against `git log --grep=YT-XX`. If it is already merged, the label is the bug, not the ticket — fix the label and move to the next one rather than re-implementing shipped work.

Why this rule exists: YT-76/77/78/79/87 were each shipped and merged, kept `needs-cc-art`, and so sat at the top of the queue afterwards looking exactly like unstarted work. A session that trusts the queue will redo finished tickets and can regress them.

## Scope — code-side technical art ONLY
You own: VFX & particles, materials & shaders, lighting & post-processing, camera/render polish, LODs/atlasing, elemental-recolor variant tooling, and the AI-asset import pipeline (GLB -> rigged load-by-key prefab).
You do NOT touch: gameplay logic, combat rules, enemy AI, HUD behaviour, player controls — that's the gameplay stream.
When a ticket's success is visual, do the work, ship it to the WebGL link, and hand it to Lee's eye per the handoff rule above (drop `cc-active` + `needs-cc-art`, add `needs-lee`) — do NOT mark Done on a pure-visual AC yourself.

## Staying out of the other stream's way
- Work in art/render file areas: `Assets/_Project/Art`, materials, VFX, rendering/URP settings, and `Assets/_Project/Code/Editor` (import tooling). Read gameplay files if needed but let the gameplay stream own `Assets/_Project/Code/Runtime/Player`, `/Combat`, `/CameraRig` behaviour, and enemy AI.
- `git pull origin main` before starting each ticket (the gameplay stream is merging in parallel). Auto-merge + separate file areas keeps conflicts rare; if you hit one, rebase onto main and resolve gameplay-owned files in favour of main.

## Second working copy
This stream runs in its OWN clone (e.g. `C:\dev\MaxVsTheWorlds-art`), separate from the gameplay clone, so the two CC agents never clobber each other. Same repo/remote; both push to `main`.
