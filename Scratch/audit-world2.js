// Throwaway audit: replicates MapValidation.WorldGarrison's cover-clearance rule and the MapData-level
// doorway (Links) rule against the shipped world2_config.json, collecting EVERY violation instead of
// stopping at the first (MapValidation itself short-circuits) — for a complete MV-700 hand-off list.
const fs = require('fs');
const cfg = JSON.parse(fs.readFileSync('Assets/_Project/Resources/Worlds/world2_config.json', 'utf8'));

const COLLIDER_RADIUS = {
  rusher: 0.4, bruiser: 0.55, heavy: 0.58, brute: 0.6, gunner: 0.4, launcher: 0.42,
  blinker: 0.4, bolter: 0.4, lurker: 0.3, turret: 0.5, sludger: 0.45, charger: 0.55,
};

function boxDistanceToPoint(cx, cz, w, d, px, pz) {
  const xMin = cx - w / 2, xMax = cx + w / 2, zMin = cz - d / 2, zMax = cz + d / 2;
  const dx = Math.max(xMin - px, 0, px - xMax);
  const dz = Math.max(zMin - pz, 0, pz - zMax);
  return Math.sqrt(dx * dx + dz * dz);
}

console.log('--- WorldGarrison: garrison-vs-cover clearance ---');
let garrisonViolations = 0;
for (const a of cfg.areas) {
  if (!a.garrison || !a.cover || a.cover.length === 0) continue;
  for (const entry of a.garrison) {
    const requiredGap = (COLLIDER_RADIUS[entry.kind] ?? 0.4) + 0.1;
    for (const c of a.cover) {
      const gap = boxDistanceToPoint(c.x, c.z, c.width, c.depth, entry.x, entry.z);
      if (gap < requiredGap) {
        console.log(`area '${a.id}': garrison ${entry.kind} (${entry.x}, ${entry.z}) is ${gap.toFixed(2)} m from cover '${c.id}' — needs ${requiredGap.toFixed(1)} m`);
        garrisonViolations++;
      }
    }
  }
}
console.log(`total garrison-vs-cover violations: ${garrisonViolations}`);

console.log('\n--- WorldGarrison: footprint containment ---');
let footprintViolations = 0;
for (const a of cfg.areas) {
  if (!a.garrison) continue;
  const xMin = a.origin.x, xMax = a.origin.x + a.size.w, zMin = a.origin.z, zMax = a.origin.z + a.size.d;
  for (const entry of a.garrison) {
    if (entry.x < xMin || entry.x > xMax || entry.z < zMin || entry.z > zMax) {
      console.log(`area '${a.id}': garrison ${entry.kind} (${entry.x}, ${entry.z}) is outside the area [${xMin},${xMax}]x[${zMin},${zMax}]`);
      footprintViolations++;
    }
  }
}
console.log(`total footprint violations: ${footprintViolations}`);

console.log('\n--- Links: doorway width (MinDoorway = 3) ---');
const areaById = Object.fromEntries(cfg.areas.map(a => [a.id, a]));
// Resolve overlay origin/size the way WorldMapLoader does, before computing zone bounds.
for (const a of cfg.areas) {
  if (a.overlays && areaById[a.overlays]) {
    a.origin = areaById[a.overlays].origin;
    a.size = areaById[a.overlays].size;
  }
}
function bounds(a) {
  return { xMin: a.origin.x, xMax: a.origin.x + a.size.w, zMin: a.origin.z, zMax: a.origin.z + a.size.d };
}
function wallSpan(a, wall) {
  const b = bounds(a);
  return (wall === 'N' || wall === 'S') ? [b.xMin, b.xMax] : [b.zMin, b.zMax];
}
function wallCoord(a, wall) {
  const b = bounds(a);
  if (wall === 'N') return b.zMax;
  if (wall === 'S') return b.zMin;
  if (wall === 'E') return b.xMax;
  return b.xMin;
}
function runsAlongX(wall) { return wall === 'N' || wall === 'S'; }

let doorwayViolations = 0;
for (const g of cfg.gates) {
  const fromArea = areaById[g.from.area], toArea = areaById[g.to.area];
  if (!fromArea || !toArea) continue;
  const [fMin, fMax] = wallSpan(fromArea, g.from.wall);
  const [tMin, tMax] = wallSpan(toArea, g.to.wall);
  const posFrom = fMin + Math.min(1, Math.max(0, g.from.pos)) * (fMax - fMin);
  const posTo = tMin + Math.min(1, Math.max(0, g.to.pos)) * (tMax - tMin);
  const along = (posFrom + posTo) / 2;

  const fb = bounds(fromArea), tb = bounds(toArea);
  let overlapMin, overlapMax, alongXAxis;
  if (Math.abs(fb.zMax - tb.zMin) < 1e-4 || Math.abs(fb.zMin - tb.zMax) < 1e-4) {
    alongXAxis = true;
    overlapMin = Math.max(fb.xMin, tb.xMin); overlapMax = Math.min(fb.xMax, tb.xMax);
  } else if (Math.abs(fb.xMax - tb.xMin) < 1e-4 || Math.abs(fb.xMin - tb.xMax) < 1e-4) {
    alongXAxis = false;
    overlapMin = Math.max(fb.zMin, tb.zMin); overlapMax = Math.min(fb.zMax, tb.zMax);
  } else {
    console.log(`gate '${g.id}': ${g.from.area} and ${g.to.area} do not share an edge`);
    continue;
  }
  const overlapLen = overlapMax - overlapMin;
  if (overlapLen <= 0) { console.log(`gate '${g.id}': zero/negative overlap (${overlapLen})`); continue; }

  let hole;
  if (g.width <= 0 || g.width >= overlapLen) {
    hole = overlapLen;
  } else {
    let centre = along;
    const half = g.width / 2;
    centre = Math.min(Math.max(centre, overlapMin + half), overlapMax - half);
    hole = 2 * half;
  }
  const margin = hole - 3;
  if (margin < 0.01) {
    console.log(`gate '${g.id}' (${g.from.area}->${g.to.area}): resolved hole ${hole.toFixed(4)} m, margin ${margin.toFixed(4)} m over MinDoorway=3 (posFrom=${posFrom.toFixed(4)}, posTo=${posTo.toFixed(4)}, along=${along.toFixed(4)}, overlap=[${overlapMin},${overlapMax}])`);
    doorwayViolations++;
  }
}
console.log(`total doorways within 0.01m of the 3m floor: ${doorwayViolations}`);

console.log('\n--- Cover: cover-vs-replicator spawn ring clearance (MapValidation.Cover, SpawnRadius+SpawnClearance=4.3) ---');
const REQUIRED_SPAWN_CLEARANCE = 3.5 + 0.8; // SpawnRadius + SpawnClearance, MapValidation.cs:37-40
let spawnRingViolations = 0;
for (const a of cfg.areas) {
  const covers = a.cover || [];
  const reps = a.replicators || [];
  for (const c of covers) {
    const xMin = c.x - c.width / 2, xMax = c.x + c.width / 2, zMin = c.z - c.depth / 2, zMax = c.z + c.depth / 2;
    for (const r of reps) {
      const dx = Math.max(xMin - r.x, 0, r.x - xMax);
      const dz = Math.max(zMin - r.z, 0, r.z - zMax);
      const dist = Math.sqrt(dx * dx + dz * dz);
      if (dist < REQUIRED_SPAWN_CLEARANCE) {
        console.log(`area '${a.id}': cover '${c.id}' (box ${c.width}x${c.depth} @ ${c.x},${c.z}) is ${dist.toFixed(3)} m from replicator '${r.id}' (${r.x},${r.z}) — needs ${REQUIRED_SPAWN_CLEARANCE.toFixed(1)} m`);
        spawnRingViolations++;
      }
    }
  }
}
console.log(`total cover-vs-replicator spawn ring violations: ${spawnRingViolations}`);
