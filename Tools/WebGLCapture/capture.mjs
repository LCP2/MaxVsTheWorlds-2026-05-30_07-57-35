// MV-1376: serve Builds/cc-webgl, drive headless Edge over CDP, wait for the in-build READY line,
// take two screenshots K rAF frames apart.
// Usage: node capture.mjs <buildDir> <world> <area> <gap> <outDir> <timeoutSec>
// Exit 0 ok; 3 timeout/no READY; 4 browser failure. Always self-terminates (hard timer + kills Edge).
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { spawn, spawnSync } from 'node:child_process';

const [buildDir, world, area, gapStr, outDir, timeoutStr] = process.argv.slice(2);
const gap = parseInt(gapStr, 10);
const timeoutMs = parseInt(timeoutStr, 10) * 1000;
const t0 = Date.now();
const consoleLog = [];
const log = (m) => console.log(`[capture +${((Date.now() - t0) / 1000).toFixed(1)}s] ${m}`);
let edge = null;
let server = null;
const cleanup = () => {
  try { if (edge) spawnSync('taskkill', ['/F', '/T', '/PID', String(edge.pid)]); } catch {}
  try { if (server) server.close(); } catch {}
};
const die = (code, why) => {
  console.log(`FAIL: ${why}`);
  try { fs.mkdirSync(outDir, { recursive: true }); fs.writeFileSync(path.join(outDir, 'console.txt'), consoleLog.join('\n')); } catch {}
  cleanup();
  setTimeout(() => process.exit(code), 300);
};
const hard = setTimeout(() => die(3, `hard timeout after ${timeoutStr}s (no READY line / capture incomplete)`), timeoutMs);

const MIME = {
  '.html': 'text/html', '.js': 'application/javascript', '.wasm': 'application/wasm',
  '.data': 'application/octet-stream', '.json': 'application/json', '.png': 'image/png',
  '.css': 'text/css', '.ico': 'image/x-icon',
};
const root = path.resolve(buildDir);
server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]);
  if (p === '/') p = '/index.html';
  const f = path.resolve(path.join(root, p));
  if (!f.startsWith(root) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); res.end(); return; }
  const hdr = { 'Content-Type': MIME[path.extname(f)] || 'application/octet-stream' };
  if (f.endsWith('.br')) hdr['Content-Encoding'] = 'br';
  else if (f.endsWith('.gz')) hdr['Content-Encoding'] = 'gzip';
  res.writeHead(200, hdr);
  fs.createReadStream(f).pipe(res);
});
await new Promise((r) => server.listen(0, '127.0.0.1', r));
const port = server.address().port;
const url = `http://127.0.0.1:${port}/index.html?hud=0&world=${world}&area=${area}`;

const edgePaths = [
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
];
const exe = edgePaths.find((p) => fs.existsSync(p));
if (!exe) { die(4, 'msedge.exe not found'); await new Promise(() => {}); }
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'mv1376-'));
edge = spawn(exe, [
  '--headless=new', '--remote-debugging-port=0', `--user-data-dir=${profile}`, '--window-size=1280,720',
  '--hide-scrollbars', '--disable-renderer-backgrounding', '--disable-background-timer-throttling', '--disable-backgrounding-occluded-windows', '--disable-features=CalculateNativeWinOcclusion', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist',
  '--no-first-run', '--mute-audio', 'about:blank',
], { stdio: 'ignore' });

let wsUrl = null;
const portFile = path.join(profile, 'DevToolsActivePort');
while (!wsUrl && Date.now() - t0 < timeoutMs) {
  if (fs.existsSync(portFile)) {
    const [dp] = fs.readFileSync(portFile, 'utf8').split('\n');
    try {
      const list = await (await fetch(`http://127.0.0.1:${dp}/json`)).json();
      const pg = list.find((t) => t.type === 'page');
      if (pg) wsUrl = pg.webSocketDebuggerUrl;
    } catch {}
  }
  if (!wsUrl) await new Promise((r) => setTimeout(r, 200));
}
if (!wsUrl) { die(4, 'could not attach to Edge DevTools'); await new Promise(() => {}); }

const ws = new WebSocket(wsUrl);
await new Promise((r, j) => { ws.onopen = r; ws.onerror = () => j(new Error('ws')); });
let id = 0;
const pending = new Map();
let readyLine = null;
ws.onmessage = (ev) => {
  const m = JSON.parse(ev.data);
  if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); return; }
  if (m.method === 'Runtime.consoleAPICalled') {
    const txt = m.params.args.map((a) => a.value ?? a.description ?? '').join(' ');
    if (txt.includes('[MV-1376] READY')) readyLine = txt;
    consoleLog.push(`${m.params.type}: ${txt}`);
  }
};
const send = (method, params = {}) => new Promise((r) => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
await send('Runtime.enable');
await send('Page.enable');
await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });
log(`navigating ${url}`);
await send('Page.navigate', { url });
await send('Page.bringToFront');
await send('Emulation.setFocusEmulationEnabled', { enabled: true });

while (!readyLine && Date.now() - t0 < timeoutMs - 5000) await new Promise((r) => setTimeout(r, 250));
if (!readyLine) { die(3, 'no "[MV-1376] READY" line from the build before timeout'); await new Promise(() => {}); }
log(`READY: ${readyLine}`);
fs.mkdirSync(outDir, { recursive: true });
const frames = (n) => send('Runtime.evaluate', {
  expression: `new Promise(r=>{let k=${n};const f=()=>--k<=0?r(1):requestAnimationFrame(f);requestAnimationFrame(f)})`,
  awaitPromise: true,
});
const shot = async (name) => {
  const rc = await send('Runtime.evaluate', { expression: "(()=>{const c=document.querySelector('canvas');if(!c)return null;const b=c.getBoundingClientRect();return {x:b.x,y:b.y,width:b.width,height:b.height}})()", returnByValue: true });
  const b = rc.result?.result?.value;
  const r = await send('Page.captureScreenshot', b ? { format: 'png', clip: { ...b, scale: 1 } } : { format: 'png' });
  fs.writeFileSync(path.join(outDir, name), Buffer.from(r.result.data, 'base64'));
};
const fpsProbe = await send('Runtime.evaluate', { expression: "new Promise(r=>{let n=0;const t=performance.now();const f=()=>{n++;performance.now()-t<4000?requestAnimationFrame(f):r(n/4)};requestAnimationFrame(f)})", awaitPromise: true });
log(`rAF rate ${JSON.stringify(fpsProbe.result?.result?.value)} fps`);
log('settling');
await Promise.race([frames(30), new Promise((r) => setTimeout(r, 15000))]); // camera / area jump / streaming
log('shot a');
await shot('capture_a.png');
const tg = Date.now();
await Promise.race([frames(gap), new Promise((r) => setTimeout(r, 30000))]);
log(`gap of ${gap} rAF frames took ${Date.now() - tg} ms`);
await shot('capture_b.png');
fs.writeFileSync(path.join(outDir, 'ready.txt'), readyLine + '\n');
log('captures written');
clearTimeout(hard);
cleanup();
setTimeout(() => process.exit(0), 300);
