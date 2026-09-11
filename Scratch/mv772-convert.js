// Scratch, one-shot conversion script for MV-772. Not part of the shipped project.
const fs = require('fs');
const path = 'Assets/_Project/Resources/Worlds/world2_config.json';
const raw = fs.readFileSync(path, 'utf8');
const cfg = JSON.parse(raw);

const TABLE = {
  bolter:   { to: 'turret',  f: 0.55 },
  gunner:   { to: 'turret',  f: 0.45 },
  heavy:    { to: 'charger', f: 0.65 },
  bruiser:  { to: 'charger', f: 0.65 },
  launcher: { to: 'sludger', f: 0.70 },
  blinker:  { to: 'lurker',  f: 0.70 },
};

const seen = {};      // running counter n per kind, global across the file
const converted = {}; // count converted per source kind
let totalEntries = 0;
const preCoords = []; // x/z snapshot in file order, pre-change

for (const area of cfg.areas) {
  if (!area.garrison) continue;
  for (const entry of area.garrison) {
    totalEntries++;
    preCoords.push({ x: entry.x, z: entry.z });
    const rule = TABLE[entry.kind];
    if (!rule) continue;

    const n = (seen[entry.kind] = (seen[entry.kind] || 0) + 1);
    const before = Math.round((n - 1) * rule.f);
    const after = Math.round(n * rule.f);
    if (after > before) {
      converted[entry.kind] = (converted[entry.kind] || 0) + 1;
      if (entry.kind === 'blinker') entry.__convertedFromBlinker = true;
      entry.kind = rule.to;
    }
  }
}

// Grate constraint (MV-688/MV-724, MapValidation.WorldLurkerGrates): every lurker must sit on an
// authored grate in its OWN area. Only newly-converted blinker->lurker entries can be off a grate
// (pre-existing lurkers were already authored on one). Fix each such entry deterministically: move
// it onto the nearest grate in its own area if one exists, else revert it to blinker.
const usedGrateSlots = {}; // "areaId|grateId" -> count already placed there (incl. pre-existing)
for (const area of cfg.areas) {
  for (const entry of (area.garrison || [])) {
    if (entry.kind !== 'lurker') continue;
    for (const g of (area.grates || [])) {
      if (entry.x >= g.x - 1e-4 && entry.x <= g.x + 1 + 1e-4 && entry.z >= g.z - 1e-4 && entry.z <= g.z + 1 + 1e-4) {
        const key = `${area.id}|${g.id}`;
        usedGrateSlots[key] = (usedGrateSlots[key] || 0) + 1;
      }
    }
  }
}

let reverted = 0, moved = 0;
for (const area of cfg.areas) {
  const grates = area.grates || [];
  for (const entry of (area.garrison || [])) {
    if (!entry.__convertedFromBlinker || entry.kind !== 'lurker') continue;

    const onGrate = grates.some(g =>
      entry.x >= g.x - 1e-4 && entry.x <= g.x + 1 + 1e-4 && entry.z >= g.z - 1e-4 && entry.z <= g.z + 1 + 1e-4);
    if (onGrate) continue;

    if (grates.length === 0) {
      entry.kind = 'blinker';
      reverted++;
      continue;
    }

    // Nearest grate in this same area, by centre distance.
    let best = null, bestDist = Infinity;
    for (const g of grates) {
      const cx = g.x + 0.5, cz = g.z + 0.5;
      const d = Math.hypot(cx - entry.x, cz - entry.z);
      if (d < bestDist) { bestDist = d; best = g; }
    }

    const key = `${area.id}|${best.id}`;
    const slot = usedGrateSlots[key] || 0;
    usedGrateSlots[key] = slot + 1;
    // Offset within the grate's 1x1 tile so stacked entries don't share one exact point.
    const offset = 0.25 + (slot % 2) * 0.5;
    entry.x = best.x + offset;
    entry.z = best.z + offset;
    moved++;
  }
}

for (const area of cfg.areas)
  for (const entry of (area.garrison || []))
    delete entry.__convertedFromBlinker;

// Every area's `composition` dict (MV-365, authored-exact) mirrors its own `garrison` array's kind
// counts 1:1 in this file (verified against the pre-change file) — WorldGarrison's validation refuses
// a garrison authoring more of a kind than `composition` declares, so a kind swap in `garrison` without
// the matching `composition` edit fails to load at all. Rebuild each area's composition from its own
// post-conversion garrison, keeping existing keys' order and appending newly-introduced kinds after.
for (const area of cfg.areas) {
  if (!area.composition) continue;

  const finalPerArea = {};
  for (const entry of (area.garrison || []))
    finalPerArea[entry.kind] = (finalPerArea[entry.kind] || 0) + 1;

  const rebuilt = {};
  for (const kind of Object.keys(area.composition))
    if (finalPerArea[kind] > 0) rebuilt[kind] = finalPerArea[kind];
  for (const kind of Object.keys(finalPerArea))
    if (!(kind in rebuilt)) rebuilt[kind] = finalPerArea[kind];

  area.composition = rebuilt;
}

console.log('total garrison entries:', totalEntries);
console.log('seen counts:', seen);
console.log('converted counts:', converted);
console.log('grate-fix: moved', moved, 'reverted to blinker', reverted);

const finalCounts = {};
for (const area of cfg.areas)
  for (const entry of (area.garrison || []))
    finalCounts[entry.kind] = (finalCounts[entry.kind] || 0) + 1;
console.log('final counts:', finalCounts);

let stillOffGrate = [];
for (const area of cfg.areas) {
  for (let i = 0; i < (area.garrison || []).length; i++) {
    const entry = area.garrison[i];
    if (entry.kind !== 'lurker') continue;
    const onGrate = (area.grates || []).some(g =>
      entry.x >= g.x - 1e-4 && entry.x <= g.x + 1 + 1e-4 && entry.z >= g.z - 1e-4 && entry.z <= g.z + 1 + 1e-4);
    if (!onGrate) stillOffGrate.push({ area: area.id, index: i, x: entry.x, z: entry.z });
  }
}
console.log('still off grate (should be empty):', stillOffGrate);

if (process.argv[2] === '--write') {
  fs.writeFileSync('Scratch/mv772-precoords.json', JSON.stringify(preCoords));
  // JSON.stringify drops the trailing .0 on whole-number floats (e.g. wallHeight: 3.0 -> 3);
  // this ticket touches only garrison kind/x/z, so restore that one field's original formatting.
  let out = JSON.stringify(cfg, null, 1) + '\n';
  out = out.replace('"wallHeight": 3,', '"wallHeight": 3.0,');
  fs.writeFileSync(path, out);
  console.log('WROTE');
} else {
  console.log('DRY RUN (pass --write to apply)');
}
