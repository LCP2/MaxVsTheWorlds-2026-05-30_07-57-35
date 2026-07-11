# CC Autonomy Contract — ART / TECH-ART STREAM (MAX vs. THE WORLDS)

> Kickoff: `Follow CC_AUTONOMY_ART.md.`
> This is the SECOND, parallel workstream — code-side technical art. The gameplay stream runs separately under `CC_AUTONOMY.md`. Do NOT do gameplay-logic work; that's the other stream's job.

## Shared rules
Follow ALL of `CC_AUTONOMY.md` for conventions: auto-merge on green (squash-merge your own verified branch to `main`, don't wait for Lee), run `cc-verify` before merge, the code-driven scene/prefab convention (`docs/CODE_DRIVEN_SCENES.md`), the boot-scene rule, commit/Jira etiquette, and "run the verify command — never infer success."

## This stream's queue (DIFFERENT from the gameplay one)
On every start, query Jira:

```
project = YT AND labels = needs-cc-art AND statusCategory != Done ORDER BY priority DESC, key ASC
```

Claim the top ticket (add label `cc-active`). If the `needs-cc-art` queue is empty, STOP and report — do NOT fall back to gameplay (`needs-cc`) tickets. Those belong to the other stream.

## Scope — code-side technical art ONLY
You own: VFX & particles, materials & shaders, lighting & post-processing, camera/render polish, LODs/atlasing, elemental-recolor variant tooling, and the AI-asset import pipeline (GLB -> rigged load-by-key prefab).
You do NOT touch: gameplay logic, combat rules, enemy AI, HUD behaviour, player controls — that's the gameplay stream.
When a ticket's success is visual, do the work, ship it to the WebGL link, and set `needs-lee` for Lee's eye — do NOT mark Done on a pure-visual AC yourself.

## Staying out of the other stream's way
- Work in art/render file areas: `Assets/_Project/Art`, materials, VFX, rendering/URP settings, and `Assets/_Project/Code/Editor` (import tooling). Read gameplay files if needed but let the gameplay stream own `Assets/_Project/Code/Runtime/Player`, `/Combat`, `/CameraRig` behaviour, and enemy AI.
- `git pull origin main` before starting each ticket (the gameplay stream is merging in parallel). Auto-merge + separate file areas keeps conflicts rare; if you hit one, rebase onto main and resolve gameplay-owned files in favour of main.

## Second working copy
This stream runs in its OWN clone (e.g. `C:\dev\MaxVsTheWorlds-art`), separate from the gameplay clone, so the two CC agents never clobber each other. Same repo/remote; both push to `main`.
