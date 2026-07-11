# Generated-model pipeline — drop it in, run the tool, it's in the game

The bridge between AI model generation (Meshy / Tripo) and the running game. Designed so swapping a greybox for a real model is a **push**, not a Unity session.

## The flow

1. **Drop** the model into `Assets/_Project/Art/Models/Incoming/`.
2. **Run** `MaxWorlds ▸ Art ▸ Process Incoming Models (YT-51)` (or headlessly: `-executeMethod MaxWorlds.Editor.ModelImportPipeline.Run`).
3. **Get** a prefab at `Assets/_Project/Resources/Models/<key>.prefab`, loadable anywhere by key.

Nothing is dragged into an inspector at any point, so the result builds headlessly in CI and shows up on the WebGL play link — the rule in `CODE_DRIVEN_SCENES.md`.

## Keys

The key is the filename, lowercased, with non-alphanumerics collapsed to underscores:

| Source file | Key |
| --- | --- |
| `max.fbx` | `max` |
| `Big Bermuda v3.fbx` | `big_bermuda_v3` |
| `robot-enemy.fbx` | `robot_enemy` |

It's **deterministic**: re-rolling a model in Meshy and dropping the new file over the old one rebuilds the *same* prefab, rather than quietly leaving a second copy beside it. Re-running the tool is always safe.

The keys the game actually asks for live in `ModelKeys` (`max`, `water_blaster`, `robot_enemy`, `mower_hutch`, `big_bermuda`). Name the file to match the key and it lands where it's wanted.

## Using a model

```csharp
// Is it generated yet?
if (ModelLibrary.Exists(ModelKeys.Max)) { ... }

// Instantiate it:
var max = ModelLibrary.Instantiate(ModelKeys.Max, parent);
```

## Swapping a greybox — the point of all this

Attach `ModelSwap` to a greybox and give it a key:

```csharp
placeholder.AddComponent<ModelSwap>().Bind(ModelKeys.RobotEnemy);
```

- **No model generated yet?** It does nothing. The greybox stands in and the game runs exactly as it does today. This is the normal state, not a failure.
- **A model exists for that key?** The placeholder's renderer is hidden and the real model is instantiated in its place — automatically, on Awake.

The collider and every gameplay component stay on the original object. Only the *visual* changes. That's what keeps model swaps an art-stream concern that can never break combat.

So "put the real Max in the game" is:

> drop `max.fbx` → run the tool → commit → push.

CI builds it, the play link has it.

## Import settings

Applied centrally in `ModelImportPipeline.ConfigureImporter`, so every asset lands with the same rules whoever generated it:

- **Generic rig** with an avatar created from the model. Meshy/Tripo output a generic skeleton, not a Unity humanoid — forcing humanoid would demand a bone mapping these models won't satisfy.
- Normals imported, tangents calculated (Mikk).
- Mesh compression + polygon/vertex optimisation + weld: generated meshes arrive dense and unoptimised.
- **No collider.** The greybox behind the swap keeps its own.
- Materials embedded in the prefab, so importing doesn't spray loose `.mat` files through the project.

Change the house rules in one place and re-run the tool over `Incoming/` to re-apply them to everything.

## ⚠ GLB needs a package decision

**Unity cannot import `.glb` / `.gltf` natively.** It needs `com.unity.cloud.gltfast`, which is *not* in this project's `manifest.json` — and adding a Unity package is a guardrail on the art stream, so it has not been added unilaterally.

Accepted today: **`.fbx`** (plus `.obj`, `.dae`). **Meshy and Tripo both export FBX**, so the pipeline is usable right now with no package at all.

If GLB is wanted as the source format, that's Lee's call. If it's approved, the change here is small: add `com.unity.cloud.gltfast` to the manifest and add `.glb` to `ModelImportPipeline.AcceptedExtensions` (glTFast imports GLB through a ScriptedImporter, so the prefab-build step needs a small branch — it won't be a `ModelImporter`).
