// Analyse the dance: センター travel, and find good loop segments (pose-matched seams).
"use strict";
const { parseVmd, tracksPerBone, qAngle, sampleTrack } = require("./vmd");

const VMD = "E:/donet/McKuro/玉兰开花三月三_动作_by_鲸落璃沙郊_2d24dce03c1f8211d4d9c60f803fd5d7/玉兰开花三月三_动作.vmd";
const { modelName, boneFrames } = parseVmd(VMD);
const tracks = tracksPerBone(boneFrames);

console.log("model:", modelName);
const center = tracks.get("センター");
if (!center) { console.error("VMD 缺少骨骼 センター"); process.exit(1); }
const groove = tracks.get("グルーブ");
if (!groove) { console.error("VMD 缺少骨骼 グルーブ"); process.exit(1); }
const maxF = Math.max(...[...tracks.values()].map((t) => t.frames[t.frames.length - 1]));
console.log("last frame:", maxF, "=", (maxF / 30).toFixed(1), "s @30fps");

// centre/groove travel over time (sampleTrack interpolates position linearly and
// rotation via slerp between the surrounding keyframes)
console.log("\ncenter (x, y, z) samples every 300 frames:");
for (let f = 0; f <= maxF; f += 300) {
  const c = sampleTrack(center, f), g = sampleTrack(groove, f);
  console.log(`  f=${String(f).padStart(4)} t=${(f / 30).toFixed(0).padStart(3)}s  c=(${c.p.map((v) => v.toFixed(2)).join(", ")})  g=(${g.p.map((v) => v.toFixed(2)).join(", ")})`);
}

// overall ranges
let minX = 1e9, maxX = -1e9, minY = 1e9, maxY = -1e9, minZ = 1e9, maxZ = -1e9;
for (const p of center.p) {
  minX = Math.min(minX, p[0]); maxX = Math.max(maxX, p[0]);
  minY = Math.min(minY, p[1]); maxY = Math.max(maxY, p[1]);
  minZ = Math.min(minZ, p[2]); maxZ = Math.max(maxZ, p[2]);
}
console.log(`\ncenter range x:[${minX.toFixed(2)}, ${maxX.toFixed(2)}] y:[${minY.toFixed(2)}, ${maxY.toFixed(2)}] z:[${minZ.toFixed(2)}, ${maxZ.toFixed(2)}]`);

// ---- loop search: find (a, b) with pose(b) ~= pose(a), b - a in [12s, 60s] ----
// pose distance: mean quaternion angle over the mapped bones + centre pos distance.
const BONES = ["センター", "腰", "下半身", "上半身", "上半身2", "首", "頭",
  "左肩", "右肩", "左腕", "右腕", "左ひじ", "右ひじ", "左手首", "右手首",
  "左足", "右足", "左ひざ", "右ひざ", "左足首", "右足首"];
const fps = 30;
const poseAt = (f) => {
  const qs = [], ps = [];
  for (const name of BONES) {
    const t = tracks.get(name);
    if (!t) continue;
    const s = sampleTrack(t, f);
    qs.push(s.q); ps.push(s.p);
  }
  return { qs, ps };
};
const poseDist = (A, B) => {
  let d = 0;
  for (let i = 0; i < A.qs.length; i++) d += qAngle(A.qs[i], B.qs[i]);
  d /= A.qs.length;
  let pd = 0;
  for (let i = 0; i < A.ps.length; i++) pd += Math.hypot(A.ps[i][0] - B.ps[i][0], A.ps[i][1] - B.ps[i][1], A.ps[i][2] - B.ps[i][2]);
  pd /= A.ps.length;
  return d + pd * 0.15; // ~1 unit of position ≈ 15° of rotation
};

const candidates = [];
const step = 15; // 0.5s
for (let a = 0; a + 12 * fps <= maxF; a += step) {
  for (let dur = 12 * fps; dur <= 60 * fps && a + dur <= maxF; dur += step) {
    const b = a + dur;
    const d = poseDist(poseAt(a), poseAt(b));
    candidates.push({ a, b, dur: (b - a) / fps, d });
  }
}
candidates.sort((x, y) => x.d - y.d);
console.log("\nbest loop seams (a, b, duration, mean pose distance in rad):");
for (const c of candidates.slice(0, 12)) {
  console.log(`  a=${c.a} (${(c.a / 30).toFixed(1)}s) -> b=${c.b} (${(c.b / 30).toFixed(1)}s)  dur=${c.dur.toFixed(1)}s  d=${c.d.toFixed(4)}`);
}
