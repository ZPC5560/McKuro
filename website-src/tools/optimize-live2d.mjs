/* Optimise the raw 心 dual-form PuppetLoom package into the assets the site ships.
 *
 *   node website-src/tools/optimize-live2d.mjs \
 *        <raw-package-dir> <out-dir>            # out-dir defaults to website-src/live2d
 *
 * The raw package (下载的 心_双形态_手脚轻摆_r33-r35_*.zip 解压目录) is NOT committed:
 * it holds a per-form copy of the runtime and unminified project JSON. This script
 * turns it into what `build.py` copies verbatim:
 *
 *   <out>/puppetloom-web.js          single shared runtime (both forms were byte-identical)
 *   <out>/manifest.json              provenance: revisions, fingerprints, source hashes, sizes
 *   <out>/form1/project.json         minified, floats rounded, texture refs -> .webp
 *   <out>/form1/textures/*.webp      lossless WebP (visually identical, ~46% of the PNGs)
 *   <out>/form2/...                  same for the second form
 *
 * Losslessness is the point: the artwork is machine-exported 2D character art, so any
 * encode loss would show up as shimmer on hair strands between forms. Lossless WebP was
 * measured byte-different only on fully transparent pixels (visibleDiff = 0), and the
 * float rounding is 5 decimal places on 0–1 normalised coordinates — 0.01 px at the
 * 1024 px canvas the runtime draws into.
 *
 * Requires `sharp`. Run it from a directory that can resolve it (`E:\donet\.site-build`
 * carries it for the other website pipelines), e.g.:
 *   cd E:\donet\.site-build && node E:\donet\McKuro\website-src\tools\optimize-live2d.mjs <raw> <out>
 */
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { createRequire } from "node:module";

// Resolve sharp from the current working directory as well as from this file: the image
// pipelines that carry it live in a sibling tooling folder, so `cd` there before running.
const require = createRequire(path.join(process.cwd(), "noop.js"));
let sharp;
try {
  sharp = require("sharp");
} catch {
  console.error("需要 sharp: 在携带它的目录里运行（如 E:\\donet\\.site-build），或先 `npm i sharp`。");
  process.exit(1);
}

const RAW = path.resolve(process.argv[2] || "");
const OUT = path.resolve(process.argv[3] || path.join(import.meta.dirname, "..", "live2d"));
if (!process.argv[2] || !fs.existsSync(RAW)) {
  console.error("用法: node optimize-live2d.mjs <原始包目录> [输出目录]");
  process.exit(1);
}

const DECIMALS = 5;                 // 0–1 coords -> 0.01 px at 1024 px; invisible, ~16% smaller
const FORMS = [["form1", "红黑形态"], ["form2", "白金形态"]];

const mb = (b) => (b / 1048576).toFixed(2);
const sha = (p) => crypto.createHash("sha256").update(fs.readFileSync(p)).digest("hex");

/** Round every non-integer number to DECIMALS places, preserving array/field shape. */
function roundFloats(v) {
  if (Array.isArray(v)) return v.map(roundFloats);
  if (v && typeof v === "object") {
    const out = {};
    for (const k of Object.keys(v)) out[k] = roundFloats(v[k]);
    return out;
  }
  if (typeof v === "number" && !Number.isInteger(v)) {
    const m = 10 ** DECIMALS;
    return Math.round(v * m) / m;
  }
  return v;
}

fs.mkdirSync(OUT, { recursive: true });

const manifest = {
  generatedBy: "website-src/tools/optimize-live2d.mjs",
  note: "由原始展示包离线优化而来；原始包不入库。",
  runtime: null,
  forms: [],
};

// ---- runtime: one copy, both forms shipped the same bytes ----
const runtimeSrc = path.join(RAW, "models", "form1", "puppetloom-web.js");
const other = path.join(RAW, "models", "form2", "puppetloom-web.js");
if (sha(runtimeSrc) !== sha(other)) {
  throw new Error("两个形态的运行时不再相同，去重前提已失效，请改为各存一份。");
}
fs.copyFileSync(runtimeSrc, path.join(OUT, "puppetloom-web.js"));
manifest.runtime = {
  file: "puppetloom-web.js",
  sha256: sha(runtimeSrc),
  bytes: fs.statSync(runtimeSrc).size,
};
console.log(`runtime  ${manifest.runtime.bytes} B (两形态同一文件, 已去重)`);

// ---- forms ----
let srcTotal = 0, outTotal = manifest.runtime.bytes;

for (const [form, label] of FORMS) {
  const projDir = path.join(RAW, "models", form, "project");
  const projRaw = path.join(projDir, "puppetloom.json");
  const project = JSON.parse(fs.readFileSync(projRaw, "utf8"));
  if (project.version !== 4) {
    throw new Error(`${form}: 运行时要求项目 version 4，实际 ${project.version}`);
  }
  srcTotal += fs.statSync(projRaw).size;

  const outDir = path.join(OUT, form);
  fs.mkdirSync(path.join(outDir, "textures"), { recursive: true });

  // ---- textures: PNG -> lossless WebP, rewriting each layer's reference ----
  let texIn = 0, texOut = 0, texCount = 0;
  for (const layer of project.layers) {
    const rel = String(layer.texture).replace(/\\/g, "/");          // e.g. textures/layer-000-*.png
    if (!rel.toLowerCase().endsWith(".png")) throw new Error(`${form}: 非 PNG 纹理 ${rel}`);
    const src = path.join(projDir, rel);
    if (!fs.existsSync(src)) throw new Error(`${form}: 缺少纹理 ${rel}`);
    const dstRel = rel.replace(/\.png$/i, ".webp");
    const dst = path.join(outDir, dstRel);
    fs.mkdirSync(path.dirname(dst), { recursive: true });
    const buf = await sharp(src).webp({ lossless: true, effort: 5 }).toBuffer();
    fs.writeFileSync(dst, buf);
    texIn += fs.statSync(src).size;
    texOut += buf.length;
    texCount++;
    layer.texture = dstRel;
  }

  const json = Buffer.from(JSON.stringify(roundFloats(project)), "utf8");
  fs.writeFileSync(path.join(outDir, "project.json"), json);
  srcTotal += texIn;
  outTotal += json.length + texOut;

  manifest.forms.push({
    form,
    label,
    revision: JSON.parse(fs.readFileSync(projRaw, "utf8")).version === 4
      ? (JSON.parse(fs.readFileSync(path.join(RAW, "models", form, "web-runtime-manifest.json"), "utf8")).revision)
      : null,
    projectJson: { file: `${form}/project.json`, bytes: json.length, sha256: sha(path.join(outDir, "project.json")) },
    textures: { count: texCount, bytes: texOut, format: "webp/lossless" },
    source: {
      projectJsonBytes: fs.statSync(projRaw).size,
      textureBytes: texIn,
      projectJsonSha256: sha(projRaw),
    },
  });

  console.log(`\n${form} (${label})`);
  console.log(`  project.json ${mb(fs.statSync(projRaw).size)} MB -> ${mb(json.length)} MB`);
  console.log(`  textures     ${mb(texIn)} MB -> ${mb(texOut)} MB  (${texCount} files, lossless webp)`);
}

fs.writeFileSync(path.join(OUT, "manifest.json"), JSON.stringify(manifest, null, 2) + "\n");
outTotal += fs.statSync(path.join(OUT, "manifest.json")).size;

console.log(`\n合计 ${mb(srcTotal)} MB -> ${mb(outTotal)} MB  (输出 ${path.relative(process.cwd(), OUT)})`);
