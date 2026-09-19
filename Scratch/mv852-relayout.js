// Scratch, one-shot conversion script for MV-852. Not part of the shipped project.
// Re-lays out World 2: forces the ground route through the Replicator door into a12, then builds
// the upper level (Part 2) over a12/a15/a16/a17/a18/a14/a19/a20.
const fs = require('fs');
const path = 'Assets/_Project/Resources/Worlds/world2_config.json';
const raw = fs.readFileSync(path, 'utf8');
const cfg = JSON.parse(raw);

const round = n => Math.round(n * 1000) / 1000;

const byId = id => cfg.areas.find(a => a.id === id);

// ---- 1. Move areas (origin + every ABSOLUTE-coordinate field: garrison/replicators/grates/cover/boss).
// sludge/decks/ramps/hatches are area-local and are NOT shifted by an origin move.
const MOVES = {
  a4: { dx: 6, dz: -32 },
  a8: { dx: 6, dz: 0 },
  a9: { dx: 6, dz: 0 },
  a10: { dx: 6, dz: 0 },
  a11: { dx: 6, dz: 0 },
  a18: { dx: 144, dz: -32 },
  a17: { dx: 144, dz: -30 },
  a16: { dx: 144, dz: -34 },
  a6: { dx: 148, dz: -6 },
  a15: { dx: 148, dz: -6 }, // overlay of a6, mirrors its move
  a12: { dx: 86, dz: 22 },
};

for (const [id, { dx, dz }] of Object.entries(MOVES)) {
  const a = byId(id);
  if (!a) throw new Error(`area ${id} not found`);
  a.origin.x = round(a.origin.x + dx);
  a.origin.z = round(a.origin.z + dz);
  for (const g of a.garrison || []) { g.x = round(g.x + dx); g.z = round(g.z + dz); }
  for (const r of a.replicators || []) { r.x = round(r.x + dx); r.z = round(r.z + dz); }
  for (const gr of a.grates || []) { gr.x = round(gr.x + dx); gr.z = round(gr.z + dz); }
  for (const c of a.cover || []) { c.x = round(c.x + dx); c.z = round(c.z + dz); }
  if (a.boss) { a.boss.x = round(a.boss.x + dx); a.boss.z = round(a.boss.z + dz); }
}

// ---- 2. Delete a7 (Silt Beds) and a13 (The Weir deck), with their contents.
cfg.areas = cfg.areas.filter(a => a.id !== 'a7' && a.id !== 'a13');

// ---- 3. Gates: remove the old upper-connection set, keep the rest, add the new ground route +
// Replicator door + Part 2 deck gates.
const REMOVE_GATES = new Set(['g3', 'g5', 'g6', 'g7', 'g8', 'g12', 'g13', 'g14', 'g15', 'g16', 'g17', 'g18', 'g19', 'g20']);
cfg.gates = cfg.gates.filter(g => !REMOVE_GATES.has(g.id));

function gate(id, fromArea, fromWall, fromPos, toArea, toWall, toPos, width, opensWith) {
  return { id, from: { area: fromArea, wall: fromWall, pos: fromPos }, to: { area: toArea, wall: toWall, pos: toPos }, width, opensWith };
}

const NEW_GATES = [
  // Ground route (primary)
  gate('g25', 'a5', 'E', 0.5, 'a4', 'W', 0.5, 3, 'primary'),
  gate('g26', 'a4', 'E', 0.5, 'a8', 'W', 0.35, 3, 'primary'),
  gate('g27', 'a11', 'N', 0.5, 'a18', 'S', 0.58, 3, 'primary'),
  gate('g28', 'a18', 'E', 0.385, 'a17', 'W', 0.333, 3, 'primary'),
  gate('g29', 'a17', 'E', 0.333, 'a16', 'W', 0.429, 3, 'primary'),
  gate('g30', 'a16', 'E', 0.429, 'a6', 'W', 0.385, 3, 'primary'),
  // Replicator door — every Replicator on the ground route; a19's is on the upper level.
  gate('g31', 'a6', 'E', 0.385, 'a12', 'W', 0.467, 3,
    'replicators-destroyed:a2,a3,a5,a8,a9,a10,a11,a18,a17,a16,a6'),
  // Part 2 — upper-level deck gates, a12 -> a15 -> a16 -> a17 -> a18 -> a14 -> a19 (then g21 -> a20)
  gate('g32', 'a12', 'W', 0.95, 'a15', 'E', 0.942, 3, 'primary[DECK]'),
  gate('g33', 'a15', 'W', 0.942, 'a16', 'E', 0.946, 3, 'primary[DECK]'),
  gate('g34', 'a16', 'W', 0.946, 'a17', 'E', 0.9375, 3, 'primary[DECK]'),
  gate('g35', 'a17', 'W', 0.9375, 'a18', 'E', 0.942, 3, 'primary[DECK]'),
  gate('g36', 'a18', 'W', 0.942, 'a14', 'E', 0.875, 3, 'primary[DECK]'),
  gate('g37', 'a14', 'W', 0.875, 'a19', 'E', 0.75, 3, 'primary[DECK]'),
];
cfg.gates.push(...NEW_GATES);

// ---- 4. "No other way up": strip every ramp/hatch in a3, a6, a11, a16, a17, a18, a12 (and a15's
// side decks), plus every deck that only those (now-removed) ramps served.
byId('a3').decks = [];
byId('a3').ramps = [];
byId('a3').hatches = [];

byId('a6').decks = byId('a6').decks.filter(d => d.id === 'a6_deck1');
byId('a6').ramps = [];
byId('a6').hatches = [];

byId('a15').decks = byId('a15').decks.filter(d => d.id === 'a15_deck1');

byId('a11').decks = [];
byId('a11').ramps = [];
byId('a11').hatches = [];

byId('a16').decks = [{ id: 'a16_deck1', x: 0, z: 25, w: 24, d: 3, height: 2.5 }];
byId('a16').ramps = [];
byId('a16').hatches = [];

byId('a17').decks = [{ id: 'a17_deck1', x: 0, z: 21, w: 26, d: 3, height: 2.5 }];
byId('a17').ramps = [];
byId('a17').hatches = [];

byId('a18').decks = [{ id: 'a18_deck1', x: 0, z: 23, w: 26, d: 3, height: 2.5 }];
byId('a18').ramps = [];
byId('a18').hatches = [];

byId('a12').decks = [{ id: 'a12_deck1', x: 0, z: 27, w: 34, d: 3, height: 2.5 }];
byId('a12').ramps = [{ id: 'a12_ramp1', x: 31, z: 21, w: 3, d: 6 }];
byId('a12').hatches = [];

// ---- 5. a19: keep deck1-3, add the deck4 connector to the new east-wall deck gate.
byId('a19').decks.push({ id: 'a19_deck4', x: 28, z: 21, w: 8, d: 3, height: 2.5 });

// a20's own deck/ramp are left exactly as authored (per spec).

// ---- 5b. Two pre-existing cover boxes ended up too close to a newly-added gate mouth / an
// adjacent-area Replicator once their areas moved; nudge them to clear space in the same room.
{
  const c = byId('a16').cover.find(x => x.id === 'a16_cover5');
  c.x = round(byId('a16').origin.x + 20);
  c.z = round(byId('a16').origin.z + 2);
}
{
  const c = byId('a18').cover.find(x => x.id === 'a18_cover2');
  c.x = round(byId('a18').origin.x + 18);
  c.z = round(byId('a18').origin.z + 10);
}

// ---- 6. a14 Gantry Run is entirely re-authored: new origin/size, single deck, one sludge channel,
// floor unreachable. Garrison/cover keep their existing kind counts, repositioned onto the new deck.
{
  const a14 = byId('a14');
  const oldGarrison = a14.garrison;
  const oldCoverCount = a14.cover.length;

  a14.origin = { x: 90, z: 108 };
  a14.size = { w: 106, d: 12 };
  a14.sludge = [{ id: 'a14_sludge1', x: 0, z: 2, w: 106, d: 4 }];
  a14.decks = [{ id: 'a14_deck1', x: 0, z: 9, w: 106, d: 3, height: 2.5 }];
  a14.ramps = [];
  a14.hatches = [];

  // Deck spans local z 9-12 (world z 117-120); spread combatants along it in two staggered rows,
  // clear of both end deck-gate mouths (x world 90 and 196).
  const marginX = 10;
  const usableW = a14.size.w - marginX * 2;
  const n = oldGarrison.length;
  a14.garrison = oldGarrison.map((entry, i) => {
    const t = n === 1 ? 0.5 : i / (n - 1);
    const localX = marginX + t * usableW;
    const localZ = i % 2 === 0 ? 9.8 : 11.2;
    return { kind: entry.kind, x: round(a14.origin.x + localX), z: round(a14.origin.z + localZ), level: 1 };
  });

  // Cover sits on the unreachable floor below the deck (local z ~6, well clear of the z9-12 deck
  // band the garrison stands on) so it can't ever coincide with a garrison point.
  const coverMarginX = 20;
  const coverUsableW = a14.size.w - coverMarginX * 2;
  a14.cover = Array.from({ length: oldCoverCount }, (_, i) => {
    const t = oldCoverCount === 1 ? 0.5 : i / (oldCoverCount - 1);
    const localX = coverMarginX + t * coverUsableW;
    return {
      id: `a14_cover${i + 1}`,
      x: round(a14.origin.x + localX), z: round(a14.origin.z + 6),
      width: 2, height: 1.6, depth: 2, shape: 'box', dressing: i % 2 === 0 ? 'tree' : 'none',
    };
  });
}

// ---- 7. Walled parapets — mark every deck on the upper-level route (data-driven flag consumed by
// MapRuntime.BuildDeck; see WorldConfig.WorldDeck.walled).
const WALLED_DECKS = new Set([
  'a12_deck1', 'a15_deck1', 'a16_deck1', 'a17_deck1', 'a18_deck1', 'a14_deck1',
  'a19_deck1', 'a19_deck2', 'a19_deck3', 'a19_deck4', 'a20_deck1',
]);
for (const a of cfg.areas)
  for (const d of a.decks || [])
    if (WALLED_DECKS.has(d.id)) d.walled = true;

// ---- write ----
let out = JSON.stringify(cfg, null, 1) + '\n';
// JSON.stringify drops the trailing .0 on whole-number floats.
out = out.replace('"wallHeight": 3,', '"wallHeight": 3.0,');
fs.writeFileSync(path, out);
console.log('WROTE', path);
console.log('areas:', cfg.areas.length, 'gates:', cfg.gates.length);
