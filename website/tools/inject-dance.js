// Inject the retargeted dance into the skinned GLB as a glTF animation, and drop
// NORMAL attributes (unlit materials never sample them).
//
// Usage: node inject-dance.js <model.glb> <dance-tracks.json> <out.glb>
"use strict";
const fs = require("fs");
const { NodeIO } = require("@gltf-transform/core");
const { ALL_EXTENSIONS } = require("@gltf-transform/extensions");

const [inGlb, inJson, outGlb] = process.argv.slice(2);
if (!inGlb || !inJson || !outGlb) {
  console.error("usage: node inject-dance.js <model.glb> <dance-tracks.json> <out.glb>");
  process.exit(2);
}

let dance;
try {
  dance = JSON.parse(fs.readFileSync(inJson, "utf8"));
} catch (e) {
  console.error(`读取 dance-tracks.json 失败: ${e.message}`);
  process.exit(1);
}
if (!dance || typeof dance.tracks !== "object" || dance.tracks === null) {
  console.error("读取 dance-tracks.json 失败: 缺少 tracks 对象");
  process.exit(1);
}
if (!dance.meta || typeof dance.meta.duration !== "number" || !Number.isFinite(dance.meta.duration)) {
  console.error("读取 dance-tracks.json 失败: 缺少 meta.duration（数值）");
  process.exit(1);
}
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
io.read(inGlb).then((doc) => main(doc)).catch((e) => { console.error(e); process.exit(1); });

async function main(doc) {
const root = doc.getRoot();

const buffer = root.listBuffers()[0];
if (!buffer) {
  console.error("GLB 中没有任何 buffer，无法创建动画 accessor");
  process.exit(1);
}

const nodeByName = new Map(root.listNodes().map((n) => [n.getName(), n]));

const anim = doc.createAnimation("yulan-bloom-dance");
let channels = 0, keys = 0;
for (const [name, track] of Object.entries(dance.tracks)) {
  const nodeName = name === "__root_position" ? "Bip001" : name;
  const node = nodeByName.get(nodeName);
  if (!node) { console.error("missing node for track:", name); process.exit(3); }

  const times = Float32Array.from(track.times);
  const values = Float32Array.from(track.values);
  if (track.type === "quat") {
    for (let i = 0; i < values.length; i += 4) {
      const l = Math.hypot(values[i], values[i + 1], values[i + 2], values[i + 3]) || 1;
      values[i] /= l; values[i + 1] /= l; values[i + 2] /= l; values[i + 3] /= l;
    }
  }
  const input = doc.createAccessor()
    .setType("SCALAR").setArray(times).setBuffer(buffer);
  const output = doc.createAccessor()
    .setType(track.type === "quat" ? "VEC4" : "VEC3").setArray(values).setBuffer(buffer);
  const sampler = doc.createAnimationSampler()
    .setInput(input).setOutput(output).setInterpolation("LINEAR");
  anim.addSampler(sampler);
  const channel = doc.createAnimationChannel()
    .setSampler(sampler)
    .setTargetNode(node)
    .setTargetPath(track.type === "quat" ? "rotation" : "translation");
  anim.addChannel(channel);
  channels++;
  keys += track.times.length;
}

// unlit + no normal maps ⇒ normals are dead weight
let stripped = 0;
for (const mesh of root.listMeshes()) {
  for (const prim of mesh.listPrimitives()) {
    if (prim.getAttribute("NORMAL")) { prim.setAttribute("NORMAL", null); stripped++; }
  }
}

await io.write(outGlb, doc);
console.log(`injected: ${channels} channels, ${keys} keys, duration ${dance.meta.duration.toFixed(2)}s; stripped ${stripped} NORMAL attrs`);
console.log("wrote", outGlb, (fs.statSync(outGlb).size / 1024 / 1024).toFixed(2), "MB");
}
