# Git Merge Setup — Unity YAML must merge headless (YT-103)

**Rule:** git must never launch a GUI to merge a Unity file. A blocking dialog halts an
autonomous CC merge forever.

## What broke

Git was configured to use **Unity Smart-Merge (`UnityYAMLMerge`)** as the merge driver for
`.unity` / `.prefab` / `.asset`. When it can't fully auto-merge it launches a GUI fallback tool
listed in `mergespecfile.txt`. That tool isn't installed, so a merge/rebase popped:

> UnityYAMLMerge Error — Couldn't locate merge tool to handle extension … mergespecfile.txt

and the merge blocked waiting on a human that never comes.

## The fix

**Take UnityYAMLMerge out of the loop entirely.** Unity YAML files use git's built-in **3-way
text merge**, which writes standard `<<<<<<< / ======= / >>>>>>>` conflict markers a CC resolves
in code — no external tool, no dialog, ever.

Two parts:

1. **Committed (durable, every clone):** `.gitattributes` no longer registers `merge=unityyamlmerge`.
   The `[attr]unity-yaml` macro carries only `eol=lf linguist-language=yaml`, so `merge` is left
   **unspecified** → git's default text merge. Verify:

   ```sh
   git check-attr merge -- ProjectSettings/ProjectSettings.asset   # -> merge: unspecified
   ```

2. **Per-clone hygiene (run once on every CC start):**

   ```sh
   sh scripts/setup-git-merge.sh
   ```

   Idempotent. Strips any stale `merge.unityyamlmerge` driver from this clone's `.git/config`
   (merge-driver config lives in `.git/config`, which git won't read from the repo, so it can't be
   committed). With the attribute unset nothing invokes it anyway — this just makes sure a future
   re-add of the attribute can't resurrect the GUI.

## Resolving a Unity YAML conflict

You'll get normal git conflict markers. Per the CC contract, **favour `main` for gameplay-owned
files** when merging/rebasing onto it. Note: `ProjectSettings.asset`'s `bundleVersion:
local-MMDD-HHMM` is auto-rewritten on every build and carries no meaning — never let it be the
thing you agonise over; take either side (or re-run a build to re-stamp it).

## Reduce conflicts in the first place

Two streams (gameplay + art) push to `main`, and Unity YAML is merge-hostile. Keep leaning on
[`CODE_DRIVEN_SCENES.md`](CODE_DRIVEN_SCENES.md): **no hand-edited shared scene/prefab/asset** —
scenes and prefabs are assembled in code/scaffolds, tunables live in ScriptableObjects a single
owner edits. Keep the two streams' YAML-asset file areas non-overlapping.

## History

- **YT-103** — the conflict that triggered this was `ProjectSettings/ProjectSettings.asset`, and the
  only real delta between the two streams was the throwaway `bundleVersion` auto-bump; resolved by
  keeping the gameplay stream's meaningful settings (company, iOS bundle id) and letting the newer
  bundleVersion stand.
