# Code-Driven Scenes & Prefabs - Convention

**Purpose:** keep MAX vs. THE WORLDS buildable, testable, and changeable entirely by pushing code. The cloud CI (`.github/workflows/build.yml`) builds the game headlessly on every push and publishes the WebGL play link. That only works if scenes and prefabs can be assembled WITHOUT a human dragging references around in the Unity Inspector.

**Rule of thumb:** if a feature needs someone to open the Unity editor and wire something by hand for it to run, it is NOT done.

## Core rules

1. **Bootstrap is the single entry point.** Scene 0 (`Bootstrap`) is an (almost) empty scene holding one GameObject that runs `GameBootstrap`. It constructs the world in code - camera rig, player, HUD, arena, enemies, factories - in `Awake`/`Start`. Any other scenes are also assembled from code, not hand-populated.

2. **No inspector wiring as the source of truth.** Do not rely on serialized `[SerializeField]` slots that a human must populate by dragging objects in. Resolve references in code (explicit construction, `GetComponent`, a small service locator, or `RuntimeInitializeOnLoadMethod`).

3. **Data in ScriptableObjects, wiring in code.** Tunable numbers (speeds, damage, spawn counts, palettes) live in committed ScriptableObject assets that a human or AI can edit. But which objects exist and how they connect is decided in code, not in a hand-built scene graph.

4. **Load prefabs/assets by stable key, not by slot.** Instantiate prefabs via Addressables or `Resources.Load` by a stable path/key, or build them procedurally. Do not depend on a prefab reference that only exists because someone dragged it into an inspector field.

5. **Art/assets loaded by code.** Materials, meshes, and (later) AI-generated art are loaded from known paths/Addressable keys in code, so a fresh checkout builds identically with no manual setup.

6. **Tests construct systems in code.** EditMode/PlayMode tests build the systems under test in code and assert behaviour. No test should depend on a manually wired scene fixture.

## Definition of done (per feature)

- Runs by loading the `Bootstrap` scene and pressing Play, with zero manual editor setup on a fresh clone.
- Therefore builds and runs headlessly in CI (the WebGL play link reflects it).
- Any values a human/AI would tune are committed ScriptableObject assets.
- Non-trivial logic has EditMode/PlayMode tests that need no manual wiring.

## Why

This is the piece that keeps Unity out of Lee's day-to-day. With scenes built in code, Claude Code can add or change gameplay, push, and the cloud build produces a fresh playable link automatically - Lee just plays it. Manual inspector wiring would force a human back into the editor on every change, which is exactly the loop the CI pipeline removed.

See also: `.github/workflows/build.yml` (the CI pipeline) and `CC_AUTONOMY.md` (which points here).
