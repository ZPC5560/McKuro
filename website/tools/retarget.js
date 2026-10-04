// VMD → biped-GLB retarget (v2).
//
// MMD bones rest at identity, so a VMD quaternion q(b) is the bone's rotation in its
// parent's ANIMATED frame. The target keeps its bind pose when q = identity, so the
// animated local rotation of a driven glTF node is
//
//   Rloc(b, t) = Rw_bind(parent)⁻¹ · C·q(b, t)·C⁻¹ · Rw_bind(b)
//
// (Rw_bind = bind world rotation, C = mirror-Z: quats (x,y,z,w)→(-x,-y,z,w), which
// maps MMD's front (-Z) onto the model's front (+Z) and flips handedness). Worlds
// then come from plain FK down the node hierarchy — joint positions follow the
// rotating parents — and the animated world rotation works out to
// Δworld(b)·Rw_bind(b) with Δworld = C·R_mmd_world(b)·C⁻¹, i.e. the MMD delta about
// the bone's own bind pivot.
//
// Root: センター translation (slow xz drift removed so she dances in place) is added
// to Bip001's bind local translation, rotated into the parent's frame, scaled by S.
// S and the ground offset dy are calibrated by Theil-Sen: on planted frames
// S·py + relFootY is constant, so slope(relFootY vs py) = -S.
"use strict";
const fs = require("fs");
const { parseVmd, tracksPerBone, qMul, qInv, qAngle, qSlerp, sampleTrack } = require("./vmd");

// ---------- config ----------
const USAGE = "usage: node retarget.js [model.glb] [segA] [segB] [out.json] [vmd]  (segA/segB: 整数帧, segA < segB)";
const GLB_IN = process.argv[2] || "E:/donet/.site-build/gltf/s5.glb";
const VMD = process.argv[6] || "E:/donet/McKuro/玉兰开花三月三_动作_by_鲸落璃沙郊_2d24dce03c1f8211d4d9c60f803fd5d7/玉兰开花三月三_动作.vmd";
const SEG_A = Number(process.argv[3] ?? 645);
const SEG_B = Number(process.argv[4] ?? 1470);
if (!Number.isInteger(SEG_A) || !Number.isInteger(SEG_B) || !(SEG_A < SEG_B)) {
  console.error(USAGE);
  console.error(`invalid segment: segA=${process.argv[3] ?? 645} segB=${process.argv[4] ?? 1470} (need integers with segA < segB)`);
  process.exit(1);
}
const FPS = 30;
const DECIM_ROT = 0.35 * Math.PI / 180;
const DECIM_POS = 0.002;
const OUT_JSON = process.argv[5] || "dance-tracks.json";

// MMD bone → glTF node
const MMD_TO_NODE = {
  "センター": "Bip001", "腰": "Bip001Pelvis",
  "上半身": "Bip001Spine", "上半身2": "Bip001Spine1", "首": "Bip001Neck", "頭": "Bip001Head",
  "左肩": "Bip001LClavicle", "右肩": "Bip001RClavicle",
  "左腕": "Bip001LUpperArm", "右腕": "Bip001RUpperArm",
  "左ひじ": "Bip001LForearm", "右ひじ": "Bip001RForearm",
  "左手首": "Bip001LHand", "右手首": "Bip001RHand",
  "左足": "Bip001LThigh", "右足": "Bip001RThigh",
  "左ひざ": "Bip001LCalf", "右ひざ": "Bip001RCalf",
  "左足首": "Bip001LFoot", "右足首": "Bip001RFoot",
};
// 下半身 has no target of its own (both 腰 and 下半身 would want Bip001Pelvis): 腰
// drives Pelvis, and 下半身 — which in MMD turns the legs against the torso — is
// folded into the thighs' source chain, where it propagates to the whole leg exactly.
const MMD_EXTRA = { "左足": ["下半身"], "右足": ["下半身"] };
for (const [en, jp] of [["L", "左"], ["R", "右"]]) {
  MMD_TO_NODE[jp + "親指0"] = `Bip001${en}Finger0`;
  MMD_TO_NODE[jp + "親指1"] = `Bip001${en}Finger01`;
  MMD_TO_NODE[jp + "親指2"] = `Bip001${en}Finger02`;
  for (const [f, n] of Object.entries({ "人指": "1", "中指": "2", "薬指": "3", "小指": "4" })) {
    MMD_TO_NODE[jp + f + "1"] = `Bip001${en}Finger${n}`;
    MMD_TO_NODE[jp + f + "2"] = `Bip001${en}Finger${n}1`;
    MMD_TO_NODE[jp + f + "3"] = `Bip001${en}Finger${n}2`;
  }
}
// MMD hierarchy (standard skeleton; 肩 off 上半身2 — its dance rotations are tiny so
// the alternative parent would not change results materially)
const MMD_TREE = {
  "センター": null, "グルーブ": "センター", "腰": "グルーブ",
  "上半身": "腰", "上半身2": "上半身", "首": "上半身2", "頭": "首",
  "左肩": "上半身2", "右肩": "上半身2",
  "左腕": "左肩", "右腕": "右肩",
  "左ひじ": "左腕", "右ひじ": "右腕",
  "左手首": "左ひじ", "右手首": "右ひじ",
  "下半身": "腰",
  "左足": "下半身", "右足": "下半身",
  "左ひざ": "左足", "右ひざ": "右足",
  "左足首": "左ひざ", "右足首": "右ひざ",
};
for (const jp of ["左", "右"]) {
  MMD_TREE[jp + "親指0"] = jp + "手首"; MMD_TREE[jp + "親指1"] = jp + "親指0"; MMD_TREE[jp + "親指2"] = jp + "親指1";
  for (const f of ["人指", "中指", "薬指", "小指"]) {
    MMD_TREE[jp + f + "1"] = jp + "手首";
    MMD_TREE[jp + f + "2"] = jp + f + "1";
    MMD_TREE[jp + f + "3"] = jp + f + "2";
  }
}

// Critical bones/nodes: every name the computation below consumes through
// tracks.get / nodeByName / world.get. If any is missing we abort at startup
// (console.error + exit) instead of crashing mid-run or silently retargeting half
// the skeleton. Finger chains are optional decoration and keep the NOTE behaviour.
const CRITICAL_BONES = [
  "センター", "腰", "下半身",
  "上半身", "上半身2", "首", "頭",
  "左肩", "右肩", "左腕", "右腕", "左ひじ", "右ひじ", "左手首", "右手首",
  "左足", "右足", "左ひざ", "右ひざ", "左足首", "右足首",
];
const CRITICAL_NODES = ["Bip001", ...CRITICAL_BONES.map((b) => MMD_TO_NODE[b]).filter((n) => n)];

// ---------- math ----------
function m4Mul(a, b) {
  const o = new Array(16).fill(0);
  for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) for (let k = 0; k < 4; k++) o[c * 4 + r] += a[k * 4 + r] * b[c * 4 + k];
  return o;
}
function m4FromNode(n) {
  if (n.matrix) return n.matrix.slice();
  const t = n.translation || [0, 0, 0], q = n.rotation || [0, 0, 0, 1], s = n.scale || [1, 1, 1];
  const [x, y, z, w] = q;
  const x2 = x + x, y2 = y + y, z2 = z + z;
  const xx = x * x2, xy = x * y2, xz = x * z2, yy = y * y2, yz = y * z2, zz = z * z2, wx = w * x2, wy = w * y2, wz = w * z2;
  return [
    (1 - (yy + zz)) * s[0], (xy + wz) * s[0], (xz - wy) * s[0], 0,
    (xy - wz) * s[1], (1 - (xx + zz)) * s[1], (yz + wx) * s[1], 0,
    (xz + wy) * s[2], (yz - wx) * s[2], (1 - (xx + yy)) * s[2], 0,
    t[0], t[1], t[2], 1,
  ];
}
function m4Decompose(m) { // {t, q, s} for TRS matrices
  const t = [m[12], m[13], m[14]];
  const sx = Math.hypot(m[0], m[1], m[2]), sy = Math.hypot(m[4], m[5], m[6]), sz = Math.hypot(m[8], m[9], m[10]);
  const m00 = m[0] / sx, m01 = m[4] / sy, m02 = m[8] / sz;
  const m10 = m[1] / sx, m11 = m[5] / sy, m12 = m[9] / sz;
  const m20 = m[2] / sx, m21 = m[6] / sy, m22 = m[10] / sz;
  const tr = m00 + m11 + m22;
  let q;
  if (tr > 0) {
    const S = Math.sqrt(tr + 1) * 2;
    q = [(m21 - m12) / S, (m02 - m20) / S, (m10 - m01) / S, 0.25 * S];
  } else if (m00 > m11 && m00 > m22) {
    const S = Math.sqrt(1 + m00 - m11 - m22) * 2;
    q = [0.25 * S, (m01 + m10) / S, (m02 + m20) / S, (m21 - m12) / S];
  } else if (m11 > m22) {
    const S = Math.sqrt(1 + m11 - m00 - m22) * 2;
    q = [(m01 + m10) / S, 0.25 * S, (m12 + m21) / S, (m02 - m20) / S];
  } else {
    const S = Math.sqrt(1 + m22 - m00 - m11) * 2;
    q = [(m02 + m20) / S, (m12 + m21) / S, 0.25 * S, (m10 - m01) / S];
  }
  const l = Math.hypot(q[0], q[1], q[2], q[3]) || 1;
  return { t, q: [q[0] / l, q[1] / l, q[2] / l, q[3] / l], s: [sx, sy, sz] };
}
function m4Inv(m) {
  const a = m;
  const b00 = a[0] * a[5] - a[1] * a[4], b01 = a[0] * a[6] - a[2] * a[4], b02 = a[0] * a[7] - a[3] * a[4];
  const b03 = a[1] * a[6] - a[2] * a[5], b04 = a[1] * a[7] - a[3] * a[5], b05 = a[2] * a[7] - a[3] * a[6];
  const b06 = a[8] * a[13] - a[9] * a[12], b07 = a[8] * a[14] - a[10] * a[12], b08 = a[8] * a[15] - a[11] * a[12];
  const b09 = a[9] * a[14] - a[10] * a[13], b10 = a[9] * a[15] - a[11] * a[13], b11 = a[10] * a[15] - a[11] * a[14];
  let det = b00 * b11 - b01 * b10 + b02 * b09 + b03 * b08 - b04 * b07 + b05 * b06;
  if (Math.abs(det) < 1e-12) throw new Error("m4Inv: matrix is singular (|det| < 1e-12), cannot invert");
  det = 1 / det;
  return [
    (a[5] * b11 - a[6] * b10 + a[7] * b09) * det, (a[2] * b10 - a[1] * b11 - a[3] * b09) * det,
    (a[1] * b07 - a[2] * b06 + a[3] * b05) * det, (a[0] * b06 - a[1] * b05 + a[2] * b04) * det,
    (a[6] * b08 - a[4] * b11 - a[7] * b07) * det, (a[0] * b11 - a[2] * b08 + a[3] * b07) * det,
    (a[2] * b02 - a[0] * b07 - a[1] * b05) * det, (a[1] * b04 - a[0] * b06 - a[2] * b03) * det,
    (a[4] * b10 - a[5] * b08 + a[7] * b06) * det, (a[1] * b08 - a[0] * b10 - a[3] * b06) * det,
    (a[0] * b05 - a[1] * b02 + a[3] * b01) * det, (a[0] * b04 - a[1] * b01 + a[2] * b00) * det,
    (a[5] * b07 - a[4] * b09 - a[6] * b06) * det, (a[0] * b09 - a[1] * b07 + a[2] * b06) * det,
    (a[4] * b04 - a[5] * b03 + a[6] * b02) * det, (a[0] * b03 - a[1] * b02 + a[2] * b01) * det,
  ];
}
const m4Pos = (m) => [m[12], m[13], m[14]];
// TRS compose with an animated quaternion (keeps bind translation + scale)
function composeTQS(t, q, s) {
  const [x, y, z, w] = q;
  const x2 = x + x, y2 = y + y, z2 = z + z;
  const xx = x * x2, xy = x * y2, xz = x * z2, yy = y * y2, yz = y * z2, zz = z * z2, wx = w * x2, wy = w * y2, wz = w * z2;
  return [
    (1 - (yy + zz)) * s[0], (xy + wz) * s[0], (xz - wy) * s[0], 0,
    (xy - wz) * s[1], (1 - (xx + zz)) * s[1], (yz + wx) * s[1], 0,
    (xz + wy) * s[2], (yz - wx) * s[2], (1 - (xx + yy)) * s[2], 0,
    t[0], t[1], t[2], 1,
  ];
}
// rotate a vector by a quaternion
function qRot(q, v) {
  const [x, y, z, w] = q;
  const [vx, vy, vz] = v;
  // t = 2 q_vec × v
  const tx = 2 * (y * vz - z * vy), ty = 2 * (z * vx - x * vz), tz = 2 * (x * vy - y * vx);
  return [
    vx + w * tx + (y * tz - z * ty),
    vy + w * ty + (z * tx - x * tz),
    vz + w * tz + (x * ty - y * tx),
  ];
}

// ---------- GLB skeleton ----------
function readGlbJson(path) {
  const buf = fs.readFileSync(path);
  if (buf.length < 12 || buf.toString("latin1", 0, 4) !== "glTF") {
    console.error(`不是有效的 GLB 文件(magic 不是 'glTF'): ${path}`);
    process.exit(1);
  }
  const version = buf.readUInt32LE(4);
  if (version !== 2) {
    console.error(`不支持的 GLB 版本 ${version}(仅支持 version 2): ${path}`);
    process.exit(1);
  }
  // chunk iteration is bounded by the real buffer length, not just the header's
  // claimed total, so a truncated file reports one error instead of crashing.
  const declaredLen = buf.readUInt32LE(8);
  const end = Math.min(declaredLen, buf.length);
  let off = 12, json = null;
  while (off + 8 <= end) {
    const clen = buf.readUInt32LE(off), ctype = buf.readUInt32LE(off + 4);
    if (off + 8 + clen > end) {
      console.error(`GLB chunk 越界: 偏移 ${off} 处声明长度 ${clen},只剩 ${end - off - 8} 字节 — ${path}`);
      process.exit(1);
    }
    if (ctype === 0x4e4f534a) json = JSON.parse(buf.subarray(off + 8, off + 8 + clen).toString("utf8"));
    off += 8 + clen;
  }
  if (!json) {
    console.error(`GLB 中未找到 JSON chunk: ${path}`);
    process.exit(1);
  }
  return json;
}
const gltf = readGlbJson(GLB_IN);
const nodes = gltf.nodes;
const nodeByName = new Map(nodes.map((n, i) => [n.name, i]));
const parentOf = new Array(nodes.length).fill(-1);
nodes.forEach((n, i) => (n.children || []).forEach((c) => { parentOf[c] = i; }));
const localM = nodes.map(m4FromNode);
const bindLocal = localM.map(m4Decompose);
const bindWorld = new Array(nodes.length);
{
  const done = new Array(nodes.length).fill(false);
  const walk = (i) => {
    if (done[i]) return bindWorld[i];
    done[i] = true;
    bindWorld[i] = parentOf[i] >= 0 ? m4Mul(walk(parentOf[i]), localM[i]) : localM[i];
    return bindWorld[i];
  };
  for (let i = 0; i < nodes.length; i++) walk(i);
}
const bindQ = bindWorld.map((m) => m4Decompose(m).q);
// sanity: report scale on the Bip001 ancestor chain (composeTQS assumes TRS)
{
  let c = nodeByName.get("Bip001");
  const chain = [];
  while (c >= 0) { chain.push(nodes[c].name); c = parentOf[c]; }
  const scaled = chain.filter((nm) => {
    const s = bindLocal[nodeByName.get(nm)].s;
    return Math.abs(s[0] - 1) > 1e-6 || Math.abs(s[1] - 1) > 1e-6 || Math.abs(s[2] - 1) > 1e-6;
  });
  console.log("Bip001 ancestors:", chain.join(" > "));
  console.log("scaled ancestors:", scaled.length ? scaled.join(", ") : "none");
}

// ---------- VMD ----------
const { modelName, boneFrames } = parseVmd(VMD);
const tracks = tracksPerBone(boneFrames);
console.log(`vmd: model "${modelName}", ${tracks.size} bones, segment ${SEG_A}..${SEG_B}`);
// ---------- startup assertions: critical data must be complete ----------
for (const bone of CRITICAL_BONES) {
  if (!tracks.has(bone)) {
    console.error(`VMD 缺少骨骼 ${bone}（重定向必需）`);
    process.exit(1);
  }
}
for (const nm of CRITICAL_NODES) {
  const ni = nodeByName.get(nm);
  if (ni === undefined) {
    console.error(`GLB 缺少节点 ${nm}（重定向必需）`);
    process.exit(1);
  }
  if (!bindWorld[ni] || !bindLocal[ni]) {
    console.error(`GLB 节点 ${nm} 缺少绑定矩阵`);
    process.exit(1);
  }
}
// optional mapped bones (finger chains, etc.): missing → stays at bind pose.
for (const m of Object.keys(MMD_TO_NODE)) {
  if (CRITICAL_BONES.includes(m)) continue;
  if (!tracks.has(m)) console.log(`  NOTE: VMD has no track for mapped bone ${m}`);
  if (!nodeByName.has(MMD_TO_NODE[m])) console.log(`  NOTE: GLB has no node ${MMD_TO_NODE[m]}`);
}
{
  let mp = 0;
  for (const p of tracks.get("腰").p) mp = Math.max(mp, Math.hypot(p[0], p[1], p[2]));
  console.log(`腰 position magnitude: max=${mp.toFixed(4)} ${mp > 0.01 ? "(non-zero)" : "(zero — ignored)"}`);
}

const frameCount = SEG_B - SEG_A;
const mmdChain = {};
for (const m of Object.keys(MMD_TREE)) {
  const chain = [];
  let cur = m;
  while (cur) { chain.unshift(cur); cur = MMD_TREE[cur]; }
  mmdChain[m] = chain;
}

// per-frame VMD local quats for every MMD bone we may need (mapped targets + folded
// extras like 下半身)
const vmdLocalQ = new Map();
{
  const wanted = new Set([...Object.keys(MMD_TO_NODE), ...Object.values(MMD_EXTRA).flat()]);
  for (const m of wanted) {
    const t = tracks.get(m);
    if (!t) continue;
    const out = new Float32Array(frameCount * 4);
    for (let f = 0; f < frameCount; f++) out.set(sampleTrack(t, SEG_A + f).q, f * 4);
    vmdLocalQ.set(m, out);
  }
}
const conjZ = (q) => [-q[0], -q[1], q[2], q[3]];

const nodeToMmd = new Map(Object.entries(MMD_TO_NODE).map(([m, n]) => [n, m]));
const drivenIdx = new Set([...nodeToMmd.keys()].map((n) => nodeByName.get(n)).filter((x) => x !== undefined));

// static local-rotation recipe per driven node:
//   Rloc(f) = inv(bindWorldQ[parent]) ⊗ conjZ(q_extra ⊗ … ⊗ q_own(f)) ⊗ bindWorldQ[node]
// (extra = MMD bones folded into this node's chain, e.g. 下半身 for the thighs)
const recipe = new Map(); // nodeIndex -> { chain: [mmd names, own last], pre, post }
for (const ni of drivenIdx) {
  const mmd = nodeToMmd.get(nodes[ni].name);
  if (!vmdLocalQ.has(mmd)) continue;
  const pi = parentOf[ni];
  const pre = pi >= 0 ? qInv(bindQ[pi]) : [0, 0, 0, 1];
  recipe.set(ni, { chain: [...(MMD_EXTRA[mmd] || []), mmd], pre, post: bindQ[ni] });
}
const chainQuat = (rec, f) => {
  let q = [0, 0, 0, 1];
  for (const bone of rec.chain) {
    const t = vmdLocalQ.get(bone);
    if (t) q = qMul(q, [t[f * 4], t[f * 4 + 1], t[f * 4 + 2], t[f * 4 + 3]]);
  }
  return q;
};

// animated worlds by FK
function computeAnimWorlds(f, rootDeltaLocal) {
  const world = new Map();
  const get = (i) => {
    let m = world.get(i);
    if (m) return m;
    const pw = parentOf[i] >= 0 ? get(parentOf[i]) : null;
    const rec = recipe.get(i);
    let lm;
    if (rec) {
      const q = qMul(qMul(rec.pre, conjZ(chainQuat(rec, f))), rec.post);
      const bl = bindLocal[i];
      const t = (rootDeltaLocal && nodes[i].name === "Bip001")
        ? [bl.t[0] + rootDeltaLocal[0], bl.t[1] + rootDeltaLocal[1], bl.t[2] + rootDeltaLocal[2]]
        : bl.t;
      lm = composeTQS(t, q, bl.s);
    } else {
      lm = localM[i];
    }
    m = pw ? m4Mul(pw, lm) : lm;
    world.set(i, m);
    return m;
  };
  const need = new Set();
  for (const d of drivenIdx) { let c = d; while (c >= 0 && !need.has(c)) { need.add(c); c = parentOf[c]; } }
  for (const n of need) get(n);
  return world;
}

// root translation in Bip001's parent frame: the MMD centre offset lives in world
// axes, so rotate S·(x,y,-z) (+dy) into the parent's bind frame (parent never animates)
const bip001 = nodeByName.get("Bip001");
const bip001ParentQ = parentOf[bip001] >= 0 ? bindQ[parentOf[bip001]] : [0, 0, 0, 1];

// ---------- root translation: in-place centre ----------
const centerT = tracks.get("センター");
const cPos = [];
for (let f = 0; f < frameCount; f++) cPos.push(sampleTrack(centerT, SEG_A + f).p);
const BASE_WIN = Math.round(2.5 * FPS);
const baseXZ = cPos.map((_, i) => {
  let sx = 0, sz = 0, n = 0;
  for (let j = Math.max(0, i - BASE_WIN); j <= Math.min(frameCount - 1, i + BASE_WIN); j++) { sx += cPos[j][0]; sz += cPos[j][2]; n++; }
  return [sx / n, sz / n];
});
// world-frame delta (x, y, -z) before scaling; drift removed from x/z
const rootDeltaWorld = cPos.map((p, i) => [p[0] - baseXZ[i][0], p[1], -(p[2] - baseXZ[i][1])]);

// ---------- calibration ----------
const SAMPLE_STEP = 5;
const samples = [];
for (let f = 0; f < frameCount; f += SAMPLE_STEP) {
  const world = computeAnimWorlds(f, null);
  const fy = Math.min(
    m4Pos(world.get(nodeByName.get("Bip001LFoot")))[1],
    m4Pos(world.get(nodeByName.get("Bip001RFoot")))[1]);
  samples.push({ py: rootDeltaWorld[f][1], relFootY: fy });
}
function theilSen(pts) {
  const slopes = [];
  for (let i = 0; i < pts.length; i++) for (let j = i + 1; j < pts.length; j++) {
    const dp = pts[j].py - pts[i].py;
    if (Math.abs(dp) > 0.02) slopes.push((pts[j].relFootY - pts[i].relFootY) / dp);
  }
  if (!slopes.length) throw new Error("calibration degenerate: py never varies >0.02");
  slopes.sort((a, b) => a - b);
  return slopes[Math.floor(slopes.length / 2)];
}
const S = -theilSen(samples);
const residuals = samples.map((s) => S * s.py + s.relFootY).sort((a, b) => a - b);
const dy = -residuals[Math.floor(residuals.length / 2)];
const iqr = residuals[Math.floor(residuals.length * 0.75)] - residuals[Math.floor(residuals.length * 0.25)];
console.log(`calibration: S=${S.toFixed(5)} (${(S * 100).toFixed(2)} cm/MMD unit), dy=${dy.toFixed(4)}, planted-residual IQR=${(iqr * 100).toFixed(2)} cm`);

// root delta in Bip001's parent-local frame, per frame (parent never animates, so a
// static inverse maps world axes into its local frame)
function rootDeltaLocalAt(f) {
  const d = rootDeltaWorld[f];
  const v = qRot(qInv(bip001ParentQ), [d[0] * S, d[1] * S + dy, d[2] * S]);
  return v;
}

// ---------- per-frame local tracks ----------
const times = new Float32Array(frameCount);
for (let f = 0; f < frameCount; f++) times[f] = f / FPS;
const animQ = new Map();
const animRootT = new Float32Array(frameCount * 3);
for (let f = 0; f < frameCount; f++) {
  const world = computeAnimWorlds(f, rootDeltaLocalAt(f));
  for (const ni of drivenIdx) {
    const rec = recipe.get(ni);
    if (!rec) continue;
    const pi = parentOf[ni];
    const q = qMul(qMul(rec.pre, conjZ(chainQuat(rec, f))), rec.post);
    let a = animQ.get(ni);
    if (!a) { a = new Float32Array(frameCount * 4); animQ.set(ni, a); }
    a.set(q, f * 4);
  }
  const w = world.get(bip001);
  animRootT.set([w[12], w[13], w[14]], f * 3);
}

// ---------- validation ----------
let nan = 0;
for (const a of animQ.values()) for (const v of a) if (!Number.isFinite(v)) nan++;
for (const v of animRootT) if (!Number.isFinite(v)) nan++;
console.log("non-finite values:", nan);
{
  const ys = [];
  for (let f = 0; f < frameCount; f += 5) {
    const world = computeAnimWorlds(f, rootDeltaLocalAt(f));
    ys.push(Math.min(m4Pos(world.get(nodeByName.get("Bip001LFoot")))[1], m4Pos(world.get(nodeByName.get("Bip001RFoot")))[1]));
  }
  ys.sort((a, b) => a - b);
  const p = (q) => (ys[Math.min(ys.length - 1, Math.floor(ys.length * q))] * 100).toFixed(2);
  console.log(`foot Y after apply (cm): min=${p(0)} p5=${p(0.05)} p50=${p(0.5)} p95=${p(0.95)} max=${p(1)}`);
}
console.log("\nlocal deviation from bind (deg):");
for (const [ni, a] of animQ) {
  const bl = bindLocal[ni].q;
  let sum = 0, mx = 0;
  const n = a.length / 4;
  for (let f = 0; f < n; f++) {
    const ang = qAngle([a[f * 4], a[f * 4 + 1], a[f * 4 + 2], a[f * 4 + 3]], bl);
    sum += ang; mx = Math.max(mx, ang);
  }
  if (mx > 0.05) console.log(`  ${nodes[ni].name.padEnd(20)} mean=${((sum / n) * 180 / Math.PI).toFixed(1).padStart(6)} max=${(mx * 180 / Math.PI).toFixed(1).padStart(6)}`);
}

// ---------- decimate ----------
function decimate(timesIn, valuesIn, stride, tol) {
  const n = timesIn.length;
  const keep = new Array(n).fill(false);
  keep[0] = keep[n - 1] = true;
  let active = 2;
  const dist = (i) => {
    let lo = i - 1, hi = i + 1;
    while (lo > 0 && !keep[lo]) lo--;
    while (hi < n - 1 && !keep[hi]) hi++;
    const u = (timesIn[i] - timesIn[lo]) / (timesIn[hi] - timesIn[lo]);
    if (stride === 4) {
      const a = [valuesIn[lo * 4], valuesIn[lo * 4 + 1], valuesIn[lo * 4 + 2], valuesIn[lo * 4 + 3]];
      const b = [valuesIn[hi * 4], valuesIn[hi * 4 + 1], valuesIn[hi * 4 + 2], valuesIn[hi * 4 + 3]];
      const v = [valuesIn[i * 4], valuesIn[i * 4 + 1], valuesIn[i * 4 + 2], valuesIn[i * 4 + 3]];
      return qAngle(v, qSlerp(a, b, u));
    }
    let best = 0;
    for (let k = 0; k < stride; k++) {
      const e = valuesIn[lo * stride + k] + (valuesIn[hi * stride + k] - valuesIn[lo * stride + k]) * u;
      best = Math.max(best, Math.abs(valuesIn[i * stride + k] - e));
    }
    return best;
  };
  while (active < n) {
    let bi = -1, bd = -1;
    for (let i = 1; i < n - 1; i++) {
      if (keep[i]) continue;
      const d = dist(i);
      if (d > bd) { bd = d; bi = i; }
    }
    if (bi < 0 || bd <= tol) break;
    keep[bi] = true; active++;
  }
  const ti = [], vi = [];
  for (let i = 0; i < n; i++) if (keep[i]) { ti.push(timesIn[i]); for (let k = 0; k < stride; k++) vi.push(valuesIn[i * stride + k]); }
  return { times: ti, values: vi };
}

const outTracks = {};
let keys = 0;
for (const [ni, a] of animQ) {
  const r = decimate(times, a, 4, DECIM_ROT);
  outTracks[nodes[ni].name] = { type: "quat", times: r.times, values: r.values };
  keys += r.times.length;
}
{
  const r = decimate(times, animRootT, 3, DECIM_POS);
  outTracks["__root_position"] = { type: "vec3", times: r.times, values: r.values };
  keys += r.times.length;
}
console.log(`\ndecimated keys: ${keys} (raw would be ${frameCount * (animQ.size + 1)})`);

fs.writeFileSync(OUT_JSON, JSON.stringify({
  meta: { segment: [SEG_A, SEG_B], fps: FPS, duration: frameCount / FPS, S, dy, model: modelName, source: VMD, space: "mirror-Z, glb local space" },
  tracks: outTracks,
}));
console.log("wrote", OUT_JSON, (fs.statSync(OUT_JSON).size / 1024).toFixed(0), "KB");
