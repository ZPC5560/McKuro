// Minimal VMD (MikuMikuDance motion) parser — bone keyframes only.
// Format reference: "Vocaloid Motion Data 0002", Shift-JIS bone names (15 bytes),
// frame u32, position 3xf32, quaternion 4xf32 (x,y,z,w), 64-byte bezier interp (ignored).
"use strict";
const fs = require("fs");

let sjisDecoder = null;
try { sjisDecoder = new TextDecoder("shift_jis"); } catch (e) { /* fallback below */ }

function readBoneName(buf, off) {
  let s;
  if (sjisDecoder) s = sjisDecoder.decode(buf.subarray(off, off + 15));
  else s = buf.toString("latin1", off, off + 15);
  const z = s.indexOf("\0");
  return (z >= 0 ? s.slice(0, z) : s).trim();
}

function parseVmd(path) {
  const b = fs.readFileSync(path);
  if (b.toString("binary", 0, 30).startsWith("Vocaloid Motion Data 0002")) {
    // 0002: 30-byte sig + 20-byte model name
  } else if (b.toString("binary", 0, 26).startsWith("Vocaloid Motion Data file")) {
    // 0001: 26-byte sig + 10-byte model name
  } else {
    throw new Error("not a VMD file: " + path);
  }
  let o = 30;
  const v2 = b.toString("binary", 0, 30).startsWith("Vocaloid Motion Data 0002");
  const nameLen = v2 ? 20 : 10;
  const raw = b.subarray(o, o + nameLen);
  let modelName;
  try { modelName = (sjisDecoder ? sjisDecoder.decode(raw) : raw.toString("latin1")).replace(/\0.*$/, "").trim(); } catch (e) { modelName = ""; }
  o += nameLen;

  const boneFrames = [];
  if (o + 4 > b.length) throw new Error(`VMD 文件截断: 关键帧数量 需要 4 字节只剩 ${b.length - o}`);
  const count = b.readUInt32LE(o); o += 4;
  // one bone keyframe = 15 (name) + 4 (frame) + 12 (pos) + 16 (quat) + 64 (bezier) = 111 bytes
  const KEYFRAME_BYTES = 111;
  const needBytes = count * KEYFRAME_BYTES;
  if (b.length - o < needBytes) throw new Error(`VMD 文件截断: 骨骼关键帧 需要 ${needBytes} 字节只剩 ${b.length - o}`);
  for (let i = 0; i < count; i++) {
    const name = readBoneName(b, o); o += 15;
    const frame = b.readUInt32LE(o); o += 4;
    const px = b.readFloatLE(o), py = b.readFloatLE(o + 4), pz = b.readFloatLE(o + 8); o += 12;
    const qx = b.readFloatLE(o), qy = b.readFloatLE(o + 4), qz = b.readFloatLE(o + 8), qw = b.readFloatLE(o + 12); o += 16;
    o += 64; // bezier interpolation
    boneFrames.push({ name, frame, p: [px, py, pz], q: [qx, qy, qz, qw] });
  }
  return { modelName, boneFrames };
}

// Group keyframes per bone, sorted by frame index. Bone names are normalised:
// full-width digits (１２３) → ASCII, so mappings can use one spelling.
function tracksPerBone(boneFrames) {
  const map = new Map();
  for (const f of boneFrames) {
    const name = f.name.replace(/[０-９]/g, (c) => String.fromCharCode(c.charCodeAt(0) - 0xFEE0));
    let t = map.get(name);
    if (!t) { t = { name, frames: [], p: [], q: [] }; map.set(name, t); }
    t.frames.push(f.frame);
    t.p.push(f.p);
    t.q.push(f.q);
  }
  for (const t of map.values()) {
    const order = [...t.frames.keys()].sort((a, c) => t.frames[a] - t.frames[c]);
    t.frames = order.map((i) => t.frames[i]);
    t.p = order.map((i) => t.p[i]);
    t.q = order.map((i) => t.q[i]);
  }
  return map;
}

// --- quaternion helpers (all functions take/return [x,y,z,w]) ---
const qMul = (a, b) => [
  a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1],
  a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0],
  a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3],
  a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2],
];
const qConj = (a) => [-a[0], -a[1], -a[2], a[3]];
const qInv = (a) => {
  const l2 = a[0] * a[0] + a[1] * a[1] + a[2] * a[2] + a[3] * a[3] || 1;
  return [-a[0] / l2, -a[1] / l2, -a[2] / l2, a[3] / l2];
};
const qNorm = (a) => {
  const l = Math.hypot(a[0], a[1], a[2], a[3]) || 1;
  return [a[0] / l, a[1] / l, a[2] / l, a[3] / l];
};
// shortest-path slerp
function qSlerp(a, b, t) {
  let d = a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3];
  let bb = b;
  if (d < 0) { bb = [-b[0], -b[1], -b[2], -b[3]]; d = -d; }
  if (d > 0.9995) {
    return qNorm([
      a[0] + (bb[0] - a[0]) * t, a[1] + (bb[1] - a[1]) * t,
      a[2] + (bb[2] - a[2]) * t, a[3] + (bb[3] - a[3]) * t,
    ]);
  }
  const th = Math.acos(Math.min(1, d));
  const s = Math.sin(th);
  const wa = Math.sin((1 - t) * th) / s, wb = Math.sin(t * th) / s;
  return [a[0] * wa + bb[0] * wb, a[1] * wa + bb[1] * wb, a[2] * wa + bb[2] * wb, a[3] * wa + bb[3] * wb];
}
const qAngle = (a, b) => {
  const d = Math.abs(a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3]);
  return 2 * Math.acos(Math.min(1, d));
};

// Sample a bone track linearly (nearest keyframe interpolation is too steppy at
// segment boundaries; slerp between surrounding keys matches three's LINEAR quats).
function sampleTrack(t, frame) {
  const frames = t.frames;
  if (frame <= frames[0]) return { p: t.p[0], q: t.q[0] };
  if (frame >= frames[frames.length - 1]) return { p: t.p[t.p.length - 1], q: t.q[t.q.length - 1] };
  // binary search
  let lo = 0, hi = frames.length - 1;
  while (hi - lo > 1) { const mid = (lo + hi) >> 1; if (frames[mid] <= frame) lo = mid; else hi = mid; }
  const f0 = frames[lo], f1 = frames[hi];
  const u = (frame - f0) / (f1 - f0);
  return {
    p: [0, 1, 2].map((k) => t.p[lo][k] + (t.p[hi][k] - t.p[lo][k]) * u),
    q: qSlerp(t.q[lo], t.q[hi], u),
  };
}

module.exports = { parseVmd, tracksPerBone, qMul, qConj, qInv, qNorm, qSlerp, qAngle, sampleTrack };
