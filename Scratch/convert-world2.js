// Scratch, one-shot conversion script for MV-700. Not part of the shipped project.
const fs = require('fs');

const DESIGN_PATH = 'C:/Users/lee/OneDrive - +61432418785/Documents 1/Claude/Projects/Games/world2_config.DESIGN.json';
const OUT_PATH = 'C:/Dev/MAx CCs/cc-web/Assets/_Project/Resources/Worlds/world2_config.json';

const design = JSON.parse(fs.readFileSync(DESIGN_PATH, 'utf8'));

function normalizeOpensWith(s) {
  const bracketIdx = s.indexOf(' [');
  if (bracketIdx === -1) return s;
  const base = s.slice(0, bracketIdx);
  const bracket = s.slice(bracketIdx);
  return /deck/i.test(bracket) ? base + '[DECK]' : base;
}

// ---- gates ----
const gates = design.gates.map(g => ({
  id: g.id,
  from: { area: g.from.area, wall: g.from.wall, pos: g.from.pos },
  to: { area: g.to.area, wall: g.to.wall, pos: g.to.pos },
  width: g.width,
  opensWith: normalizeOpensWith(g.opensWith),
}));

// ---- stub entry area (hand-authored, flush against a1's west wall at the gate's resolved point) ----
const a1 = design.areas.find(a => a.id === 'a1');
const g0 = design.gates.find(g => g.id === 'g0');
const a1West = a1.origin.x; // a1's west wall x
const gateZ = a1.origin.z + a1.size.d * g0.to.pos; // a1's own wall-span resolution
const stubSize = { w: 6, d: 6 };
const stub = {
  id: 'stub',
  index: 0,
  name: 'Entry path',
  role: 'entry',
  origin: { x: a1West - stubSize.w, z: gateZ - stubSize.d / 2 },
  size: stubSize,
  hasShed: false,
  garrisonDensity: 'none',
};

// ---- areas ----
const KNOWN_COMPOSITION_KEYS = new Set([
  'rusher', 'bruiser', 'heavy', 'brute', 'gunner', 'launcher', 'blinker', 'bolter', 'lurker', 'turret', 'sludger', 'charger',
]);

const droppedFloorComposition = [];

function convertComposition(area) {
  const comp = area.composition || {};
  const out = {};
  for (const [k, v] of Object.entries(comp)) {
    if (KNOWN_COMPOSITION_KEYS.has(k)) out[k] = v;
    else throw new Error(`area ${area.id}: unrecognised composition key '${k}'`);
  }
  return out;
}

function convertCover(area) {
  // WorldCover.x/z are ABSOLUTE (same convention as garrison/grates/replicators, confirmed against
  // world1_config.json's a1_h1 sitting inside a1's own absolute bounds) — unlike sludge/deck/ramp/hatch,
  // which WorldArea.WorldRectOf resolves as area-local. DESIGN authors cover area-local like those, so
  // it needs the same area.origin shift here.
  return (area.cover || []).map((c, i) => {
    let dressing;
    if (c.kind === 'concrete') dressing = 'none';
    else if (c.kind === 'pipe') dressing = 'pipe';
    else throw new Error(`area ${area.id}: unrecognised cover kind '${c.kind}'`);
    return {
      id: `${area.id}_cover${i + 1}`,
      x: area.origin.x + c.x, z: area.origin.z + c.z,
      width: c.w, height: 1.6, depth: c.d,
      shape: 'box',
      dressing,
    };
  });
}

function convertSludge(area) {
  return (area.sludge || []).map((s, i) => ({ id: `${area.id}_sludge${i + 1}`, x: s.x, z: s.z, w: s.w, d: s.d }));
}

function convertDecks(area) {
  return (area.decks || []).map((d, i) => ({ id: `${area.id}_deck${i + 1}`, x: d.x, z: d.z, w: d.w, d: d.d, height: d.height || 0 }));
}

function convertRamps(area) {
  return (area.ramps || []).map((r, i) => ({ id: `${area.id}_ramp${i + 1}`, x: r.x, z: r.z, w: r.w, d: r.d }));
}

function convertHatches(area) {
  return (area.hatches || []).map((h, i) => ({ id: `${area.id}_hatch${i + 1}`, x: h.x, z: h.z, w: h.w, d: h.d }));
}

function convertGrates(area) {
  // WorldGrate.x/z are ABSOLUTE (same convention as garrison), unlike sludge/decks/ramps/hatches which are area-local.
  return (area.grates || []).map((g, i) => ({ id: `${area.id}_grate${i + 1}`, x: area.origin.x + g.x, z: area.origin.z + g.z }));
}

function convertReplicators(area) {
  // WorldReplicator.x/z are ABSOLUTE too.
  return (area.replicators || []).map(r => ({ id: r.id, x: area.origin.x + r.x, z: area.origin.z + r.z, capacity: r.capacity }));
}

function convertGarrison(area) {
  return (area.garrison || []).map(g => ({ kind: g.kind, x: area.origin.x + g.x, z: area.origin.z + g.z, level: g.level || 0 }));
}

function convertBoss(area) {
  if (!area.boss) return null;
  // Engine WorldBoss has no dedicated "kind" field — the same "id names the kind" idiom as
  // "big_bermuda" (MapRuntime.cs OutfallGateId doc comment). DESIGN's boss.kind becomes the engine id.
  return {
    id: area.boss.kind,
    x: area.origin.x + area.boss.x,
    z: area.origin.z + area.boss.z,
    size: { w: area.boss.size.w, d: area.boss.size.d },
  };
}

const areas = design.areas.map(a => {
  if (a.floorComposition && Object.keys(a.floorComposition).length > 0) {
    droppedFloorComposition.push({ area: a.id, floorComposition: a.floorComposition });
  }

  // DESIGN marks a1 role "entry" for its own narrative purposes, but the engine's "entry" role is
  // reserved for the synthetic stub (spec §7: exactly one entry area, preceding Area 1) — world1_config.json
  // sets a1's role "normal" for the same reason. Carry every other role through verbatim.
  const role = a.role === 'entry' ? 'normal' : a.role;

  const out = {
    id: a.id,
    index: a.index,
    name: a.name,
    role,
    origin: { x: a.origin.x, z: a.origin.z },
    size: { w: a.size.w, d: a.size.d },
    hasShed: false,
    garrisonDensity: a.garrisonDensity,
    targetThreatBudget: a.targetThreatBudget || 0,
  };

  if (a.level) out.level = a.level;
  if (a.overlays) out.overlays = a.overlays;

  out.composition = convertComposition(a);
  out.cover = convertCover(a);
  out.sludge = convertSludge(a);
  out.decks = convertDecks(a);
  out.ramps = convertRamps(a);
  out.hatches = convertHatches(a);
  out.grates = convertGrates(a);
  out.replicators = convertReplicators(a);
  out.garrison = convertGarrison(a);

  const boss = convertBoss(a);
  if (boss) out.boss = boss;

  return out;
});

const config = {
  $schema: 'MaxVsTheWorlds/world-config@0.6-draft',
  world: design.world,
  revision: 'WORLD 2 v1 2026-09-08 (MV-700): areas a1-a23 converted from world2_config.DESIGN.json ' +
    '(authored by the build chat from the World 2 brief; the xlsx/svg pair alongside it are the drawn ' +
    'source). Replaces the MV-687/696 placeholder (2 areas). Garrison/grate/replicator/boss/cover ' +
    'coordinates were area-local in the DESIGN file and were shifted to the engine\'s absolute-world ' +
    'convention here; sludge/deck/ramp/hatch rects stayed area-local, matching WorldRectOf. DESIGN\'s per-area ' +
    '"floorComposition" (extra floor-level composition on a13/a15/a19 when the deck overlay is revisited) ' +
    'has no engine equivalent (WorldArea carries one composition per area index) and was not carried over ' +
    '— see the MV-700 fix comment.',
  note: design.revision,
  units: design.units,
  wallHeight: 1.5,
  dials: {
    areaCount: design.dials.areaCount,
    baseThreat: design.dials.baseThreat,
    threatGrowth: design.dials.threatGrowth,
    band: { up: design.dials.band.up, down: design.dials.band.down },
    pacingRhythm: design.dials.pacingRhythm,
    toughnessCurve: {
      heavyFromArea: design.dials.heavyFromArea,
      bruteFromArea: design.dials.bruteFromArea,
      gunnerFromArea: design.dials.gunnerFromArea,
      launcherFromArea: design.dials.launcherFromArea,
      blinkerFromArea: design.dials.blinkerFromArea,
    },
    powerupCadence: design.dials.powerupCadence,
    sludgeSpeedMultiplier: design.dials.sludgeSpeedMultiplier,
    deckHeight: design.dials.deckHeight,
  },
  enemyTypes: {
    small: { thv: 1.2 },
    large: { thv: 1.41 },
    heavy: { thv: 5.38 },
    brute: { thv: 8.53 },
    lurker: { thv: 3.0 },
    sludger: { thv: 2.0 },
    charger: { thv: 3.4 },
    turret: { thv: 2.5 },
  },
  enemyOverrides: [
    { kind: 'rusher', displayName: 'SCRAP RAT', moveSpeed: 2.4, maxHealth: 28, skin: 'stormdrain' },
  ],
  gates,
  areas: [stub, ...areas],
};

fs.writeFileSync(OUT_PATH, JSON.stringify(config, null, 1) + '\n');

console.log('wrote', OUT_PATH);
console.log('dropped floorComposition (no engine field):', JSON.stringify(droppedFloorComposition, null, 1));
