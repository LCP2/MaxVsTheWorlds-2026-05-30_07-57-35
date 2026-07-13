# Garden kit — source & provenance (YT-75)

**Kit:** Kenney *Nature Kit* v2.1 (29-04-2020) — <https://kenney.nl/assets/nature-kit>
**Licence:** CC0 1.0 (public domain). Free for commercial use, credit appreciated, not required.
Full text in `LICENSE.txt` next to this file.

## What's in the repo

A curated **34-model subset**, not the whole 330-model kit — the fence, trees, shrubs, flowers,
planters, beds, paving, rocks and timber the Backyard actually places. The rest of the kit (cliffs,
bridges, palms, tents, crops) isn't backyard, so it isn't carried.

The models live in `Assets/_Project/Resources/GardenKit/*.fbx` so they load by key at runtime
(`GardenKit.Prefab`), which is the load-by-key half of `docs/CODE_DRIVEN_SCENES.md` §4 — nothing is
dragged into an inspector slot, and a fresh clone builds the same yard.

## Why FBX and not GLB

Unity imports FBX/OBJ/DAE with no extra package. It does **not** import `.glb`/`.gltf` without
glTFast, which isn't in `Packages/manifest.json` — and adding a package is a guardrail on this
stream. Same call as `ModelImportPipeline` (YT-51) made, for the same reason.

## Why the kit's colours aren't used

The models keep their **material names** (`wood`, `leafsGreen`, `stone`, `dirt`…) and lose their
colours: `MaxWorlds.Rendering.KitMaterials` repaints every one of them from our own palette. The
names are a free, stable classification of every surface in the kit; the colours belong to somebody
else's game, and a yard wearing them reads as an asset pack dropped into ours. It's also the hook
YT-77's surface pass hangs on.

Import settings are pinned in code (`Editor/GardenKitImporter.cs`), not clicked into an inspector,
so CI imports the kit exactly as a dev machine does.

## Re-fetching

    curl -L -o nature-kit.zip \
      https://kenney.nl/media/pages/assets/nature-kit/37ac38a37b-1677698939/kenney_nature-kit.zip

Take `Models/FBX format/<name>.fbx` for the models listed in `MaxWorlds.Dressing.KitModels`.
