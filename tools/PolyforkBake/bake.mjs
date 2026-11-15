// Builds every variant in models.mjs from its Polyfork module (createAsset with the listed knobs),
// merges it to one indexed mesh with COLOR_0, and writes:
//   unity/Assets/Resources/Polyfork/<key>.bytes   GLB per variant
//   unity/Assets/Resources/Polyfork/variants.json  catalogue the game loads (ModelCatalog shape)
//   unity/Assets/Resources/Polyfork/colliders/<asset>.json  Polyfork convex hulls for breakable models, in Unity model space
//   tools/PolyforkBake/manifest.json                every asset, knob value, reason, size, triangle count and physics
// physics.json is Polyfork's getAsset physics block per asset (mass, body, material, collision), fetched through the MCP.
// Modules and metadata are cached in tools/PolyforkBake/.cache (gitignored); the .mjs files are never committed.
//   node tools/PolyforkBake/bake.mjs [--only <prefix>]
import * as THREE from 'three';
import fs from 'fs';
import path from 'path';
import { fileURLToPath, pathToFileURL } from 'url';
import { palette, derived, variants } from './models.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..', '..');
const cache = path.join(here, '.cache');
const outDir = path.join(root, 'unity', 'Assets', 'Resources', 'Polyfork');
const UA = { 'User-Agent': 'crosstalk-dogpatch-bake/1.0 (+https://github.com/duration-ai/crosstalk-yc-hack)' };
const physics = JSON.parse(fs.readFileSync(path.join(here, 'physics.json'), 'utf8')).assets;
const physicsClass = role => role.startsWith('building:') || role === 'prop:crane_mast' || role === 'prop:crane_top' || role === 'prop:bollard' ? 'static' : 'breakable';
const only = process.argv.includes('--only') ? process.argv[process.argv.indexOf('--only') + 1] : null;
fs.mkdirSync(cache, { recursive: true });
const realWarn = console.warn;
console.warn = (...a) => { if (!String(a[0]).includes('toNonIndexed')) realWarn(...a); };

async function cached(name, url) {
  const file = path.join(cache, name);
  if (!fs.existsSync(file)) {
    for (let attempt = 0; ; attempt++) {
      const res = await fetch(url, { headers: UA });
      if (res.ok) { fs.writeFileSync(file, Buffer.from(await res.arrayBuffer())); break; }
      if (attempt >= 4 || (res.status !== 429 && res.status < 500)) throw new Error(url + ' -> ' + res.status);
      await new Promise(r => setTimeout(r, 1000 * 2 ** attempt));
    }
  }
  return file;
}

function resolve(knobs) {
  const out = {};
  for (const [k, v] of Object.entries(knobs)) out[k] = typeof v === 'string' && palette[v] ? palette[v] : v;
  return out;
}

function merge(group) {
  group.updateMatrixWorld(true);
  const pos = [], col = [], idx = [], seen = new Map();
  const p = new THREE.Vector3();
  group.traverse(o => {
    if (!o.isMesh || !o.visible) return;
    const g = o.geometry, P = g.attributes.position, C = g.attributes.color;
    const mats = Array.isArray(o.material) ? o.material : [o.material];
    const fallback = mats[0]?.color ?? new THREE.Color(1, 1, 1);
    const n = g.index ? g.index.count : P.count;
    for (let i = 0; i < n; i++) {
      const vi = g.index ? g.index.getX(i) : i;
      p.fromBufferAttribute(P, vi).applyMatrix4(o.matrixWorld);
      const r = C ? C.getX(vi) : fallback.r, gg = C ? C.getY(vi) : fallback.g, b = C ? C.getZ(vi) : fallback.b;
      const key = [p.x, p.y, p.z].map(v => Math.round(v * 1e4)).join(',') + '|' + [r, gg, b].map(v => Math.round(v * 4096)).join(',');
      let id = seen.get(key);
      if (id === undefined) { id = pos.length / 3; seen.set(key, id); pos.push(p.x, p.y, p.z); col.push(r, gg, b); }
      idx.push(id);
    }
    if (o.matrixWorld.determinant() < 0) for (let i = idx.length - n; i < idx.length; i += 3) [idx[i + 1], idx[i + 2]] = [idx[i + 2], idx[i + 1]];
  });
  const clean = [];
  for (let i = 0; i < idx.length; i += 3) if (idx[i] !== idx[i + 1] && idx[i + 1] !== idx[i + 2] && idx[i] !== idx[i + 2]) clean.push(idx[i], idx[i + 1], idx[i + 2]);
  return { pos: new Float32Array(pos), col: new Float32Array(col), idx: new Uint32Array(clean) };
}

function glb({ pos, col, idx }, name) {
  const min = [Infinity, Infinity, Infinity], max = [-Infinity, -Infinity, -Infinity];
  for (let i = 0; i < pos.length; i += 3) for (let a = 0; a < 3; a++) { min[a] = Math.min(min[a], pos[i + a]); max[a] = Math.max(max[a], pos[i + a]); }
  const bin = Buffer.concat([Buffer.from(pos.buffer), Buffer.from(col.buffer), Buffer.from(idx.buffer)]);
  const json = {
    asset: { version: '2.0', generator: 'crosstalk PolyforkBake' }, scene: 0, scenes: [{ nodes: [0] }],
    nodes: [{ name, mesh: 0 }],
    meshes: [{ name, primitives: [{ attributes: { POSITION: 0, COLOR_0: 1 }, indices: 2, mode: 4 }] }],
    buffers: [{ byteLength: bin.length }],
    bufferViews: [
      { buffer: 0, byteOffset: 0, byteLength: pos.byteLength, target: 34962 },
      { buffer: 0, byteOffset: pos.byteLength, byteLength: col.byteLength, target: 34962 },
      { buffer: 0, byteOffset: pos.byteLength + col.byteLength, byteLength: idx.byteLength, target: 34963 },
    ],
    accessors: [
      { bufferView: 0, componentType: 5126, count: pos.length / 3, type: 'VEC3', min, max },
      { bufferView: 1, componentType: 5126, count: col.length / 3, type: 'VEC3' },
      { bufferView: 2, componentType: 5125, count: idx.length, type: 'SCALAR' },
    ],
  };
  let js = Buffer.from(JSON.stringify(json));
  js = Buffer.concat([js, Buffer.alloc((4 - js.length % 4) % 4, 0x20)]);
  const head = Buffer.alloc(12);
  head.writeUInt32LE(0x46546c67, 0); head.writeUInt32LE(2, 4); head.writeUInt32LE(12 + 8 + js.length + 8 + bin.length, 8);
  const chunk = (type, data) => { const h = Buffer.alloc(8); h.writeUInt32LE(data.length, 0); h.writeUInt32LE(type, 4); return Buffer.concat([h, data]); };
  return { bytes: Buffer.concat([head, chunk(0x4e4f534a, js), chunk(0x004e4942, bin)]), min, max };
}

const assets = {}, records = [], problems = [];
for (const v of variants) {
  if (only && !v.key.startsWith(only)) continue;
  if (!assets[v.asset]) {
    const meta = JSON.parse(fs.readFileSync(await cached(v.asset + '.json', 'https://polyfork.dev/api/assets/' + v.asset), 'utf8'));
    const params = JSON.parse(fs.readFileSync(await cached(v.asset + '-params.json', 'https://polyfork.dev/cdn/' + v.asset + '-params.json'), 'utf8'));
    const mod = await import(pathToFileURL(await cached(v.asset + '.mjs', 'https://polyfork.dev/cdn/' + v.asset + '.mjs')).href);
    const a = meta.asset ?? meta;
    assets[v.asset] = { title: a.title, kit: a.kit ?? null, page: 'https://polyfork.dev/asset/' + v.asset, credit: a.credit ?? null, license: a.license ?? 'CC BY 4.0 (mesh)', stockSize: a.size_m, stockTriangles: a.triangles, params: params.params ?? {}, night: params.night ?? {}, create: mod.createAsset ?? mod.default };
  }
  const asset = assets[v.asset];
  const unknown = Object.keys(v.knobs).filter(k => !(k in asset.params));
  if (unknown.length) problems.push(v.key + ': unknown knobs ' + unknown.join(', '));
  const colourKnobs = Object.entries(asset.params).filter(([, s]) => s.type === 'color').map(([k]) => k);
  const unset = colourKnobs.filter(k => !(k in v.knobs));
  if (unset.length) problems.push(v.key + ': colour knobs left at stock ' + unset.join(', '));
  const knobs = resolve(v.knobs);
  const night = [];
  for (const [zone, z] of Object.entries(asset.night)) {
    if (z.findable === false) continue;
    const albedo = (knobs[zone] ?? z.albedo ?? '').toLowerCase();
    if (!albedo) continue;
    const shared = colourKnobs.filter(k => k !== zone && !/glass|pane/i.test(k) && String(knobs[k] ?? asset.params[k].default).toLowerCase() === albedo);
    if (shared.length) problems.push(v.key + ': night zone ' + zone + ' shares its colour ' + albedo + ' with ' + shared.join(', '));
    night.push({ zone, albedo, color: z.color, intensity: z.intensity ?? 1, describe: z.describe });
  }
  const mesh = merge(asset.create(knobs));
  const { bytes, min, max } = glb(mesh, v.key);
  fs.writeFileSync(path.join(outDir, v.key + '.bytes'), bytes);
  const size = { x: +(max[0] - min[0]).toFixed(3), y: +(max[1] - min[1]).toFixed(3), z: +(max[2] - min[2]).toFixed(3) };
  records.push({ key: v.key, role: v.role, asset: v.asset, physicsClass: physicsClass(v.role), knobs, night, why: v.why, size, center: { x: +((max[0] + min[0]) / 2).toFixed(3), z: +((max[2] + min[2]) / 2).toFixed(3) }, minY: +min[1].toFixed(3), triangles: mesh.idx.length / 3, vertices: mesh.pos.length / 3, bytes: bytes.length });
  console.log(v.key.padEnd(34), String(mesh.idx.length / 3).padStart(5), 'tris', size.x + ' x ' + size.y + ' x ' + size.z);
}
fs.mkdirSync(path.join(outDir, 'colliders'), { recursive: true });
for (const id of new Set(records.filter(r => physicsClass(r.role) !== 'static').map(r => r.asset))) {
  const p = physics[id];
  if (!p?.collision?.colliders_url) { problems.push(id + ': no Polyfork colliders'); continue; }
  const src = JSON.parse(fs.readFileSync(await cached(id + '-colliders.json', p.collision.colliders_url), 'utf8'));
  const parts = (src.parts ?? [src.hull]).map(part => ({ pts: part.flatMap(([x, y, z]) => [-x, y, z]) }));
  fs.writeFileSync(path.join(outDir, 'colliders', id + '.json'), JSON.stringify({ asset: id, url: p.collision.colliders_url, approximation: src.approximation, space: 'Unity model space (glTF X mirrored), metres, default knobs', parts }));
}
if (problems.length) { console.log('\nPROBLEMS\n' + problems.join('\n')); }
if (only) process.exit(problems.length ? 1 : 0);

const manifest = {
  generatedBy: 'node tools/PolyforkBake/bake.mjs', source: 'https://polyfork.dev', palette, derivedColours: derived,
  nightRule: 'night[] per variant is the asset\'s Polyfork night map with each zone\'s albedo replaced by the recoloured knob value (sRGB hex). Vertices whose COLOR_0, converted to sRGB, equals that albedo are the zone: make them emissive at color x intensity after dark. The bake fails if a zone shares its colour with another knob.',
  colourRule: 'Every colour knob of every variant is set to a Speed Dial palette colour (STYLE-GUIDE.md); none is left at its stock value.',
  assets: Object.fromEntries(Object.entries(assets).map(([id, a]) => [id, { title: a.title, kit: a.kit, page: a.page, credit: a.credit, license: a.license, stockSize: a.stockSize, stockTriangles: a.stockTriangles, physics: physics[id] ?? null }])),
  physicsRule: 'physics per asset is Polyfork\'s own block for the default knobs. physicsClass per variant: static for buildings, crane parts and bollards, breakable for trees and every other prop. Breakable assets ship their Polyfork convex hulls under Resources/Polyfork/colliders.',
  variants: records,
};
fs.writeFileSync(path.join(here, 'manifest.json'), JSON.stringify(manifest, null, 1) + '\n');
const catalog = { source: 'https://polyfork.dev', models: records.map(r => ({ id: r.key, role: r.role, title: assets[r.asset].title, asset: r.asset, sizeX: r.size.x, sizeY: r.size.y, sizeZ: r.size.z, centerX: r.center.x, centerZ: r.center.z, triangles: r.triangles, massKg: physics[r.asset]?.mass_kg ?? 0, body: physics[r.asset]?.body ?? '', material: physics[r.asset]?.material ?? '', physicsClass: r.physicsClass, colliders: r.physicsClass === 'static' ? '' : 'Polyfork/colliders/' + r.asset, collidersUrl: physics[r.asset]?.collision?.colliders_url ?? '' })) };
fs.writeFileSync(path.join(outDir, 'variants.json'), JSON.stringify(catalog));
console.log('\n' + records.length + ' variants, ' + Object.keys(assets).length + ' assets, ' + (records.reduce((s, r) => s + r.bytes, 0) / 1048576).toFixed(1) + ' MB' + (problems.length ? ', ' + problems.length + ' problems' : ''));
process.exit(problems.length ? 1 : 0);
