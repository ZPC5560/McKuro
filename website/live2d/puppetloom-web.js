function T(e, t, n) {
  return Math.min(n, Math.max(t, e));
}
function Pr(e, t, n, o) {
  if (t.blinkMode === "geometry" || !["eyeWhite", "iris", "eyelash"].includes(t.role))
    return n;
  const i = Math.max(0, Math.min(1, o)), s = i * i * (3 - 2 * i);
  return { x: n.x, y: t.pivot.y + (n.y - t.pivot.y) * (1 - s * 0.72) };
}
const Sr = {
  "head-yaw": "headYaw",
  "head-pitch": "headPitch",
  "head-roll": "headRoll",
  "body-sway": "bodySway",
  "body-pitch": "bodyPitch",
  "body-roll": "bodyRoll",
  "gaze-x": "gazeX",
  "gaze-y": "gazeY",
  breath: "breath",
  blink: "blink",
  "blink-left": "blinkLeft",
  "blink-right": "blinkRight",
  "brow-left": "browLeft",
  "brow-right": "browRight",
  smile: "smile",
  "cheek-puff": "cheekPuff",
  "mouth-open": "mouthOpen",
  "mouth-a": "mouthA",
  "mouth-i": "mouthI",
  "mouth-u": "mouthU",
  "mouth-e": "mouthE",
  "mouth-o": "mouthO",
  "arm-left": "armLeft",
  "arm-right": "armRight",
  "hand-left-x": "handLeftX",
  "hand-left-y": "handLeftY",
  "hand-right-x": "handRightX",
  "hand-right-y": "handRightY",
  "hand-left-open": "handLeftOpen",
  "hand-right-open": "handRightOpen",
  "ear-x": "earX",
  "ear-y": "earY",
  "tail-x": "tailX",
  "tail-y": "tailY"
};
function Mr(e, t) {
  const n = e.runtime.constraints?.motionLimits ?? [];
  if (n.length === 0)
    return t;
  const o = { ...t };
  for (const i of n) {
    const s = Sr[i.semantic], r = s ? o[s] : void 0;
    s && typeof r == "number" && (o[s] = Math.max(i.min, Math.min(i.max, r)));
  }
  return o;
}
function Rr(e) {
  if (e.length === 0)
    return;
  const t = e.map((a) => a.x), n = e.map((a) => a.y), o = Math.min(...t), i = Math.min(...n), s = Math.max(...t), r = Math.max(...n);
  return { x: o, y: i, width: s - o, height: r - i };
}
function Er(e, t, n) {
  const o = t.x - n - (e.x + e.width), i = t.x + t.width + n - e.x, s = t.y - n - (e.y + e.height), r = t.y + t.height + n - e.y;
  return o >= 0 || i <= 0 || s >= 0 || r <= 0 ? { x: 0, y: 0 } : [{ x: o, y: 0 }, { x: i, y: 0 }, { x: 0, y: s }, { x: 0, y: r }].sort((c, u) => Math.hypot(c.x, c.y) - Math.hypot(u.x, u.y))[0];
}
function ii(e, t, n) {
  const o = e.runtime.constraints?.collisions.filter((s) => s.movingLayerIds.includes(t.id)) ?? [];
  if (o.length === 0)
    return n;
  let i = n;
  for (const s of o)
    for (const r of s.colliderLayerIds) {
      const a = e.layers.find((f) => f.id === r && f.visible !== !1), c = Rr(i);
      if (!a || !c)
        continue;
      const u = Er(c, a.bounds, s.padding), h = Math.hypot(u.x, u.y);
      if (h <= 1e-9)
        continue;
      const d = Math.min(1, s.maxCorrection / h) * s.strength;
      i = i.map((f) => ({ x: f.x + u.x * d, y: f.y + u.y * d }));
    }
  return i;
}
function ri(e, t) {
  const n = e.production;
  if (!n)
    return { variants: {}, props: [], parameters: {}, expressions: {} };
  const o = t?.presetId ? n.presets.find((r) => r.id === t.presetId) : void 0, i = Object.fromEntries(n.variants.map((r) => [r.id, r.defaultOptionId]));
  Object.assign(i, o?.variants ?? {}, t?.variants ?? {});
  const s = t?.props ?? o?.props ?? n.props.filter((r) => r.defaultEnabled).map((r) => r.id);
  return {
    ...o ? { presetId: o.id } : {},
    variants: i,
    props: [...new Set(s)],
    parameters: { ...o?.parameters ?? {} },
    expressions: { ...o?.expressions ?? {} }
  };
}
function zr(e, t, n) {
  if (!e.production)
    return !0;
  const o = ri(e, n.characterState);
  for (const s of e.production.variants) {
    const r = s.options.filter((a) => a.layerIds.includes(t));
    if (r.length > 0 && !r.some((a) => a.id === o.variants[s.id]))
      return !1;
  }
  const i = e.production.props.filter((s) => s.layerIds.includes(t));
  return !(i.length > 0 && !i.some((s) => o.props.includes(s.id)));
}
function Cr(e, t) {
  const n = ri(e, t.characterState);
  return Object.keys(n.parameters).length === 0 && Object.keys(n.expressions).length === 0 ? t : {
    ...t,
    parameters: { ...n.parameters, ...t.parameters ?? {} },
    expressions: { ...n.expressions, ...t.expressions ?? {} }
  };
}
function si(e, t, n, o) {
  const i = (n.y - o.y) * (t.x - o.x) + (o.x - n.x) * (t.y - o.y);
  if (Math.abs(i) < 1e-12)
    return;
  const s = ((n.y - o.y) * (e.x - o.x) + (o.x - n.x) * (e.y - o.y)) / i, r = ((o.y - t.y) * (e.x - o.x) + (t.x - o.x) * (e.y - o.y)) / i, a = 1 - s - r;
  return [s, r, a];
}
function Tr(e, t, n, o) {
  const i = si(e, t, n, o);
  return i?.every((s) => s >= -1e-7) ? i : void 0;
}
function Lr(e, t) {
  let n, o, i = Number.POSITIVE_INFINITY;
  for (let a = 0; a < e.triangles.length; a += 3) {
    const c = e.triangles[a], u = e.triangles[a + 1], h = e.triangles[a + 2];
    if (c === void 0 || u === void 0 || h === void 0)
      continue;
    const d = e.uvs[c], f = e.uvs[u], m = e.uvs[h];
    if (!d || !f || !m)
      continue;
    const l = Tr(t, d, f, m);
    if (l)
      return { indices: [c, u, h], weights: l };
    const p = [d, f, m], x = [c, u, h];
    for (let k = 0; k < 3; k += 1) {
      const w = p[k], P = p[(k + 1) % 3], M = P.x - w.x, _ = P.y - w.y, B = M * M + _ * _, $ = B <= 1e-12 ? 0 : Math.max(0, Math.min(1, ((t.x - w.x) * M + (t.y - w.y) * _) / B)), z = (t.x - (w.x + M * $)) ** 2 + (t.y - (w.y + _ * $)) ** 2;
      if (z >= i)
        continue;
      const A = [0, 0, 0];
      A[k] = 1 - $, A[(k + 1) % 3] = $, n = { indices: [x[0], x[1], x[2]], weights: A }, o = { indices: [x[0], x[1], x[2]], vertices: [p[0], p[1], p[2]] }, i = z;
    }
  }
  if (o) {
    const a = si(t, ...o.vertices);
    if (a?.every((c) => c >= -0.35 && c <= 1.35))
      return { indices: o.indices, weights: a };
  }
  if (n)
    return n;
  let s = 0, r = Number.POSITIVE_INFINITY;
  for (let a = 0; a < e.uvs.length; a += 1) {
    const c = e.uvs[a], u = (c.x - t.x) ** 2 + (c.y - t.y) ** 2;
    u < r && (s = a, r = u);
  }
  return { indices: [s, s, s], weights: [1, 0, 0] };
}
function Or(e, t) {
  const n = Lr(e, t);
  return n.indices.reduce((o, i, s) => ({
    x: o.x + e.points[i].x * n.weights[s],
    y: o.y + e.points[i].y * n.weights[s]
  }), { x: 0, y: 0 });
}
const ai = {
  "head-yaw": "headYaw",
  "head-pitch": "headPitch",
  "head-roll": "headRoll",
  "body-sway": "bodySway",
  "body-pitch": "bodyPitch",
  "body-roll": "bodyRoll",
  "gaze-x": "gazeX",
  "gaze-y": "gazeY",
  breath: "breath",
  blink: "blink",
  "blink-left": "blinkLeft",
  "blink-right": "blinkRight",
  "brow-left": "browLeft",
  "brow-right": "browRight",
  smile: "smile",
  "cheek-puff": "cheekPuff",
  "mouth-open": "mouthOpen",
  "mouth-a": "mouthA",
  "mouth-i": "mouthI",
  "mouth-u": "mouthU",
  "mouth-e": "mouthE",
  "mouth-o": "mouthO",
  "arm-left": "armLeft",
  "arm-right": "armRight",
  "hand-left-x": "handLeftX",
  "hand-left-y": "handLeftY",
  "hand-right-x": "handRightX",
  "hand-right-y": "handRightY",
  "hand-left-open": "handLeftOpen",
  "hand-right-open": "handRightOpen",
  "ear-x": "earX",
  "ear-y": "earY",
  "tail-x": "tailX",
  "tail-y": "tailY"
}, $r = [
  { id: "param-head-yaw", name: "Head Yaw", group: "Head", min: -1, default: 0, max: 1, semantic: "head-yaw" },
  { id: "param-head-pitch", name: "Head Pitch", group: "Head", min: -1, default: 0, max: 1, semantic: "head-pitch" },
  { id: "param-head-roll", name: "Head Roll", group: "Head", min: -1, default: 0, max: 1, semantic: "head-roll" },
  { id: "param-body-sway", name: "Body Sway", group: "Body", min: -1, default: 0, max: 1, semantic: "body-sway" },
  { id: "param-body-pitch", name: "Body Pitch", group: "Body", min: -1, default: 0, max: 1, semantic: "body-pitch" },
  { id: "param-body-roll", name: "Body Roll", group: "Body", min: -1, default: 0, max: 1, semantic: "body-roll" },
  { id: "param-gaze-x", name: "Gaze X", group: "Eyes", min: -1, default: 0, max: 1, semantic: "gaze-x" },
  { id: "param-gaze-y", name: "Gaze Y", group: "Eyes", min: -1, default: 0, max: 1, semantic: "gaze-y" },
  { id: "param-breath", name: "Breath", group: "Body", min: -1, default: 0, max: 1, semantic: "breath" },
  { id: "param-blink", name: "Blink", group: "Eyes", min: 0, default: 0, max: 1, semantic: "blink" },
  { id: "param-mouth-open", name: "Mouth Open", group: "Mouth", min: 0, default: 0, max: 1, semantic: "mouth-open" },
  { id: "param-blink-left", name: "Blink Left", group: "Eyes", min: 0, default: 0, max: 1, semantic: "blink-left" },
  { id: "param-blink-right", name: "Blink Right", group: "Eyes", min: 0, default: 0, max: 1, semantic: "blink-right" },
  { id: "param-brow-left", name: "Brow Left", group: "Face", min: -1, default: 0, max: 1, semantic: "brow-left" },
  { id: "param-brow-right", name: "Brow Right", group: "Face", min: -1, default: 0, max: 1, semantic: "brow-right" },
  { id: "param-smile", name: "Smile", group: "Face", min: 0, default: 0, max: 1, semantic: "smile" },
  { id: "param-cheek-puff", name: "Cheek Puff", group: "Face", min: 0, default: 0, max: 1, semantic: "cheek-puff" },
  { id: "param-mouth-a", name: "Mouth A", group: "Visemes", min: 0, default: 0, max: 1, semantic: "mouth-a" },
  { id: "param-mouth-i", name: "Mouth I", group: "Visemes", min: 0, default: 0, max: 1, semantic: "mouth-i" },
  { id: "param-mouth-u", name: "Mouth U", group: "Visemes", min: 0, default: 0, max: 1, semantic: "mouth-u" },
  { id: "param-mouth-e", name: "Mouth E", group: "Visemes", min: 0, default: 0, max: 1, semantic: "mouth-e" },
  { id: "param-mouth-o", name: "Mouth O", group: "Visemes", min: 0, default: 0, max: 1, semantic: "mouth-o" }
];
function Ar() {
  return {
    parameters: $r.map((e) => ({ ...e, kind: "continuous" })),
    deformers: [],
    bindings: [],
    expressions: [],
    physics: [],
    behaviors: []
  };
}
const Wn = /* @__PURE__ */ new WeakMap(), Bn = /* @__PURE__ */ new WeakMap(), Nr = [];
function ct(e) {
  const t = e.model, n = Wn.get(e);
  if (n && n.source === t)
    return n.model;
  const o = t ? { ...t, expressions: t.expressions ?? [], physics: t.physics ?? [], behaviors: t.behaviors ?? [] } : Ar();
  return Wn.set(e, { source: t, model: o }), o;
}
function ci(e, t) {
  return `${e}:${t}`;
}
function Yt(e) {
  const t = ct(e), n = Bn.get(t);
  if (n)
    return n;
  const o = /* @__PURE__ */ new Map();
  for (const s of t.bindings) {
    const r = ci(s.target.kind, s.target.id), a = o.get(r);
    a ? a.push(s) : o.set(r, [s]);
  }
  const i = {
    model: t,
    parametersById: new Map(t.parameters.map((s) => [s.id, s])),
    behaviorsById: new Map(t.behaviors.map((s) => [s.id, s])),
    bindingsByTarget: o
  };
  return Bn.set(t, i), i;
}
function je(e, t, n) {
  return Math.max(t, Math.min(n, e));
}
function bt(e, t) {
  if (!t.repeat || t.max <= t.min)
    return je(e, t.min, t.max);
  const n = t.max - t.min;
  return ((e - t.min) % n + n) % n + t.min;
}
function Zr(e, t) {
  return t === "hold" ? 0 : t === "linear" ? e : e * e * (3 - 2 * e);
}
function Dr(e, t) {
  return e.loop ? (t % e.duration + e.duration) % e.duration : je(t, 0, e.duration);
}
function Fr(e, t) {
  const n = e.keyframes;
  if (t <= n[0].time)
    return n[0].value;
  if (t >= n.at(-1).time)
    return n.at(-1).value;
  for (let o = 0; o < n.length - 1; o += 1) {
    const i = n[o], s = n[o + 1];
    if (t <= s.time) {
      const r = Zr((t - i.time) / Math.max(1e-12, s.time - i.time), s.easing ?? "smoothstep");
      return i.value * (1 - r) + s.value * r;
    }
  }
  return n.at(-1).value;
}
function Wr(e, t) {
  const n = { parameters: {}, expressions: { ...t.expressions ?? {} } }, o = Yt(e), i = o.model, s = [];
  if (t.timeSeconds !== void 0)
    for (const r of i.behaviors)
      r.autoplay && s.push({ behavior: r, timeSeconds: t.timeSeconds, weight: 1 });
  if (t.behavior) {
    const r = o.behaviorsById.get(t.behavior.id);
    r && s.push({ behavior: r, timeSeconds: t.behavior.timeSeconds, weight: je(t.behavior.weight ?? 1, 0, 1) });
  }
  for (const r of s) {
    const a = Dr(r.behavior, r.timeSeconds);
    for (const c of r.behavior.tracks) {
      const u = Fr(c, a);
      if (c.target.kind === "parameter") {
        const h = o.parametersById.get(c.target.id);
        n.parameters[c.target.id] = h ? h.default + (u - h.default) * r.weight : u;
      } else
        n.expressions[c.target.id] = je(u * r.weight, 0, 1);
    }
  }
  return n;
}
function wn(e, t) {
  const n = {}, o = Yt(e), i = o.model, s = Wr(e, t);
  for (const r of i.parameters) {
    const a = r.semantic ? ai[r.semantic] : void 0, c = a ? t[a] : void 0, u = c === void 0 && (r.semantic === "blink-left" || r.semantic === "blink-right") ? t.blink : c, h = typeof u == "number" ? u : r.default;
    n[r.id] = bt(h, r);
  }
  for (const r of i.expressions) {
    const a = je(s.expressions[r.id] ?? 0, 0, 1);
    if (!(a <= 0))
      for (const [c, u] of Object.entries(r.parameters)) {
        const h = o.parametersById.get(c);
        h && (n[c] = bt((n[c] ?? h.default) + (u - h.default) * a, h));
      }
  }
  for (const [r, a] of Object.entries(s.parameters)) {
    const c = o.parametersById.get(r);
    c && (n[r] = bt(a, c));
  }
  for (const [r, a] of Object.entries(t.parameters ?? {})) {
    const c = o.parametersById.get(r);
    c && (n[r] = bt(a, c));
  }
  return n;
}
function Br(e) {
  const t = [], n = /* @__PURE__ */ new Set(), o = (i) => {
    if (n.has(i.id))
      return;
    const s = e.find((r) => r.outputParameterId === i.inputParameterId);
    s && o(s), n.add(i.id), t.push(i);
  };
  return e.forEach(o), t;
}
class Hr {
  project;
  axes = /* @__PURE__ */ new Map();
  lastTime;
  constructor(t) {
    this.project = t;
    for (const n of ct(t).physics)
      this.axes.set(n.id, { value: 0, velocity: 0 });
  }
  reset() {
    this.lastTime = void 0;
    for (const t of this.axes.values())
      t.value = 0, t.velocity = 0;
  }
  sample(t, n = t.timeSeconds ?? 0) {
    const o = Yt(this.project), i = o.model;
    if (i.physics.length === 0)
      return { ...t, timeSeconds: n };
    const s = this.lastTime === void 0 ? 1 / 60 : je(n - this.lastTime, 1 / 240, 0.05);
    this.lastTime = n;
    const r = wn(this.project, { ...t, timeSeconds: n });
    for (const a of Br(i.physics)) {
      const c = o.parametersById.get(a.inputParameterId), u = o.parametersById.get(a.outputParameterId), h = this.axes.get(a.id), f = (((r[c.id] ?? c.default) - c.default) * a.inputScale - h.value) * a.response * a.response - 2 * a.damping * a.response * h.velocity;
      h.velocity += f * s, h.value += h.velocity * s, t.parameters?.[u.id] === void 0 && (r[u.id] = bt(u.default + h.value * a.outputScale, u));
    }
    return { ...t, timeSeconds: n, parameters: r };
  }
}
function Yr(e, t) {
  const n = wn(e, t), o = { ...t, parameters: n };
  for (const i of ct(e).parameters) {
    if (!i.semantic)
      continue;
    const s = ai[i.semantic], r = n[i.id];
    r !== void 0 && (o[s] = r);
  }
  return o;
}
const Hn = /* @__PURE__ */ new WeakMap();
function Xr(e) {
  const t = Hn.get(e);
  if (t)
    return t;
  const n = {
    xs: [...new Set(e.keyforms.map((o) => o.values[0]))].sort((o, i) => o - i),
    ys: [...new Set(e.keyforms.map((o) => o.values[1] ?? 0))].sort((o, i) => o - i),
    byX: new Map(e.keyforms.map((o) => [o.values[0], o])),
    byCoordinate: new Map(e.keyforms.map((o) => [`${o.values[0]}:${o.values[1] ?? 0}`, o]))
  };
  return Hn.set(e, n), n;
}
function Qt(e, t) {
  if (e.length === 0)
    return [0, 0, 0];
  if (t <= e[0])
    return [e[0], e[0], 0];
  if (t >= e.at(-1))
    return [e.at(-1), e.at(-1), 0];
  for (let n = 0; n < e.length - 1; n += 1) {
    const o = e[n], i = e[n + 1];
    if (t <= i)
      return [o, i, (t - o) / Math.max(1e-12, i - o)];
  }
  return [e.at(-1), e.at(-1), 0];
}
function ui(e, t) {
  const n = t[e.parameterIds[0]] ?? 0, o = Xr(e);
  if (e.parameterIds.length === 1) {
    const [f, m, l] = Qt(o.xs, n), p = o.byX.get(f);
    if (f === m)
      return [{ keyform: p, weight: 1 }];
    const x = o.byX.get(m);
    return [{ keyform: p, weight: 1 - l }, { keyform: x, weight: l }];
  }
  const i = t[e.parameterIds[1]] ?? 0, [s, r, a] = Qt(o.xs, n), [c, u, h] = Qt(o.ys, i), d = (f, m) => o.byCoordinate.get(`${f}:${m}`);
  return s === r && c === u ? [{ keyform: d(s, c), weight: 1 }] : s === r ? [{ keyform: d(s, c), weight: 1 - h }, { keyform: d(s, u), weight: h }] : c === u ? [{ keyform: d(s, c), weight: 1 - a }, { keyform: d(r, c), weight: a }] : [
    { keyform: d(s, c), weight: (1 - a) * (1 - h) },
    { keyform: d(r, c), weight: a * (1 - h) },
    { keyform: d(s, u), weight: (1 - a) * h },
    { keyform: d(r, u), weight: a * h }
  ];
}
function hi(e, t, n) {
  t && (e.x += t.x * n, e.y += t.y * n);
}
function mn(e, t, n, o) {
  for (const i of t)
    hi(e, i.keyform[n]?.[String(o)], i.weight);
}
function Yn(e, t, n) {
  return e.reduce((o, i) => o + (i.keyform[t] ?? n) * i.weight, 0);
}
function Dt(e) {
  const t = { translation: { x: 0, y: 0 }, rotationDegrees: 0, scale: { x: 1, y: 1 } };
  for (const n of e)
    hi(t.translation, n.keyform.transform?.translation, n.weight), t.rotationDegrees += (n.keyform.transform?.rotationDegrees ?? 0) * n.weight, t.scale.x += ((n.keyform.transform?.scale?.x ?? 1) - 1) * n.weight, t.scale.y += ((n.keyform.transform?.scale?.y ?? 1) - 1) * n.weight;
  return t;
}
function Ft(e, t, n) {
  const o = n.rotationDegrees * Math.PI / 180, i = (e.x - t.x) * n.scale.x, s = (e.y - t.y) * n.scale.y, r = Math.cos(o), a = Math.sin(o);
  return {
    x: t.x + i * r - s * a + n.translation.x,
    y: t.y + i * a + s * r + n.translation.y
  };
}
function Ur(e, t, n) {
  const o = je((e.x - t.bounds.x) / t.bounds.width, 0, 1) * (t.cols - 1), i = je((e.y - t.bounds.y) / t.bounds.height, 0, 1) * (t.rows - 1), s = Math.min(t.cols - 2, Math.floor(o)), r = Math.min(t.rows - 2, Math.floor(i)), a = o - s, c = i - r, u = (l, p) => n[p * t.cols + l], h = u(s, r), d = u(s + 1, r), f = u(s, r + 1), m = u(s + 1, r + 1);
  return {
    x: h.x * (1 - a) * (1 - c) + d.x * a * (1 - c) + f.x * (1 - a) * c + m.x * a * c,
    y: h.y * (1 - a) * (1 - c) + d.y * a * (1 - c) + f.y * (1 - a) * c + m.y * a * c
  };
}
function di(e, t, n) {
  return Yt(e).bindingsByTarget.get(ci(t, n)) ?? Nr;
}
function li(e, t, n, o) {
  const i = di(e, "deformer", t.id).map((a) => ui(a, o));
  let s = n;
  if (t.kind === "rotation")
    for (const a of i)
      s = Ft(s, t.pivot, Dt(a));
  else {
    const a = t.controlPoints.map((c, u) => {
      const h = { ...c };
      for (const d of i)
        mn(h, d, "warpPointDeltas", u);
      return h;
    });
    s = Ur(s, t, a);
    for (const c of i)
      s = Ft(s, {
        x: t.bounds.x + t.bounds.width * 0.5,
        y: t.bounds.y + t.bounds.height * 0.5
      }, Dt(c));
  }
  if (!t.parentId)
    return s;
  const r = ct(e).deformers.find((a) => a.id === t.parentId);
  return r ? li(e, r, s, o) : s;
}
function jr(e, t, n, o) {
  const i = di(e, "layer", t.id).filter((f) => !f.blinkMode || f.blinkMode === (t.blinkMode ?? "texture"));
  if (i.length === 0 && !t.deformerId)
    return o ? (o.points = t.mesh.points, o.opacityMultiplier = 1, o.drawOrderOffset = 0, o) : { points: t.mesh.points, opacityMultiplier: 1, drawOrderOffset: 0 };
  const s = i.map((f) => ui(f, n)), r = t.headPoseMode === "keyforms" ? i.findIndex((f) => f.parameterIds.length === 2 && ["head-yaw", "head-pitch"].every((m) => f.parameterIds.some((l) => ct(e).parameters.some((p) => p.id === l && p.semantic === m)))) : -1, a = s.filter((f, m) => m !== r), c = r >= 0 ? s[r] : void 0, u = c ? {
    ...t.mesh,
    uvs: t.mesh.points,
    points: t.mesh.points.map((f, m) => {
      const l = { x: 0, y: 0 };
      return mn(l, c, "meshPointDeltas", m), l;
    })
  } : void 0, h = o?.points !== t.mesh.points && o?.points.length === t.mesh.points.length ? o.points : new Array(t.mesh.points.length);
  for (let f = 0; f < t.mesh.points.length; f += 1) {
    const m = t.mesh.points[f];
    let l = h[f] ?? { x: m.x, y: m.y };
    l.x = m.x, l.y = m.y;
    for (const p of a)
      mn(l, p, "meshPointDeltas", f);
    for (const p of a) {
      const x = Ft(l, t.pivot, Dt(p));
      l.x = x.x, l.y = x.y;
    }
    if (u && c) {
      const p = Or(u, l);
      l.x += p.x, l.y += p.y;
      const x = Ft(l, t.pivot, Dt(c));
      l.x = x.x, l.y = x.y;
    }
    if (t.deformerId) {
      const p = ct(e).deformers.find((x) => x.id === t.deformerId);
      if (p) {
        const x = li(e, p, l, n);
        l.x = x.x, l.y = x.y;
      }
    }
    h[f] = l;
  }
  const d = o ?? { points: h, opacityMultiplier: 1, drawOrderOffset: 0 };
  return d.points = h, d.opacityMultiplier = s.reduce((f, m) => f * Yn(m, "opacityMultiplier", 1), 1), d.drawOrderOffset = s.reduce((f, m) => f + Yn(m, "drawOrderOffset", 0), 0), d;
}
function Vr(e, t, n, o) {
  return jr(e, t, n.parameters ?? wn(e, n), o);
}
const Xn = (e) => e > 1e-20 ? 0.5 * e * Math.log(e) : 0;
function Gr(e) {
  const t = e.length, n = e.map((o, i) => [...o, ...Array.from({ length: t }, (s, r) => i === r ? 1 : 0)]);
  for (let o = 0; o < t; o++) {
    let i = o;
    for (let r = o + 1; r < t; r++)
      Math.abs(n[r][o]) > Math.abs(n[i][o]) && (i = r);
    if (Math.abs(n[i][o]) < 1e-12)
      return;
    [n[o], n[i]] = [n[i], n[o]];
    const s = n[o][o];
    for (let r = 0; r < 2 * t; r++)
      n[o][r] /= s;
    for (let r = 0; r < t; r++) {
      if (r === o)
        continue;
      const a = n[r][o];
      for (let c = 0; c < 2 * t; c++)
        n[r][c] -= a * n[o][c];
    }
  }
  return n.map((o) => o.slice(t));
}
function Jr(e) {
  if (!e.length)
    return () => [];
  const t = e.reduce((c, u) => ({ x: c.x + u.x / e.length, y: c.y + u.y / e.length }), { x: 0, y: 0 }), n = Math.max(1e-6, ...e.map((c) => Math.hypot(c.x - t.x, c.y - t.y))), o = (c) => ({ x: (c.x - t.x) / n, y: (c.y - t.y) / n }), i = e.map(o), s = i.length, r = Array.from({ length: s + 3 }, () => Array(s + 3).fill(0));
  for (let c = 0; c < s; c++) {
    for (let u = 0; u < s; u++)
      r[c][u] = Xn((i[c].x - i[u].x) ** 2 + (i[c].y - i[u].y) ** 2);
    [1, i[c].x, i[c].y].forEach((u, h) => {
      r[c][s + h] = u, r[s + h][c] = u;
    });
  }
  const a = Gr(r);
  return (c) => {
    const u = o(c);
    if (a) {
      const f = [...i.map((m) => Xn((u.x - m.x) ** 2 + (u.y - m.y) ** 2)), 1, u.x, u.y];
      return i.map((m, l) => f.reduce((p, x, k) => p + x * a[k][l], 0));
    }
    const h = i.map((f) => 1 / ((u.x - f.x) ** 2 + (u.y - f.y) ** 2 + 36e-4)), d = h.reduce((f, m) => f + m, 0);
    return h.map((f) => f / d);
  };
}
function $e(e, t = 0, n = 1) {
  return Math.max(t, Math.min(n, e));
}
function Ye(e) {
  const t = $e(e);
  return t * t * (3 - 2 * t);
}
const Un = /* @__PURE__ */ new WeakMap(), jn = /* @__PURE__ */ new WeakMap(), Vn = /* @__PURE__ */ new WeakMap();
function fi(e, t) {
  return $e((t.x - e.bounds.x) / Math.max(1e-6, e.bounds.width));
}
function qr(e, t) {
  const n = Math.max(1e-6, e.bounds.height), o = e.mesh.points.filter((s) => {
    const r = fi(e, s);
    return r >= 0.3 && r <= 0.7 && s.y > t + n * 0.04;
  }), i = o.length > 0 ? Math.max(...o.map((s) => s.y)) : t + n * 0.34;
  return $e(i, t + n * 0.12, e.bounds.y + n);
}
function Kr(e) {
  const t = Un.get(e);
  if (t)
    return t;
  const n = Math.max(1e-6, e.bounds.width), o = Math.max(1e-6, e.bounds.height), i = e.secondaryAnchors?.frontHairRoot?.y ?? e.bounds.y + o * 0.52, s = $e(i - o * 0.14, e.bounds.y + o * 0.26, e.bounds.y + o * 0.44), r = {
    width: n,
    height: o,
    commonRootY: i,
    leftRoot: e.secondaryAnchors?.frontHairRootLeft ?? { x: e.bounds.x + n * 0.18, y: i },
    rightRoot: e.secondaryAnchors?.frontHairRootRight ?? { x: e.bounds.x + n * 0.82, y: i },
    leftTip: e.secondaryAnchors?.frontHairTipLeft ?? { x: e.bounds.x + n * 0.1, y: e.bounds.y + o },
    rightTip: e.secondaryAnchors?.frontHairTipRight ?? { x: e.bounds.x + n * 0.9, y: e.bounds.y + o },
    bangRootY: s,
    bangTipY: qr(e, s)
  };
  return Un.set(e, r), r;
}
function In(e, t) {
  let n = jn.get(e);
  n || (n = /* @__PURE__ */ new WeakMap(), jn.set(e, n));
  const o = n.get(t);
  if (o && o.x === t.x && o.y === t.y)
    return o.value;
  const i = Kr(e), { width: s, height: r, commonRootY: a, bangRootY: c, bangTipY: u } = i, h = fi(e, t), d = $e((t.y - e.bounds.y) / r), f = h < 0.5 ? -1 : 1, m = f < 0 ? i.leftRoot : i.rightRoot, l = f < 0 ? i.leftTip : i.rightTip, p = Math.max(r * 0.28, l.y - m.y), x = $e((t.y - m.y) / p), k = m.x + (l.x - m.x) * x, w = Math.abs(t.x - k) / Math.max(1e-6, s * 0.3), P = 1 - Ye((w - 0.2) / 0.8), M = Ye((Math.abs(h - 0.5) - 0.18) / 0.27), _ = Math.max(M, P * 0.9), B = $e((t.y - c) / Math.max(r * 0.12, u - c)), $ = 1 - Ye((Math.abs(h - 0.5) - 0.17) / 0.15), z = 1 - Ye((t.y - u) / Math.max(1e-6, r * 0.055)), A = $ * z, F = Math.hypot(t.x - (e.secondaryAnchors?.frontHairRoot?.x ?? e.pivot.x), t.y - a) <= r * 1e-6 ? 0 : Ye((B - 0.08) / 0.92) ** 1.2 * A, X = 1 - A * 0.9, j = Ye(x) ** 1.3 * _ * X, V = {
    u: h,
    v: d,
    screenSide: f,
    root: m,
    tip: l,
    progress: x,
    sideMask: _,
    sideRelease: j,
    bangRoot: { x: t.x, y: c },
    bangTipY: u,
    bangProgress: B,
    bangMask: A,
    bangRelease: F,
    totalRelease: Math.max(j, F)
  };
  return n.set(t, { x: t.x, y: t.y, value: V }), V;
}
function Pn(e, t) {
  if (e.role !== "frontHair")
    return 0;
  let n = Vn.get(e);
  n || (n = /* @__PURE__ */ new WeakMap(), Vn.set(e, n));
  const o = n.get(t);
  if (o && o.x === t.x && o.y === t.y)
    return o.value;
  const i = e.secondaryAnchors?.ahogeRoot;
  if (!i)
    return n.set(t, { x: t.x, y: t.y, value: 0 }), 0;
  const s = Math.max(1e-6, e.bounds.height), r = (i.y - t.y) / s;
  if (r <= 4e-3)
    return n.set(t, { x: t.x, y: t.y, value: 0 }), 0;
  const a = Ye((r - 4e-3) / 0.052), c = Math.abs(t.x - i.x) / s, u = $e(r / 0.28), h = 0.06 + u ** 1.5 * 0.31, d = 0.1 + u * 0.1;
  if (c >= h + d)
    return n.set(t, { x: t.x, y: t.y, value: 0 }), 0;
  const f = 1 - Ye((c - h) / d), m = $e(a * f);
  return n.set(t, { x: t.x, y: t.y, value: m }), m;
}
function Qr(e, t) {
  return Pn(e, t) >= 0.5 ? 1 : 0;
}
const pi = /* @__PURE__ */ new Set(["frontHair", "backHair", "sideHair", "headwear", "ear"]), en = /* @__PURE__ */ new Set(["eyeWhite", "iris", "eyelash", "eyeClosed"]), es = /* @__PURE__ */ new Set(["face", "nose", "mouth", "eyeWhite", "iris", "eyelash", "eyeClosed", "eyebrow"]);
function pe(e) {
  const t = T(e, 0, 1);
  return t * t * (3 - 2 * t);
}
function Ne(e, t) {
  const n = T(e, -1, 1);
  return {
    near: Math.max(0, -n * t),
    far: Math.max(0, n * t)
  };
}
function ts(e) {
  return e.side === "left" ? 1 : e.side === "right" ? -1 : 0;
}
function ns(e) {
  return e === "nose" ? 0.22 : e === "mouth" ? 0.08 : e === "eyeWhite" || e === "iris" || e === "eyelash" || e === "eyeClosed" ? 0.12 : e === "eyebrow" ? 0.08 : e === "frontHair" ? 0.16 : e === "headwear" ? 0.08 : e === "backHair" ? -0.18 : e === "sideHair" || e === "ear" ? -0.02 : e === "neck" ? -0.12 : 0;
}
function os(e, t) {
  const n = e.faceDepthProfile?.points;
  if (!n || n.length === 0)
    return 0;
  const o = T(t, 0, 1);
  if (o <= n[0].position)
    return n[0].depth;
  if (o >= n.at(-1).position)
    return n.at(-1).depth;
  for (let i = 1; i < n.length; i += 1) {
    const s = n[i], r = n[i - 1];
    if (o > s.position)
      continue;
    const a = pe((o - r.position) / Math.max(1e-9, s.position - r.position));
    return r.depth + (s.depth - r.depth) * a;
  }
  return 0;
}
function is(e, t, n = e.role) {
  if (n === "face" || n === "nose" || n === "mouth" || n === "eyeWhite" || n === "iris" || n === "eyelash" || n === "eyeClosed" || n === "eyebrow")
    return 1;
  if (n === "ear")
    return 0.74;
  if (n === "frontHair") {
    const o = T((t.y - e.bounds.y) / Math.max(1e-6, e.bounds.height), 0, 1);
    return 0.72 + (1 - pe((o - 0.38) / 0.52)) * 0.18;
  }
  if (n === "headwear")
    return 0.78;
  if (n === "sideHair")
    return 0.72;
  if (n === "backHair") {
    const o = T((t.y - e.bounds.y) / Math.max(1e-6, e.bounds.height), 0, 1);
    return 0.8 - o * o * (3 - 2 * o) * 0.35;
  }
  if (n === "neck") {
    const o = T((t.y - e.bounds.y) / Math.max(1e-6, e.bounds.height), 0, 1);
    return 1 - o * o * (3 - 2 * o);
  }
  return 0.4;
}
let tn, yt;
const Gn = /* @__PURE__ */ new WeakMap(), Jn = /* @__PURE__ */ new WeakMap(), qn = /* @__PURE__ */ new WeakMap();
let ot;
const rs = 32768;
function Kn() {
  return { byIdentity: /* @__PURE__ */ new WeakMap(), byCoordinates: /* @__PURE__ */ new Map(), byTopologyIdentity: /* @__PURE__ */ new WeakMap(), coordinateCount: 0 };
}
function ss(e, t) {
  return e.byIdentity.get(t) ?? e.byCoordinates.get(t.x)?.get(t.y);
}
function as(e, t, n) {
  if (e.byIdentity.set(t, n), e.byCoordinates.get(t.x)?.get(t.y))
    return;
  e.coordinateCount >= rs && (e.byCoordinates.clear(), e.coordinateCount = 0);
  let i = e.byCoordinates.get(t.x);
  i || (i = /* @__PURE__ */ new Map(), e.byCoordinates.set(t.x, i)), i.set(t.y, n), e.coordinateCount += 1;
}
function cs(e, t, n) {
  let o = Jn.get(e);
  o || (o = /* @__PURE__ */ new WeakMap(), Jn.set(e, o));
  const i = o.get(t);
  if (i && i.x === t.x && i.y === t.y)
    return i.depth;
  const s = os(e, n);
  return o.set(t, { x: t.x, y: t.y, depth: s }), s;
}
function mi(e, t) {
  let n = Gn.get(e);
  n || (n = /* @__PURE__ */ new WeakMap(), Gn.set(e, n));
  let o = n.get(t);
  return o || (o = { face: Kn(), skull: Kn() }, n.set(t, o)), o;
}
function Qe(e, t, n, o, i, s) {
  const r = tn;
  return r && r.field === e && r.cage === t && r.yawAngle === n && r.pitchAngle === o && r.yaw === i && r.pitch === s ? r : (tn = {
    field: e,
    cage: t,
    yawAngle: n,
    pitchAngle: o,
    yaw: i,
    pitch: s,
    projectedFace: {},
    projectedSkull: {},
    semanticMappings: mi(e, t),
    surfacePivots: /* @__PURE__ */ new WeakMap(),
    cagePivots: /* @__PURE__ */ new WeakMap(),
    attachmentPivots: /* @__PURE__ */ new WeakMap()
  }, tn);
}
function gi(e, t) {
  return e.kind === "head-surfaces-v2" && pi.has(t) && e.skullCenter && e.skullRadiusX && e.skullRadiusY ? { center: e.skullCenter, radiusX: e.skullRadiusX, radiusY: e.skullRadiusY } : { center: e.center, radiusX: e.radiusX, radiusY: e.radiusY };
}
function us(e, t, n, o) {
  let i = qn.get(e);
  i || (i = /* @__PURE__ */ new WeakMap(), qn.set(e, i));
  let s = i.get(t);
  s || (s = /* @__PURE__ */ new Map(), i.set(t, s));
  let r = s.get(o);
  r || (r = /* @__PURE__ */ new WeakMap(), s.set(o, r));
  const a = r.get(n);
  if (a && a.sourceX === n.x && a.sourceY === n.y)
    return a;
  const c = gi(e, o), u = (n.x - c.center.x) / c.radiusX, h = (n.y - c.center.y) / c.radiusY, d = u * u + h * h, f = Math.sqrt(Math.max(0, 1 - Math.min(1, d))), m = es.has(o) ? cs(e, n, (h + 1) * 0.5) : 0, l = {
    sourceX: n.x,
    sourceY: n.y,
    surface: c,
    nx: u,
    ny: h,
    z: f + (ns(o) + m) * T(e.depthStrength ?? 1, 0, 1.6),
    blend: is(t, n, o) * t.weights.head,
    rootWeight: t.weights.head,
    skullAligned: e.kind === "head-surfaces-v2" && pi.has(o)
  };
  return r.set(n, l), l;
}
function rt(e, t, n, o, i, s, r) {
  (!yt || yt.yawAngle !== i || yt.pitchAngle !== s) && (yt = {
    yawAngle: i,
    pitchAngle: s,
    cosYaw: Math.cos(i),
    sinYaw: Math.sin(i),
    cosPitch: Math.cos(s),
    sinPitch: Math.sin(s)
  });
  const { cosYaw: a, sinYaw: c, cosPitch: u, sinPitch: h } = yt, d = t * a + o * c, f = -t * c + o * a, m = n * u + o * h, l = -n * h + o * u, p = f - o + (l - o), x = T(1 + p * r, 0.94, 1.06);
  return {
    x: e.center.x + d * e.radiusX * x,
    y: e.center.y + m * e.radiusY * x
  };
}
function hs(e, t, n) {
  return ot?.field === e && ot.yawAngle === t && ot.pitchAngle === n || (ot = { field: e, yawAngle: t, pitchAngle: n }), ot;
}
function ds(e, t, n) {
  const o = hs(e, t, n);
  if (o.skullRoot && o.commonOffset)
    return { skullRoot: o.skullRoot, commonOffset: o.commonOffset };
  const i = gi(e, "frontHair"), s = { center: e.center, radiusX: e.radiusX, radiusY: e.radiusY };
  o.skullRoot = rt(i, 0, 0, 1, t, n, e.perspective);
  const r = rt(s, 0, 0, 1, t, n, e.perspective);
  return o.commonOffset = {
    x: r.x - s.center.x,
    y: r.y - s.center.y
  }, { skullRoot: o.skullRoot, commonOffset: o.commonOffset };
}
function ls(e, t, n, o, i, s) {
  const r = T(i, -1, 1), a = Math.abs(r), c = T(e.contourStrength ?? 1, 0, 1.6);
  let u = o;
  const h = Math.sign(r), d = n.x < e.center.x ? -1 : n.x > e.center.x ? 1 : 0, f = /* @__PURE__ */ new Set(["cheekLeft", "cheekRight", "jawLeft", "jawRight"]);
  if (a >= 1e-9 && f.has(t)) {
    const { near: p, far: x } = Ne(r, d);
    u = { x: u.x - h * e.radiusX * (x * 0.04 + p * 8e-3) * c, y: u.y };
  } else if (a >= 1e-9) {
    const p = t === "chin" ? 0.04 : t === "mouth" || t === "mouthLeft" || t === "mouthRight" ? 0.025 : t === "nose" ? 0.018 : 0;
    p > 0 && (u = { x: u.x + h * e.radiusX * a * p * c, y: u.y });
  }
  const m = T(s, -1, 1), l = t === "chin" ? 1 : t === "jawLeft" || t === "jawRight" ? 0.76 : t === "mouth" || t === "mouthLeft" || t === "mouthRight" ? 0.24 : t === "cheekLeft" || t === "cheekRight" ? 0.16 : 0;
  if (l > 0 && Math.abs(m) >= 1e-9) {
    const p = Math.abs(m), x = m < 0 ? 1 + p * 0.055 * l * c : 1 - p * 0.07 * l * c, k = m < 0 ? -0.045 : -0.09;
    u = {
      x: e.center.x + (u.x - e.center.x) * x,
      // Counter the strong depth difference between the mouth and chin in the
      // spherical projection. Up and down need separate compensation because
      // the available 2D artwork exposes much less underside than forehead.
      y: u.y - m * e.radiusY * l * k * c
    };
  }
  return u;
}
function fs(e, t, n, o, i, s, r, a) {
  const c = t.points[n].position, u = o === "skull" && e.skullCenter && e.skullRadiusX && e.skullRadiusY ? { center: e.skullCenter, radiusX: e.skullRadiusX, radiusY: e.skullRadiusY } : { center: e.center, radiusX: e.radiusX, radiusY: e.radiusY }, h = (c.x - u.center.x) / u.radiusX, d = (c.y - u.center.y) / u.radiusY, f = h * h + d * d;
  let m = Math.sqrt(Math.max(0, 1 - Math.min(1, f)));
  if (o === "face" && (n === "faceLeft" || n === "faceRight")) {
    const p = t.points[n === "faceLeft" ? "eyeLeftOuter" : "eyeRightOuter"].position, x = (p.x - u.center.x) / u.radiusX, k = (p.y - u.center.y) / u.radiusY, w = Math.sqrt(Math.max(0, 1 - Math.min(1, x * x + k * k)));
    m = m * 0.5 + w * 0.5;
  }
  let l = rt(u, h, d, m, i, s, e.perspective);
  if (o === "skull" && e.skullCenter && e.skullRadiusX && e.skullRadiusY) {
    const p = rt(u, 0, 0, 1, i, s, e.perspective), x = { center: e.center, radiusX: e.radiusX, radiusY: e.radiusY }, k = rt(x, 0, 0, 1, i, s, e.perspective);
    l = {
      x: u.center.x + (k.x - x.center.x) + (l.x - p.x),
      y: u.center.y + (k.y - x.center.y) + (l.y - p.y)
    };
  }
  return o === "face" ? ls(e, n, c, l, r, a) : l;
}
function ps(e, t, n, o, i = mi(e, t), s) {
  const r = i[o], a = o === "face" ? t.faceTriangles : t.skullTriangles;
  if (s) {
    const h = r.byTopologyIdentity.get(s);
    if (h && h.x === n.x && h.y === n.y)
      return h.mapping;
  } else {
    const h = ss(r, n);
    if (h)
      return h;
  }
  if (!r.smooth) {
    const h = [...new Set(a.flat())];
    r.smooth = { ids: h, weights: Jr(h.map((d) => t.points[d].position)) };
  }
  const c = r.smooth.weights(n), u = { entries: r.smooth.ids.map((h, d) => ({ id: h, weight: c[d] })) };
  return s ? r.byTopologyIdentity.set(s, { x: n.x, y: n.y, mapping: u }) : as(r, n, u), u;
}
function Re(e, t, n, o, i, s, r, a, c) {
  const u = Qe(e, t, i, s, r, a), h = o === "face" ? u.projectedFace : u.projectedSkull, d = (p) => {
    const x = h[p];
    if (x)
      return x;
    const k = fs(e, t, p, o, i, s, r, a);
    return h[p] = k, k;
  }, f = ps(e, t, n, o, u.semanticMappings, c);
  let m = 0, l = 0;
  for (const { id: p, weight: x } of f.entries) {
    const k = t.points[p], w = d(p);
    m += (w.x - k.position.x) * x, l += (w.y - k.position.y) * x;
  }
  return { x: n.x + m, y: n.y + l };
}
function et(e) {
  return e === "face" ? 0.88 : e === "eyeWhite" || e === "iris" || e === "eyelash" || e === "eyeClosed" || e === "eyebrow" ? 0.82 : e === "nose" || e === "mouth" ? 0.86 : e === "frontHair" ? 0.3 : e === "sideHair" ? 0.36 : e === "backHair" ? 0.22 : e === "headwear" ? 0.15 : e === "ear" ? 0.18 : 0;
}
const Qn = /* @__PURE__ */ new WeakMap(), eo = /* @__PURE__ */ new WeakMap();
function ms(e, t, n, o, i, s, r, a) {
  const c = Pe(e, n, o, i, s), u = Re(e, t, o, "skull", i, s, r, a), h = et("frontHair");
  return {
    x: c.x + (u.x - c.x) * h,
    y: c.y + (u.y - c.y) * h
  };
}
function gs(e, t, n) {
  let o = Qn.get(e);
  o || (o = /* @__PURE__ */ new WeakMap(), Qn.set(e, o));
  let i = o.get(t);
  i || (i = /* @__PURE__ */ new WeakMap(), o.set(t, i));
  const s = i.get(n);
  if (s && s.sourceX === n.x && s.sourceY === n.y)
    return s;
  const r = e.points.forehead.position, a = (e.points.eyeLeft.position.y + e.points.eyeRight.position.y) * 0.5, c = e.points.faceLeft.position.x, u = e.points.faceRight.position.x, h = (c + u) * 0.5, d = Math.max(1e-6, (u - c) * 0.5), f = n.x < h, m = f ? t.secondaryAnchors?.frontHairRootLeft : t.secondaryAnchors?.frontHairRootRight, l = f ? t.secondaryAnchors?.frontHairTipLeft : t.secondaryAnchors?.frontHairTipRight, p = {
    x: f ? c : u,
    y: r.y + (a - r.y) * 0.46
  }, x = {
    x: f ? t.bounds.x + t.bounds.width * 0.1 : t.bounds.x + t.bounds.width * 0.9,
    y: t.bounds.y + t.bounds.height
  }, k = m ?? p, w = l ?? x, P = Math.max(t.bounds.height * 0.28, w.y - k.y), M = T((n.y - k.y) / P, 0, 1), _ = P * 0.9, B = pe((n.y - (k.y - _)) / Math.max(1e-6, _)), $ = Math.abs(n.x - h) / d, z = pe(($ - 0.42) / 0.58), A = k.x + (w.x - k.x) * M, C = Math.abs(n.x - A) / Math.max(1e-6, t.bounds.width * 0.24), F = 1 - pe((C - 0.18) / 0.82), X = pe(($ - 0.5) / 0.34), j = Math.max(z, F * X), V = B * j * (1 - pe((M - 0.06) / 0.64)) * 0.82, se = B * j * (1 - pe((M - 0.01) / 0.34)), ie = j * pe((M - 0.015) / 0.56) * 0.94, he = { sourceX: n.x, sourceY: n.y, faceFollow: V, rootLock: se, strandRelease: ie, root: k };
  return i.set(n, he), he;
}
function ys(e, t, n) {
  let o = eo.get(e);
  o || (o = /* @__PURE__ */ new WeakMap(), eo.set(e, o));
  let i = o.get(t);
  i || (i = /* @__PURE__ */ new WeakMap(), o.set(t, i));
  const s = i.get(n);
  if (s && s.sourceX === n.x && s.sourceY === n.y)
    return s;
  const r = e.points.faceLeft.position.x, a = e.points.faceRight.position.x, c = { sourceX: n.x, sourceY: n.y, anchor: { x: T(n.x, r, a), y: n.y } };
  return i.set(n, c), c;
}
function nn(e, t, n, o, i, s, r, a) {
  const { anchor: c } = ys(t, n, o), u = Pe(e, n, c, i, s, "face"), h = Re(e, t, c, "face", i, s, r, a), d = et("face");
  return {
    x: u.x + (h.x - u.x) * d - c.x,
    y: u.y + (h.y - u.y) * d - c.y
  };
}
function vs(e, t, n, o, i, s) {
  if (s <= 1e-6)
    return n;
  const r = (e.points.faceLeft.position.x + e.points.faceRight.position.x) * 0.5, a = t.x < r ? -1 : 1, c = a < 0 ? e.points.faceLeft.position : e.points.faceRight.position, h = (t.x - c.x) * a, d = Math.max(1e-6, (e.points.faceRight.position.x - e.points.faceLeft.position.x) * 0.5);
  if (h <= 0 || h >= d * 0.45)
    return n;
  const { near: f, far: m } = Ne(i, a), l = 1 + f * 0.035 - m * 0.035, p = c.x + o.x, x = a * (n.x - p), k = T(x, h * 0.86, h * 1.42), w = h * l, P = k + (w - k) * 0.38, M = p + a * P, _ = xs(t, c, d) * s;
  return { x: n.x + (M - n.x) * _, y: n.y };
}
function yi(e, t, n, o, i, s, r, a, c) {
  if (n.role !== "frontHair" || Qr(n, o))
    return i;
  const u = In(n, o), h = Math.max(1e-6, n.bounds.width), d = T((o.x - n.bounds.x) / h, 0, 1), f = pe((Math.abs(d - 0.5) - 0.1) / 0.16), m = pe((u.progress - 0.015) / 0.38), l = f * m;
  if (l <= 1e-6)
    return i;
  const p = Pe(e, n, u.root, s, r), x = t ? Re(e, t, u.root, "skull", s, r, a, c) : p, k = t ? et("frontHair") : 0, w = {
    x: p.x + (x.x - p.x) * k,
    y: p.y + (x.y - p.y) * k
  }, { near: P, far: M } = Ne(a, u.screenSide), _ = 1 + P * 0.045 - M * 0.04, B = w.x + (o.x - u.root.x) * _, $ = T((B - i.x) * l, -h * 0.018, h * 0.018);
  return {
    x: i.x + $,
    y: i.y
  };
}
function xs(e, t, n) {
  const o = Math.abs(e.x - t.x) / Math.max(1e-6, n * 0.45);
  return 1 - pe((o - 0.35) / 0.65);
}
function bs(e, t, n, o, i, s, r, a, c) {
  if (n.role !== "headwear" || n.headwearPerspective !== "crown")
    return i;
  const u = {
    x: n.bounds.x + n.bounds.width * 0.5,
    y: n.bounds.y + n.bounds.height * 0.28
  }, h = Pe(e, n, u, s, r, "frontHair"), d = t ? Re(e, t, u, "skull", s, r, a, c) : h, f = t ? et("headwear") : 0, m = {
    x: h.x + (d.x - h.x) * f,
    y: h.y + (d.y - h.y) * f
  }, l = o.x < u.x ? -1 : o.x > u.x ? 1 : 0, { near: p, far: x } = Ne(a, l), k = T(c, -1, 1), w = (1 + p * 0.15 - x * 0.18) * (1 + Math.max(0, k) * 0.035), P = (1 + p * 0.045 - x * 0.065) * (1 - Math.max(0, -k) * 0.1 + Math.max(0, k) * 0.12), M = {
    x: m.x + (o.x - u.x) * w,
    y: m.y + (o.y - u.y) * P
  }, _ = T((o.x - n.bounds.x) / Math.max(1e-6, n.bounds.width), 0, 1), B = T((o.y - n.bounds.y) / Math.max(1e-6, n.bounds.height), 0, 1), $ = pe((Math.abs(_ - 0.5) - 0.19) / 0.2) * pe((B - 0.3) / 0.34), z = _ < 0.5 ? -1 : 1, A = {
    x: n.bounds.x + n.bounds.width * (z < 0 ? 0.34 : 0.66),
    y: n.bounds.y + n.bounds.height * 0.54
  }, C = Pe(e, n, A, s, r, "frontHair"), F = t ? Re(e, t, A, "skull", s, r, a, c) : C, X = {
    x: C.x + (F.x - C.x) * f,
    y: C.y + (F.y - C.y) * f
  }, j = Ne(a, z), V = {
    x: X.x + (o.x - A.x) * (1 + j.near * 0.07 - j.far * 0.12),
    y: X.y + (o.y - A.y) * (1 - Math.max(0, -k) * 0.05 + Math.max(0, k) * 0.06)
  }, se = {
    x: M.x + (V.x - M.x) * $,
    y: M.y + (V.y - M.y) * $
  };
  return {
    x: se.x,
    y: se.y
  };
}
const to = /* @__PURE__ */ new WeakMap();
function _s(e, t, n) {
  let o = to.get(e);
  o || (o = /* @__PURE__ */ new WeakMap(), to.set(e, o));
  let i = o.get(t);
  i || (i = /* @__PURE__ */ new WeakMap(), o.set(t, i));
  const s = i.get(n);
  if (s && s.sourceX === n.x && s.sourceY === n.y)
    return s;
  const r = t.role === "backHair" && e.skullCenter ? { x: e.skullCenter.x, y: e.skullCenter.y + (e.skullRadiusY ?? t.bounds.height * 0.3) * 0.12 } : t.pivot, a = T((n.y - t.bounds.y) / Math.max(1e-6, t.bounds.height), 0, 1), c = {
    sourceX: n.x,
    sourceY: n.y,
    pivot: r,
    side: n.x < r.x ? -1 : 1,
    geometricFreeLength: pe((a - 0.28) / 0.62),
    localX: n.x - r.x,
    localY: n.y - r.y
  };
  return i.set(n, c), c;
}
function ks(e, t, n, o, i, s, r, a, c, u, h) {
  if (n.role !== "backHair" && n.role !== "sideHair")
    return i;
  const d = _s(e, n, o), { pivot: f } = d, m = t ? Qe(e, t, s, r, a, c).attachmentPivots.get(n) : void 0, l = m?.surface ?? Pe(e, n, f, s, r), p = m?.cage ?? (t ? Re(e, t, f, "skull", s, r, a, c) : l);
  t && !m && Qe(e, t, s, r, a, c).attachmentPivots.set(n, { surface: l, cage: p });
  const x = t ? et(n.role) * T(u, 0, 1) : 0, k = {
    x: l.x + (p.x - l.x) * x,
    y: l.y + (p.y - l.y) * x
  }, w = Ne(a, d.side), P = T(c, -1, 1), M = h === void 0 ? d.geometricFreeLength : 1 - T(h, 0, 1), _ = {
    x: k.x + d.localX * (1 + w.near * 0.035 - w.far * 0.055),
    y: k.y + d.localY * (1 - Math.max(0, -P) * 0.08 + Math.max(0, P) * 0.09)
  }, B = {
    x: o.x + (k.x - f.x) + d.localX * (w.near * 0.018 - w.far * 0.028),
    y: o.y + (k.y - f.y)
  };
  return {
    x: _.x + (B.x - _.x) * M,
    y: _.y + (B.y - _.y) * M
  };
}
function ws(e, t, n, o, i, s, r, a, c, u, h) {
  const d = n.secondaryAnchors?.frontHairRoot ?? n.pivot, f = t ? Qe(e, t, i, s, r, a).attachmentPivots.get(n) : void 0, m = f?.surface ?? Pe(e, n, d, i, s), l = f?.cage ?? (t ? Re(e, t, d, "skull", i, s, r, a) : m);
  t && !f && Qe(e, t, i, s, r, a).attachmentPivots.set(n, { surface: m, cage: l });
  const p = t ? et("frontHair") * T(c, 0, 1) : 0, x = Pe(e, n, o, i, s), k = t ? Re(e, t, o, "skull", i, s, r, a, h) : x, w = {
    x: x.x + (k.x - x.x) * p,
    y: x.y + (k.y - x.y) * p
  };
  let P = {
    x: m.x + (l.x - m.x) * p,
    y: m.y + (l.y - m.y) * p
  };
  if (t) {
    let E = f?.faceFollow;
    if (!E) {
      const Y = (t.points.faceLeft.position.x + t.points.faceRight.position.x) * 0.5, W = Math.max(1e-8, e.radiusX * 1e-5), q = nn(e, t, n, { x: Y - W, y: d.y }, i, s, r, a), te = nn(e, t, n, { x: Y + W, y: d.y }, i, s, r, a);
      E = {
        x: d.x + (q.x + te.x) * 0.5,
        y: d.y + (q.y + te.y) * 0.5
      }, Qe(e, t, i, s, r, a).attachmentPivots.set(n, { surface: m, cage: l, faceFollow: E });
    }
    const N = T(u === void 0 ? c : u, 0, 1);
    P = {
      x: P.x + (E.x - P.x) * N,
      y: P.y + (E.y - P.y) * N
    };
  }
  const M = T(r, -1, 1), _ = T(a, -1, 1), B = o.x - d.x, $ = o.y - d.y, z = Math.max(1e-6, n.bounds.width * 0.5), A = T(B / z, -1, 1), C = n.secondaryAnchors?.ahogeRoot, F = n.secondaryAnchors?.frontHairRoot?.y ?? d.y, X = pe((o.y - F) / Math.max(1e-6, n.bounds.height * 0.18)), j = u === void 0 ? X : 1 - T(u, 0, 1), V = 1, se = 0, he = 1 + (1 - Math.max(0, -_) * 0.045 + Math.max(0, _) * 0.055 - 1) * j, fe = A < 0 ? -1 : 1, de = Ne(M, fe), be = 1 + de.near * 0.1 - de.far * 0.1, nt = M * z * 0.025 * (1 - A * A) * j, Be = {
    x: P.x + B * be * V - $ * he * se + nt,
    y: P.y + B * se + $ * he * V
  };
  let ce = {
    x: w.x + (Be.x - w.x) * j,
    y: w.y + (Be.y - w.y) * j
  };
  if (t) {
    const E = gs(t, n, o), N = nn(e, t, n, o, i, s, r, a), Y = u === void 0 ? Math.max(E.faceFollow, E.rootLock) : T(u, 0, 1), W = {
      x: o.x + N.x,
      y: o.y + N.y
    };
    ce = {
      x: ce.x + (W.x - ce.x) * Y,
      y: ce.y + (W.y - ce.y) * Y
    }, ce = vs(t, o, ce, N, r, E.rootLock);
  }
  ce = yi(e, t, n, o, ce, i, s, r, a);
  const b = Pn(n, o);
  if (!C || b <= 1e-6)
    return ce;
  const v = t ? ms(e, t, n, C, i, s, r, a) : Pe(e, n, C, i, s), y = {
    x: v.x + o.x - C.x,
    y: v.y + o.y - C.y
  };
  return {
    x: ce.x + (y.x - ce.x) * b,
    y: ce.y + (y.y - ce.y) * b
  };
}
function Pe(e, t, n, o, i, s = t.role) {
  const r = us(e, t, n, s);
  let a = rt(r.surface, r.nx, r.ny, r.z, o, i, e.perspective);
  if (r.skullAligned) {
    const { skullRoot: c, commonOffset: u } = ds(e, o, i);
    a = {
      x: r.surface.center.x + u.x + (a.x - c.x),
      y: r.surface.center.y + u.y + (a.y - c.y)
    };
    const h = {
      x: n.x + u.x * r.rootWeight,
      y: n.y + u.y * r.rootWeight
    };
    return {
      x: h.x + (a.x - (n.x + u.x)) * r.blend,
      y: h.y + (a.y - (n.y + u.y)) * r.blend
    };
  }
  return {
    x: n.x + (a.x - n.x) * r.blend,
    y: n.y + (a.y - n.y) * r.blend
  };
}
function Is(e, t, n, o, i, s, r, a = 1) {
  if (e.side === "center" || !en.has(e.role) && e.role !== "eyebrow")
    return n;
  const { near: c, far: u } = Ne(i, ts(e)), h = en.has(e.role) ? T(1 + c * 0.025 - u * 0.12, 0.88, 1.025) : T(1 + c * 0.018 - u * 0.08, 0.92, 1.018), d = en.has(e.role) ? T(1 + c * 6e-3 - u * 0.018, 0.982, 1.006) : T(1 + c * 4e-3 - u * 0.012, 0.988, 1.004), f = T(s, -1, 1), m = 1 - Math.max(0, f) * 0.018 + Math.max(0, -f) * 8e-3, l = 1 - Math.max(0, f) * 0.055 + Math.max(0, -f) * 0.035, p = d * l, x = Number.isFinite(a) && a > 0 ? a : 1, k = r && Math.hypot(r.x, r.y) > 1e-9 ? Math.atan2(r.y, r.x * x) : 0, w = Math.cos(k), P = Math.sin(k), M = (t.x - e.pivot.x) * x, _ = t.y - e.pivot.y, B = M * w + _ * P, $ = -M * P + _ * w, z = T(i, -1, 1), A = B * h * m, C = $ * p + B * z * 0.018;
  return {
    x: o.x + (A * w - C * P) / x,
    y: o.y + A * P + C * w
  };
}
function Ps(e, t, n, o, i) {
  if (t.role !== "face")
    return o;
  const s = T(i, -1, 1), r = Math.abs(s);
  if (r < 1e-9)
    return o;
  const a = T((n.x - e.center.x) / Math.max(1e-6, e.radiusX), -1, 1), c = a < -1e-6 ? -1 : a > 1e-6 ? 1 : 0, u = Math.abs(a), h = T((n.y - t.bounds.y) / Math.max(1e-6, t.bounds.height), 0, 1), d = T((h - 0.18) / 0.72, 0, 1), f = T((h - 0.55) / 0.45, 0, 1), { near: m, far: l } = Ne(s, c), p = Math.sign(s), x = T(e.contourStrength ?? 1, 0, 1.6), k = -p * e.radiusX * u * d * (l * 0.08 + m * 0.02) * x, w = p * e.radiusX * (1 - u) * f * r * 0.08 * x;
  return { x: o.x + k + w, y: o.y };
}
function Ss(e, t, n, o, i, s, r = {}) {
  const a = T(o, -1, 1) * e.maxYawRadians, c = i < 0 ? e.maxPitchUpRadians ?? e.maxPitchRadians : e.maxPitchDownRadians ?? e.maxPitchRadians, u = T(i, -1, 1) * c;
  if (Math.abs(a) < 1e-9 && Math.abs(u) < 1e-9)
    return { ...n };
  if (t.role === "frontHair")
    return ws(e, s, t, n, a, u, o, i, r.skull ?? 1, r.attachment, r.topologyKey);
  const h = Pe(e, t, n, a, u), d = s ? Qe(e, s, a, u, o, i) : void 0;
  let f = d?.surfacePivots.get(t);
  f || (f = Pe(e, t, t.pivot, a, u), d?.surfacePivots.set(t, f));
  const m = s?.roleGroups.skull.includes(t.role) ? "skull" : s?.roleGroups.face.includes(t.role) ? "face" : void 0, l = m === "face" ? r.face ?? 1 : m === "skull" ? r.skull ?? 1 : 1, p = s && m ? et(t.role) * T(l, 0, 1) : 0, x = s && m ? Re(e, s, n, m, a, u, o, i, r.topologyKey) : h;
  let k = s && m ? d?.cagePivots.get(t) : f;
  k || (k = Re(e, s, t.pivot, m, a, u, o, i), d?.cagePivots.set(t, k));
  let w = {
    x: h.x + (x.x - h.x) * p,
    y: h.y + (x.y - h.y) * p
  };
  w = bs(e, s, t, n, w, a, u, o, i), w = ks(e, s, t, n, w, a, u, o, i, r.skull ?? 1, r.attachment);
  const P = {
    x: f.x + (k.x - f.x) * p,
    y: f.y + (k.y - f.y) * p
  };
  w = yi(e, s, t, n, w, a, u, o, i);
  const M = s?.points.eyeLeft.position, _ = s?.points.eyeRight.position, B = M && _ ? { x: _.x - M.x, y: _.y - M.y } : void 0, $ = Is(t, n, w, P, o, i, B, r.canvasAspect);
  return s ? $ : Ps(e, t, n, $, o);
}
function Xe(e) {
  const t = T(e, 0, 1);
  return t * t * (3 - 2 * t);
}
function Mt(e, t) {
  return T((t.y - e.bounds.y) / Math.max(1e-6, e.bounds.height), 0, 1);
}
function vi(e, t) {
  return Xe((Mt(e, t) - 0.2) / 0.18);
}
function Ms(e, t) {
  return Xe((Mt(e, t) - 0.84) / 0.16);
}
function Rs(e, t) {
  return Xe((Mt(e, t) - 0.46) / 0.42);
}
function Es(e) {
  const t = e.bounds.y + e.bounds.height * 0.08, n = e.bounds.y + e.bounds.height * 0.2;
  return {
    x: T(e.pivot.x, e.bounds.x, e.bounds.x + e.bounds.width),
    y: T(e.pivot.y, t, n)
  };
}
function zs(e, t) {
  const n = Mt(e, t);
  if (e.role === "topWear") {
    const o = Xe((n - 0.16) / 0.28), i = 1 - Xe((n - 0.58) / 0.24);
    return T(o * i * 0.34, 0, 1);
  }
  return e.role === "bottomWear" ? e.garmentStructure === "supported" ? vi(e, t) : Xe((n - 0.2) / 0.8) ** 2 : e.role === "arm" ? Xe((n - 0.08) / 0.92) ** 2 : n * n;
}
function Cs(e, t) {
  return e.role !== "bottomWear" ? 1 : e.garmentStructure === "supported" ? 1 - vi(e, t) * 0.03 : 1 - Xe((Mt(e, t) - 0.2) / 0.8) * 0.08;
}
function no(e, t, n) {
  if (Math.abs(n) < 1e-8)
    return;
  const o = Math.cos(n), i = Math.sin(n), s = e.x - t.x, r = e.y - t.y;
  e.x = t.x + s * o - r * i, e.y = t.y + s * i + r * o;
}
function Ts(e) {
  return e.role === "nose" ? 1 : e.role === "mouth" ? 0.78 : e.role === "iris" || e.role === "eyeWhite" || e.role === "eyelash" || e.role === "eyeClosed" ? 0.62 : e.role === "eyebrow" ? 0.55 : e.role === "frontHair" || e.role === "headwear" ? 0.18 : e.role === "backHair" ? -0.16 : 0;
}
function Ls(e) {
  return e.role === "nose" ? 1 : e.role === "mouth" ? 0.78 : e.role === "iris" || e.role === "eyeWhite" || e.role === "eyelash" || e.role === "eyeClosed" || e.role === "eyebrow" ? 0.55 : e.role === "frontHair" || e.role === "headwear" ? 0.18 : e.role === "backHair" ? -0.12 : 0;
}
function le(e) {
  const t = T(e, 0, 1);
  return t * t * (3 - 2 * t);
}
function Os(e, t) {
  const n = T(t, 0, 1), o = e.points;
  if (n <= o[0].position)
    return o[0].depth;
  if (n >= o.at(-1).position)
    return o.at(-1).depth;
  for (let i = 1; i < o.length; i += 1) {
    const s = o[i], r = o[i - 1];
    if (n > s.position)
      continue;
    const a = le((n - r.position) / Math.max(1e-9, s.position - r.position));
    return r.depth + (s.depth - r.depth) * a;
  }
  return 0;
}
const oo = /* @__PURE__ */ new WeakMap();
function $s(e, t, n, o) {
  const i = e.runtime.torsoVolumeProfile;
  if (!i)
    return 0;
  let s = oo.get(e);
  s || (s = /* @__PURE__ */ new WeakMap(), oo.set(e, s));
  const r = s.get(t);
  if (r && r.x === t.x && r.y === t.y)
    return r.depth;
  const a = e.anchors.shoulderLeft && e.anchors.shoulderRight ? (e.anchors.shoulderLeft.y + e.anchors.shoulderRight.y) * 0.5 : e.anchors.neck?.y ?? n.y - o * 0.55, c = Math.max(a + o * 0.9, n.y + o * 0.72), u = T((t.y - a) / Math.max(1e-6, c - a), 0, 1), h = Os(i, u) * i.strength;
  return s.set(t, { x: t.x, y: t.y, depth: h }), h;
}
function ae(e, t, n) {
  const o = e?.[t];
  if (!o?.length || n <= 0)
    return 0;
  const i = T(n, 0, 1) * o.length;
  if (i >= o.length)
    return o.at(-1) ?? 0;
  if (i <= 1)
    return (o[0] ?? 0) * le(i);
  const s = Math.min(o.length - 1, Math.floor(i)), r = Math.max(0, s - 1), a = le(i - Math.floor(i));
  return (o[r] ?? 0) * (1 - a) + (o[s] ?? 0) * a;
}
function Lt(e, t, n, o, i) {
  const s = le((o - 0.34) / 0.32);
  return ae(e, n, i) * (1 - s) + ae(t, n, i) * s;
}
function As(e) {
  return e.role === "topWear" ? 1 : e.role === "neck" ? 0.2 : e.role === "arm" ? 0.18 : e.role === "bottomWear" ? 0.08 : e.role === "unknown" ? 0.12 : 0;
}
function Ns(e, t) {
  if (e.role === "neck") {
    const n = T((t.y - e.bounds.y) / Math.max(1e-6, e.bounds.height), 0, 1);
    return le((n - 0.04) / 0.92);
  }
  return e.role === "topWear" ? 1 : e.role === "arm" || e.role === "hand" ? 0.9 : e.role === "bottomWear" ? Cs(e, t) : e.role === "tail" || e.role === "accessory" ? 0.7 : e.role === "leg" ? 0.16 : e.role === "foot" ? 0 : e.role === "unknown" ? 0.45 : 0.75;
}
function Zs(e, t, n) {
  const o = n === void 0 ? void 0 : e.mesh.influences?.physicsRelease?.[n];
  if (o !== void 0)
    return T(o, 0, 1);
  const i = T((t.x - e.bounds.x) / Math.max(1e-6, e.bounds.width), 0, 1), s = T((t.y - e.bounds.y) / Math.max(1e-6, e.bounds.height), 0, 1);
  if (e.role === "frontHair")
    return In(e, t).totalRelease;
  if (e.role === "headwear") {
    const r = t.x < e.bounds.x + e.bounds.width * 0.5 ? e.secondaryAnchors?.earHingeLeft : e.secondaryAnchors?.earHingeRight;
    if (r) {
      const u = Math.hypot((t.x - r.x) / Math.max(1e-6, e.bounds.width * 0.32), (t.y - r.y) / Math.max(1e-6, e.bounds.height * 0.35)), h = le((u - 0.12) / 0.88), d = le((Math.abs(i - 0.5) - 0.08) / 0.24), f = le((s - 0.42) / 0.22);
      return h * d * f;
    }
    const a = le((Math.abs(i - 0.5) - 0.18) / 0.32), c = le((s - 0.18) / 0.72);
    return a * c;
  }
  if (e.role === "ear") {
    const r = Math.hypot((t.x - e.pivot.x) / Math.max(1e-6, e.bounds.width), (t.y - e.pivot.y) / Math.max(1e-6, e.bounds.height));
    return le((r - 0.04) / 0.72);
  }
  if (e.role === "tail") {
    const r = Math.hypot((i - 0.03) * 0.9, (s - 0.08) * 0.74);
    return le((r - 0.05) / 0.38);
  }
  return e.role === "topWear" || e.role === "bottomWear" || e.role === "arm" ? zs(e, t) : s * s;
}
function Je(e, t, n, o, i) {
  if (Math.abs(o) < 1e-8 || i <= 0)
    return;
  const s = t.x - n.x, r = t.y - n.y;
  e.x += -r * o * i, e.y += s * o * i;
}
function He(e, t, n, o, i) {
  if (Math.abs(o) < 1e-8 || i <= 0)
    return;
  const s = o * i, r = Math.cos(s), a = Math.sin(s), c = t.x - n.x, u = t.y - n.y;
  e.x += n.x + c * r - u * a - t.x, e.y += n.y + c * a + u * r - t.y;
}
function Ds(e, t, n, o, i) {
  Math.abs(o) < 1e-8 || i <= 0 || (e.x += (t.x - n.x) * o * i, e.y += (t.y - n.y) * o * i);
}
function io(e, t) {
  if (e.role === "headwear")
    return t.x < e.bounds.x + e.bounds.width * 0.5 && e.secondaryAnchors?.earHingeLeft ? { pivot: e.secondaryAnchors.earHingeLeft, mirror: -1 } : e.secondaryAnchors?.earHingeRight ? { pivot: e.secondaryAnchors.earHingeRight, mirror: 1 } : void 0;
  if (e.role === "ear") {
    const n = e.side === "right" || e.side === "center" && t.x < e.bounds.x + e.bounds.width * 0.5 ? -1 : 1;
    return { pivot: e.pivot, mirror: n };
  }
}
function ro(e, t, n) {
  const o = e.role === "ear" ? e.side : t.x < e.bounds.x + e.bounds.width * 0.5 ? "right" : "left";
  return o === "left" ? { x: n.earLeftX ?? n.earX, y: n.earLeftY ?? n.earY } : o === "right" ? { x: n.earRightX ?? n.earX, y: n.earRightY ?? n.earY } : { x: n.earX, y: n.earY };
}
function Fs(e) {
  return e.role === "eyeWhite" || e.role === "iris" || e.role === "eyelash" || e.role === "eyeClosed" || e.role === "eyebrow" || e.role === "ear";
}
function qe(e, t, n, o) {
  return n === void 0 ? o : T(e.mesh.influences?.[t]?.[n] ?? o, 0, 1);
}
const so = /* @__PURE__ */ new WeakMap();
function Ws(e) {
  const t = so.get(e);
  if (t)
    return t;
  const n = Array.from({ length: e.mesh.points.length }, () => []);
  for (const [o, i] of (e.hairStrands ?? []).entries())
    for (let s = 0; s < n.length; s += 1) {
      const r = T(i.weights[s] ?? 0, 0, 1), a = T(i.release[s] ?? 0, 0, 1);
      r <= 1e-6 || a <= 1e-6 || n[s].push({ strandIndex: o, strand: i, ownership: r, release: a });
    }
  return so.set(e, n), n;
}
function Bs(e, t) {
  const n = e.hairLayerMotion.get(t);
  if (n !== void 0)
    return n ?? void 0;
  const o = e.state.secondary?.hairStrands;
  if (!t.hairStrands?.length || !o) {
    e.hairLayerMotion.set(t, null);
    return;
  }
  const i = t.hairStrands.map((r) => o[r.id]), s = {
    active: i.some(Boolean),
    chains: i,
    rotationScale: t.role === "frontHair" ? 2.45 : t.role === "sideHair" ? 3.15 : 3.65,
    liftScale: t.role === "frontHair" ? 0.24 : t.role === "sideHair" ? 0.64 : 0.84
  };
  return e.hairLayerMotion.set(t, s), s;
}
function ao(e, t, n, o, i, s, r) {
  if (i === void 0)
    return !1;
  const a = Bs(o, n);
  if (!a?.active)
    return !1;
  for (const c of Ws(n)[i] ?? []) {
    const u = a.chains[c.strandIndex];
    if (!u)
      continue;
    const h = r * c.ownership, d = ae(u, "x", c.release), f = ae(u, "y", c.release);
    He(e, t, c.strand.root, d * a.rotationScale * h, 1), e.y += f * s * a.liftScale * h * c.release;
  }
  return !0;
}
function Sn(e, t) {
  const n = e.runtime.envelope;
  return {
    state: t,
    envelope: n,
    faceWidth: Math.max(0.08, Math.abs((e.anchors.cheekLeft?.x ?? 0.6) - (e.anchors.cheekRight?.x ?? 0.4)) / 0.64),
    faceHeight: Math.max(0.1, Math.abs((e.anchors.chin?.y ?? 0.5) - (e.anchors.forehead?.y ?? 0.25)) / 0.78),
    headPivot: e.anchors.neck ?? e.anchors.chin ?? { x: 0.5, y: 0.42 },
    bodyPivot: e.anchors.bodyCenter ?? { x: 0.5, y: 0.65 },
    yaw: T(t.headYaw, -1, 1) * n.headYaw,
    pitch: T(t.headPitch, -1, 1) * n.headPitch,
    headRoll: T(t.headRoll, -1, 1) * n.headRollDegrees * Math.PI / 180,
    bodyRoll: T(t.bodyRoll, -1, 1) * n.bodyRollDegrees * Math.PI / 180,
    hairLayerMotion: /* @__PURE__ */ new WeakMap()
  };
}
function gn(e, t, n, o, i, s) {
  const { state: r, envelope: a, faceWidth: c, faceHeight: u, headPivot: h, bodyPivot: d, yaw: f, pitch: m, headRoll: l, bodyRoll: p } = o, x = 1 - qe(t, "pin", i, 0), k = t.weights.body * qe(t, "body", i, 1) * x, w = t.weights.head * qe(t, "head", i, 1) * x, P = t.weights.gaze * qe(t, "gaze", i, 1) * x, M = t.weights.physics * qe(t, "physics", i, 1) * x;
  let _ = s ?? { x: n.x, y: n.y };
  _.x = n.x, _.y = n.y;
  const B = t.side === "left" ? r.blinkLeft ?? r.blink : t.side === "right" ? r.blinkRight ?? r.blink : r.blink;
  if ((t.role === "eyeWhite" || t.role === "iris" || t.role === "eyelash") && B > 0) {
    const $ = Pr(e, t, n, B);
    _.x = $.x, _.y = $.y;
  }
  if (k > 0) {
    const $ = k * Ns(t, n), z = T(r.breath, -1, 1), A = T(r.bodyPitch, -1, 1), C = As(t) * k;
    if (C > 0) {
      const de = 1 + z * a.breath * C, be = 1 + z * a.breath * 0.28 * C;
      _.x = d.x + (_.x - d.x) * de, _.y = d.y + (_.y - d.y) * be - Math.max(0, z) * a.breath * 0.05 * C;
    }
    _.x += r.bodySway * a.bodySway * $;
    const F = T(r.bodySway * 2.1, -1, 1), X = 1 - Math.abs(F) * 0.022 * $;
    _.x = d.x + (_.x - d.x) * X;
    const j = T((n.x - t.bounds.x) / Math.max(1e-6, t.bounds.width), 0, 1), V = T((n.y - t.bounds.y) / Math.max(1e-6, t.bounds.height), 0, 1), se = t.role === "topWear" || t.role === "arm" || t.role === "hand" || t.role === "bottomWear", ie = e.anchors.neck?.y ?? d.y - u * 0.45, he = 1 - le((n.y - ie) / Math.max(0.08, d.y - ie)), fe = t.role === "neck" ? 1 : se ? he : 1 - le((V - 0.08) / 0.92);
    if (t.role === "neck" || t.role === "topWear" || t.role === "arm" || t.role === "hand" || t.role === "bottomWear") {
      _.x += F * c * 0.012 * $ * fe, _.y += F * (j - 0.5) * u * 0.018 * $ * fe, _.y += A * u * 0.018 * $ * fe;
      const de = 1 - A * 0.012 * $ * fe;
      _.x = d.x + (_.x - d.x) * de;
    }
    if (e.runtime.torsoVolumeProfile && (t.role === "topWear" || t.role === "bottomWear")) {
      const de = $s(e, n, d, u);
      _.x += F * c * de * 0.24 * k;
    }
    if (t.side !== "center") {
      const de = t.side === "left" ? 1 : -1;
      _.x += F * de * c * 6e-3 * $;
    }
    no(_, d, p * $);
  }
  if (w > 0) {
    const $ = t.role === "neck" ? T((n.y - t.bounds.y) / Math.max(1e-6, t.bounds.height), 0, 1) : 0, z = w * (t.role === "neck" ? 1 - le($) : 1);
    if (e.runtime.poseField && t.headPoseMode !== "keyforms") {
      const A = (t.role === "eyeWhite" || t.role === "iris" || t.role === "eyelash") && B > 0, C = k === 0 && !A ? n : { x: _.x, y: _.y }, F = i === void 0 ? void 0 : t.mesh.influences?.headAttachment?.[i], X = i === void 0 ? void 0 : t.mesh.points[i], j = Ss(e.runtime.poseField, t, C, f, m, e.runtime.semanticCage, {
        canvasAspect: e.canvas.width / e.canvas.height,
        face: qe(t, "face", i, 1),
        skull: qe(t, "skull", i, 1),
        ...F === void 0 ? {} : { attachment: T(F, 0, 1) },
        ...X === void 0 ? {} : { topologyKey: X }
      });
      _.x += (j.x - C.x) * z, _.y += (j.y - C.y) * z;
    } else if (t.headPoseMode !== "keyforms") {
      if (t.side !== "center" && Fs(t)) {
        const F = t.side === "left" ? 1 : -1, X = 1 - f * F * 0.045 * z;
        _.x = t.pivot.x + (_.x - t.pivot.x) * X;
      }
      const A = 1 - Math.abs(f) * 0.055 * z;
      _.x = h.x + (_.x - h.x) * A;
      const C = T((h.y - n.y) / Math.max(0.1, u * 1.35), 0, 1);
      if (_.x += f * c * (0.028 + Ts(t) * 0.07 + C * 0.012) * z, t.role === "face") {
        const F = T((n.x - t.bounds.x) / Math.max(1e-6, t.bounds.width), 0, 1), X = T((n.y - t.bounds.y) / Math.max(1e-6, t.bounds.height), 0, 1), j = 1 - Math.abs(F * 2 - 1), V = le((X - 0.12) / 0.78);
        _.x += f * c * (0.012 + j * V * 0.014) * z, _.y += m * u * le((X - 0.36) / 0.64) * 0.01 * z;
      }
      _.y += m * u * (0.012 + C * 9e-3 + Ls(t) * 0.011) * z;
    }
    no(_, h, l * z);
  }
  if (P > 0) {
    const $ = t.bounds.width, z = t.bounds.height;
    _.x += T(r.gazeX, -1, 1) * a.gazeX * $ * P, _.y += T(r.gazeY, -1, 1) * a.gazeY * z * P;
  }
  if (M > 0) {
    const $ = T((n.x - t.bounds.x) / Math.max(1e-6, t.bounds.width), 0, 1), z = Zs(t, n, i), A = M;
    if (t.role === "frontHair") {
      if (!ao(_, n, t, o, i, u, A)) {
        const C = In(t, n), F = C.screenSide < 0 ? r.secondary?.frontHairLeft : r.secondary?.frontHairRight, X = C.bangRelease;
        if (r.secondary) {
          const j = ae(F, "x", C.sideRelease), V = ae(F, "y", C.sideRelease), se = Lt(r.secondary.frontHairLeft, r.secondary.frontHairRight, "x", $, X), ie = Lt(r.secondary.frontHairLeft, r.secondary.frontHairRight, "y", $, X);
          He(_, n, C.root, j * 2.45 * A, 1), He(_, n, C.bangRoot, se * 1.35 * A, 1), _.y += V * u * 0.22 * A * C.sideRelease, _.y += ie * u * 0.16 * A * X;
        } else
          He(_, n, C.root, r.hairX * 1.7 * A * C.sideRelease, 1), He(_, n, C.bangRoot, r.hairX * 0.9 * A * X, 1), _.y += r.hairY * u * 0.2 * A * z;
      }
    } else if (t.role === "backHair" || t.role === "sideHair") {
      if (!ao(_, n, t, o, i, u, A)) if (r.secondary) {
        const C = Lt(r.secondary.backHairLeft, r.secondary.backHairRight, "x", $, z), F = Lt(r.secondary.backHairLeft, r.secondary.backHairRight, "y", $, z);
        Je(_, n, t.pivot, C * 3.5 * A, 1), _.y += F * u * 0.82 * A;
      } else
        Je(_, n, t.pivot, r.backHairX * 2.5 * A, z), _.y += r.backHairY * u * 0.5 * A * z;
    } else if (t.role === "headwear") {
      const C = r.secondary ? ae(r.secondary.headwear, "x", z) : r.headwearX * z, F = r.secondary ? ae(r.secondary.headwear, "y", z) : r.headwearY * z;
      Je(_, n, t.pivot, C * 1.8 * A, z), Ds(_, n, t.pivot, F * 0.32 * A, z);
      const X = io(t, n);
      if (X) {
        const j = ro(t, n, r), V = j.y * X.mirror * 20 + j.x * 6;
        Je(_, n, X.pivot, V * A, z);
      }
    } else if (t.role === "ear") {
      const C = io(t, n), F = ro(t, n, r);
      C && Je(_, n, C.pivot, (F.y * C.mirror * 20 + F.x * 6) * A, z);
    } else if (t.role === "bottomWear" && t.garmentStructure === "supported") {
      const C = r.secondary?.skirt, F = C ? ae(C, "x", 0.82) : r.clothX, X = C ? ae(C, "y", 0.82) : r.clothY, j = C ? ae(C, "x", 1) : F, V = C ? ae(C, "y", 1) : X, se = Ms(t, n), ie = Rs(t, n), he = T(t.garmentFlexibility ?? 0, 0, 0.5), fe = C ? ae(C, "x", 0.46 + ie * 0.54) : F * (0.65 + ie * 0.35), de = F * 2.9 + (j - F) * 0.48 * se, be = ((fe - F) * 1.6 + F * 1.6) * he;
      He(_, n, Es(t), (de + be * ie) * A, z), _.y += X * u * 0.22 * A * z, _.y += (V - X) * u * 0.06 * A * se;
    } else if (t.role === "topWear" || t.role === "bottomWear") {
      const C = t.role === "bottomWear" ? 3.4 : 1.5, F = t.role === "bottomWear" ? r.secondary?.skirt : r.secondary?.topCloth, X = F ? ae(F, "x", z) : r.clothX * z, j = F ? ae(F, "y", z) : r.clothY * z;
      Je(_, n, t.pivot, X * C * A, 1), _.y += j * u * 0.56 * A, _.y += X * ($ - 0.5) * u * 0.16 * A;
    } else if (t.role === "tail") {
      const C = r.secondary?.tail, F = C?.x[0] ?? r.tailX, X = C?.y[0] ?? r.tailY, j = C ? ae(C, "x", z) : r.tailX, V = C ? ae(C, "y", z) : r.tailY, se = F * 0.8 + j * 0.2, ie = X * 0.72 + V * 0.28;
      He(_, n, t.pivot, (ie * 5.6 + se * 0.72) * A, z);
    } else if (t.role === "accessory") {
      const C = r.secondary ? ae(r.secondary.accessory, "x", z) : r.accessoryX * z, F = r.secondary ? ae(r.secondary.accessory, "y", z) : r.accessoryY * z;
      Je(_, n, t.pivot, C * 2.7 * A, 1), _.y += F * u * 0.68 * A;
    }
    if (t.role === "frontHair") {
      const C = Pn(t, n), F = r.secondary ? ae(r.secondary.ahoge, "x", 1) : r.ahogeX, X = r.secondary ? ae(r.secondary.ahoge, "y", 1) : r.ahogeY, j = t.secondaryAnchors?.ahogeRoot ?? t.pivot, V = t.weights.physics * x;
      He(_, n, j, (F * 5.4 + X * 1.5) * V, C);
    }
  }
  return _;
}
const Hs = /* @__PURE__ */ new WeakMap();
function Ys(e, t) {
  return e === t ? !0 : !e || !t ? !1 : [.../* @__PURE__ */ new Set([...Object.keys(e), ...Object.keys(t)])].every((o) => e[o] === t[o]);
}
function Xs(e, t) {
  return e.id === t.id && e.role === t.role && e.side === t.side && e.bounds === t.bounds && e.pivot === t.pivot && e.weights === t.weights && e.parentGroup === t.parentGroup && e.parentLayerId === t.parentLayerId && e.deformerId === t.deformerId && e.garmentStructure === t.garmentStructure && e.garmentFlexibility === t.garmentFlexibility && e.headwearPerspective === t.headwearPerspective && e.secondaryAnchors === t.secondaryAnchors && e.hairStrands === t.hairStrands && e.mesh.points.length === t.mesh.points.length && Ys(e.mesh.influences, t.mesh.influences);
}
function Us(e, t, n, o, i, s, r) {
  let a = r.get(s);
  a || (a = /* @__PURE__ */ new Map(), r.set(s, a));
  const c = a.get(t.id), u = c && c.project.runtime === e.runtime && c.project.anchors === e.anchors && Xs(c.layer, t) && c.authoredPoints.length === n.length;
  let h;
  if (u) {
    h = [...c.points];
    for (let d = 0; d < n.length; d += 1) {
      const f = n[d], m = c.authoredPoints[d];
      (f.x !== m.x || f.y !== m.y) && (h[d] = gn(e, t, f, i, d));
    }
  } else
    h = n.map((d, f) => gn(e, t, d, i, f));
  return a.set(t.id, { project: e, layer: t, authoredPoints: n, points: h }), h;
}
function js(e, t, n, o, i = Sn(e, o), s) {
  const r = s?.length === n.length ? s : new Array(n.length);
  for (let a = 0; a < n.length; a += 1)
    r[a] = gn(e, t, n[a], i, a, r[a]);
  return ii(e, t, r);
}
function Vs(e, t, n, o, i = Sn(e, o)) {
  return ii(e, t, Us(e, t, n, o, i, o, Hs));
}
var co;
function S(e, t, n) {
  function o(a, c) {
    if (a._zod || Object.defineProperty(a, "_zod", {
      value: {
        def: c,
        constr: r,
        traits: /* @__PURE__ */ new Set()
      },
      enumerable: !1
    }), a._zod.traits.has(e))
      return;
    a._zod.traits.add(e), t(a, c);
    const u = r.prototype, h = Object.keys(u);
    for (let d = 0; d < h.length; d++) {
      const f = h[d];
      f in a || (a[f] = u[f].bind(a));
    }
  }
  const i = n?.Parent ?? Object;
  class s extends i {
  }
  Object.defineProperty(s, "name", { value: e });
  function r(a) {
    var c;
    const u = n?.Parent ? new s() : this;
    o(u, a), (c = u._zod).deferred ?? (c.deferred = []);
    for (const h of u._zod.deferred)
      h();
    return u;
  }
  return Object.defineProperty(r, "init", { value: o }), Object.defineProperty(r, Symbol.hasInstance, {
    value: (a) => n?.Parent && a instanceof n.Parent ? !0 : a?._zod?.traits?.has(e)
  }), Object.defineProperty(r, "name", { value: e }), r;
}
class st extends Error {
  constructor() {
    super("Encountered Promise during synchronous parse. Use .parseAsync() instead.");
  }
}
class xi extends Error {
  constructor(t) {
    super(`Encountered unidirectional transform during encode: ${t}`), this.name = "ZodEncodeError";
  }
}
(co = globalThis).__zod_globalConfig ?? (co.__zod_globalConfig = {});
const Mn = globalThis.__zod_globalConfig;
function Ze(e) {
  return Mn;
}
function bi(e) {
  const t = Object.values(e).filter((o) => typeof o == "number");
  return Object.entries(e).filter(([o, i]) => t.indexOf(+o) === -1).map(([o, i]) => i);
}
function yn(e, t) {
  return typeof t == "bigint" ? t.toString() : t;
}
function Xt(e) {
  return {
    get value() {
      {
        const t = e();
        return Object.defineProperty(this, "value", { value: t }), t;
      }
    }
  };
}
function Rn(e) {
  return e == null;
}
function En(e) {
  const t = e.startsWith("^") ? 1 : 0, n = e.endsWith("$") ? e.length - 1 : e.length;
  return e.slice(t, n);
}
function Gs(e, t) {
  const n = e / t, o = Math.round(n), i = Number.EPSILON * Math.max(Math.abs(n), 1);
  return Math.abs(n - o) < i ? 0 : n - o;
}
const uo = /* @__PURE__ */ Symbol("evaluating");
function G(e, t, n) {
  let o;
  Object.defineProperty(e, t, {
    get() {
      if (o !== uo)
        return o === void 0 && (o = uo, o = n()), o;
    },
    set(i) {
      Object.defineProperty(e, t, {
        value: i
        // configurable: true,
      });
    },
    configurable: !0
  });
}
function tt(e, t, n) {
  Object.defineProperty(e, t, {
    value: n,
    writable: !0,
    enumerable: !0,
    configurable: !0
  });
}
function Ve(...e) {
  const t = {};
  for (const n of e) {
    const o = Object.getOwnPropertyDescriptors(n);
    Object.assign(t, o);
  }
  return Object.defineProperties({}, t);
}
function ho(e) {
  return JSON.stringify(e);
}
function Js(e) {
  return e.toLowerCase().trim().replace(/[^\w\s-]/g, "").replace(/[\s_-]+/g, "-").replace(/^-+|-+$/g, "");
}
const _i = "captureStackTrace" in Error ? Error.captureStackTrace : (...e) => {
};
function It(e) {
  return typeof e == "object" && e !== null && !Array.isArray(e);
}
const qs = /* @__PURE__ */ Xt(() => {
  if (Mn.jitless || typeof navigator < "u" && navigator?.userAgent?.includes("Cloudflare"))
    return !1;
  try {
    const e = Function;
    return new e(""), !0;
  } catch {
    return !1;
  }
});
function ut(e) {
  if (It(e) === !1)
    return !1;
  const t = e.constructor;
  if (t === void 0 || typeof t != "function")
    return !0;
  const n = t.prototype;
  return !(It(n) === !1 || Object.prototype.hasOwnProperty.call(n, "isPrototypeOf") === !1);
}
function ki(e) {
  return ut(e) ? { ...e } : Array.isArray(e) ? [...e] : e instanceof Map ? new Map(e) : e instanceof Set ? new Set(e) : e;
}
const Ks = /* @__PURE__ */ new Set(["string", "number", "symbol"]);
function ht(e) {
  return e.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
function We(e, t, n) {
  const o = new e._zod.constr(t ?? e._zod.def);
  return (!t || n?.parent) && (o._zod.parent = e), o;
}
function Z(e) {
  const t = e;
  if (!t)
    return {};
  if (typeof t == "string")
    return { error: () => t };
  if (t?.message !== void 0) {
    if (t?.error !== void 0)
      throw new Error("Cannot specify both `message` and `error` params");
    t.error = t.message;
  }
  return delete t.message, typeof t.error == "string" ? { ...t, error: () => t.error } : t;
}
function Qs(e) {
  return Object.keys(e).filter((t) => e[t]._zod.optin === "optional" && e[t]._zod.optout === "optional");
}
const ea = {
  safeint: [Number.MIN_SAFE_INTEGER, Number.MAX_SAFE_INTEGER],
  int32: [-2147483648, 2147483647],
  uint32: [0, 4294967295],
  float32: [-34028234663852886e22, 34028234663852886e22],
  float64: [-Number.MAX_VALUE, Number.MAX_VALUE]
};
function ta(e, t) {
  const n = e._zod.def, o = n.checks;
  if (o && o.length > 0)
    throw new Error(".pick() cannot be used on object schemas containing refinements");
  const s = Ve(e._zod.def, {
    get shape() {
      const r = {};
      for (const a in t) {
        if (!(a in n.shape))
          throw new Error(`Unrecognized key: "${a}"`);
        t[a] && (r[a] = n.shape[a]);
      }
      return tt(this, "shape", r), r;
    },
    checks: []
  });
  return We(e, s);
}
function na(e, t) {
  const n = e._zod.def, o = n.checks;
  if (o && o.length > 0)
    throw new Error(".omit() cannot be used on object schemas containing refinements");
  const s = Ve(e._zod.def, {
    get shape() {
      const r = { ...e._zod.def.shape };
      for (const a in t) {
        if (!(a in n.shape))
          throw new Error(`Unrecognized key: "${a}"`);
        t[a] && delete r[a];
      }
      return tt(this, "shape", r), r;
    },
    checks: []
  });
  return We(e, s);
}
function oa(e, t) {
  if (!ut(t))
    throw new Error("Invalid input to extend: expected a plain object");
  const n = e._zod.def.checks;
  if (n && n.length > 0) {
    const s = e._zod.def.shape;
    for (const r in t)
      if (Object.getOwnPropertyDescriptor(s, r) !== void 0)
        throw new Error("Cannot overwrite keys on object schemas containing refinements. Use `.safeExtend()` instead.");
  }
  const i = Ve(e._zod.def, {
    get shape() {
      const s = { ...e._zod.def.shape, ...t };
      return tt(this, "shape", s), s;
    }
  });
  return We(e, i);
}
function ia(e, t) {
  if (!ut(t))
    throw new Error("Invalid input to safeExtend: expected a plain object");
  const n = Ve(e._zod.def, {
    get shape() {
      const o = { ...e._zod.def.shape, ...t };
      return tt(this, "shape", o), o;
    }
  });
  return We(e, n);
}
function ra(e, t) {
  if (e._zod.def.checks?.length)
    throw new Error(".merge() cannot be used on object schemas containing refinements. Use .safeExtend() instead.");
  const n = Ve(e._zod.def, {
    get shape() {
      const o = { ...e._zod.def.shape, ...t._zod.def.shape };
      return tt(this, "shape", o), o;
    },
    get catchall() {
      return t._zod.def.catchall;
    },
    checks: t._zod.def.checks ?? []
  });
  return We(e, n);
}
function sa(e, t, n) {
  const i = t._zod.def.checks;
  if (i && i.length > 0)
    throw new Error(".partial() cannot be used on object schemas containing refinements");
  const r = Ve(t._zod.def, {
    get shape() {
      const a = t._zod.def.shape, c = { ...a };
      if (n)
        for (const u in n) {
          if (!(u in a))
            throw new Error(`Unrecognized key: "${u}"`);
          n[u] && (c[u] = e ? new e({
            type: "optional",
            innerType: a[u]
          }) : a[u]);
        }
      else
        for (const u in a)
          c[u] = e ? new e({
            type: "optional",
            innerType: a[u]
          }) : a[u];
      return tt(this, "shape", c), c;
    },
    checks: []
  });
  return We(t, r);
}
function aa(e, t, n) {
  const o = Ve(t._zod.def, {
    get shape() {
      const i = t._zod.def.shape, s = { ...i };
      if (n)
        for (const r in n) {
          if (!(r in s))
            throw new Error(`Unrecognized key: "${r}"`);
          n[r] && (s[r] = new e({
            type: "nonoptional",
            innerType: i[r]
          }));
        }
      else
        for (const r in i)
          s[r] = new e({
            type: "nonoptional",
            innerType: i[r]
          });
      return tt(this, "shape", s), s;
    }
  });
  return We(t, o);
}
function it(e, t = 0) {
  if (e.aborted === !0)
    return !0;
  for (let n = t; n < e.issues.length; n++)
    if (e.issues[n]?.continue !== !0)
      return !0;
  return !1;
}
function ca(e, t = 0) {
  if (e.aborted === !0)
    return !0;
  for (let n = t; n < e.issues.length; n++)
    if (e.issues[n]?.continue === !1)
      return !0;
  return !1;
}
function Ue(e, t) {
  return t.map((n) => {
    var o;
    return (o = n).path ?? (o.path = []), n.path.unshift(e), n;
  });
}
function Ot(e) {
  return typeof e == "string" ? e : e?.message;
}
function De(e, t, n) {
  const o = e.message ? e.message : Ot(e.inst?._zod.def?.error?.(e)) ?? Ot(t?.error?.(e)) ?? Ot(n.customError?.(e)) ?? Ot(n.localeError?.(e)) ?? "Invalid input", { inst: i, continue: s, input: r, ...a } = e;
  return a.path ?? (a.path = []), a.message = o, t?.reportInput && (a.input = r), a;
}
function zn(e) {
  return Array.isArray(e) ? "array" : typeof e == "string" ? "string" : "unknown";
}
function Pt(...e) {
  const [t, n, o] = e;
  return typeof t == "string" ? {
    message: t,
    code: "custom",
    input: n,
    inst: o
  } : { ...t };
}
const wi = (e, t) => {
  e.name = "$ZodError", Object.defineProperty(e, "_zod", {
    value: e._zod,
    enumerable: !1
  }), Object.defineProperty(e, "issues", {
    value: t,
    enumerable: !1
  }), e.message = JSON.stringify(t, yn, 2), Object.defineProperty(e, "toString", {
    value: () => e.message,
    enumerable: !1
  });
}, Ii = S("$ZodError", wi), Pi = S("$ZodError", wi, { Parent: Error });
function ua(e, t = (n) => n.message) {
  const n = {}, o = [];
  for (const i of e.issues)
    i.path.length > 0 ? (n[i.path[0]] = n[i.path[0]] || [], n[i.path[0]].push(t(i))) : o.push(t(i));
  return { formErrors: o, fieldErrors: n };
}
function ha(e, t = (n) => n.message) {
  const n = { _errors: [] }, o = (i, s = []) => {
    for (const r of i.issues)
      if (r.code === "invalid_union" && r.errors.length)
        r.errors.map((a) => o({ issues: a }, [...s, ...r.path]));
      else if (r.code === "invalid_key")
        o({ issues: r.issues }, [...s, ...r.path]);
      else if (r.code === "invalid_element")
        o({ issues: r.issues }, [...s, ...r.path]);
      else {
        const a = [...s, ...r.path];
        if (a.length === 0)
          n._errors.push(t(r));
        else {
          let c = n, u = 0;
          for (; u < a.length; ) {
            const h = a[u];
            u === a.length - 1 ? (c[h] = c[h] || { _errors: [] }, c[h]._errors.push(t(r))) : c[h] = c[h] || { _errors: [] }, c = c[h], u++;
          }
        }
      }
  };
  return o(e), n;
}
const Cn = (e) => (t, n, o, i) => {
  const s = o ? { ...o, async: !1 } : { async: !1 }, r = t._zod.run({ value: n, issues: [] }, s);
  if (r instanceof Promise)
    throw new st();
  if (r.issues.length) {
    const a = new (i?.Err ?? e)(r.issues.map((c) => De(c, s, Ze())));
    throw _i(a, i?.callee), a;
  }
  return r.value;
}, Tn = (e) => async (t, n, o, i) => {
  const s = o ? { ...o, async: !0 } : { async: !0 };
  let r = t._zod.run({ value: n, issues: [] }, s);
  if (r instanceof Promise && (r = await r), r.issues.length) {
    const a = new (i?.Err ?? e)(r.issues.map((c) => De(c, s, Ze())));
    throw _i(a, i?.callee), a;
  }
  return r.value;
}, Ut = (e) => (t, n, o) => {
  const i = o ? { ...o, async: !1 } : { async: !1 }, s = t._zod.run({ value: n, issues: [] }, i);
  if (s instanceof Promise)
    throw new st();
  return s.issues.length ? {
    success: !1,
    error: new (e ?? Ii)(s.issues.map((r) => De(r, i, Ze())))
  } : { success: !0, data: s.value };
}, da = /* @__PURE__ */ Ut(Pi), jt = (e) => async (t, n, o) => {
  const i = o ? { ...o, async: !0 } : { async: !0 };
  let s = t._zod.run({ value: n, issues: [] }, i);
  return s instanceof Promise && (s = await s), s.issues.length ? {
    success: !1,
    error: new e(s.issues.map((r) => De(r, i, Ze())))
  } : { success: !0, data: s.value };
}, la = /* @__PURE__ */ jt(Pi), fa = (e) => (t, n, o) => {
  const i = o ? { ...o, direction: "backward" } : { direction: "backward" };
  return Cn(e)(t, n, i);
}, pa = (e) => (t, n, o) => Cn(e)(t, n, o), ma = (e) => async (t, n, o) => {
  const i = o ? { ...o, direction: "backward" } : { direction: "backward" };
  return Tn(e)(t, n, i);
}, ga = (e) => async (t, n, o) => Tn(e)(t, n, o), ya = (e) => (t, n, o) => {
  const i = o ? { ...o, direction: "backward" } : { direction: "backward" };
  return Ut(e)(t, n, i);
}, va = (e) => (t, n, o) => Ut(e)(t, n, o), xa = (e) => async (t, n, o) => {
  const i = o ? { ...o, direction: "backward" } : { direction: "backward" };
  return jt(e)(t, n, i);
}, ba = (e) => async (t, n, o) => jt(e)(t, n, o), _a = /^[cC][0-9a-z]{6,}$/, ka = /^[0-9a-z]+$/, wa = /^[0-9A-HJKMNP-TV-Za-hjkmnp-tv-z]{26}$/, Ia = /^[0-9a-vA-V]{20}$/, Pa = /^[A-Za-z0-9]{27}$/, Sa = /^[a-zA-Z0-9_-]{21}$/, Ma = /^P(?:(\d+W)|(?!.*W)(?=\d|T\d)(\d+Y)?(\d+M)?(\d+D)?(T(?=\d)(\d+H)?(\d+M)?(\d+([.,]\d+)?S)?)?)$/, Ra = /^([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})$/, lo = (e) => e ? new RegExp(`^([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-${e}[0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12})$`) : /^([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-8][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}|00000000-0000-0000-0000-000000000000|ffffffff-ffff-ffff-ffff-ffffffffffff)$/, Ea = /^(?!\.)(?!.*\.\.)([A-Za-z0-9_'+\-\.]*)[A-Za-z0-9_+-]@([A-Za-z0-9][A-Za-z0-9\-]*\.)+[A-Za-z]{2,}$/, za = "^(\\p{Extended_Pictographic}|\\p{Emoji_Component})+$";
function Ca() {
  return new RegExp(za, "u");
}
const Ta = /^(?:(?:25[0-5]|2[0-4][0-9]|1[0-9][0-9]|[1-9][0-9]|[0-9])\.){3}(?:25[0-5]|2[0-4][0-9]|1[0-9][0-9]|[1-9][0-9]|[0-9])$/, La = /^(([0-9a-fA-F]{1,4}:){7}[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,7}:|([0-9a-fA-F]{1,4}:){1,6}:[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,5}(:[0-9a-fA-F]{1,4}){1,2}|([0-9a-fA-F]{1,4}:){1,4}(:[0-9a-fA-F]{1,4}){1,3}|([0-9a-fA-F]{1,4}:){1,3}(:[0-9a-fA-F]{1,4}){1,4}|([0-9a-fA-F]{1,4}:){1,2}(:[0-9a-fA-F]{1,4}){1,5}|[0-9a-fA-F]{1,4}:((:[0-9a-fA-F]{1,4}){1,6})|:((:[0-9a-fA-F]{1,4}){1,7}|:))$/, Oa = /^((25[0-5]|2[0-4][0-9]|1[0-9][0-9]|[1-9][0-9]|[0-9])\.){3}(25[0-5]|2[0-4][0-9]|1[0-9][0-9]|[1-9][0-9]|[0-9])\/([0-9]|[1-2][0-9]|3[0-2])$/, $a = /^(([0-9a-fA-F]{1,4}:){7}[0-9a-fA-F]{1,4}|::|([0-9a-fA-F]{1,4})?::([0-9a-fA-F]{1,4}:?){0,6})\/(12[0-8]|1[01][0-9]|[1-9]?[0-9])$/, Aa = /^$|^(?:[0-9a-zA-Z+/]{4})*(?:(?:[0-9a-zA-Z+/]{2}==)|(?:[0-9a-zA-Z+/]{3}=))?$/, Si = /^[A-Za-z0-9_-]*$/, Na = /^https?$/, Za = /^\+[1-9]\d{6,14}$/, Mi = "(?:(?:\\d\\d[2468][048]|\\d\\d[13579][26]|\\d\\d0[48]|[02468][048]00|[13579][26]00)-02-29|\\d{4}-(?:(?:0[13578]|1[02])-(?:0[1-9]|[12]\\d|3[01])|(?:0[469]|11)-(?:0[1-9]|[12]\\d|30)|(?:02)-(?:0[1-9]|1\\d|2[0-8])))", Da = /* @__PURE__ */ new RegExp(`^${Mi}$`);
function Ri(e) {
  const t = "(?:[01]\\d|2[0-3]):[0-5]\\d";
  return typeof e.precision == "number" ? e.precision === -1 ? `${t}` : e.precision === 0 ? `${t}:[0-5]\\d` : `${t}:[0-5]\\d\\.\\d{${e.precision}}` : `${t}(?::[0-5]\\d(?:\\.\\d+)?)?`;
}
function Fa(e) {
  return new RegExp(`^${Ri(e)}$`);
}
function Wa(e) {
  const t = Ri({ precision: e.precision }), n = ["Z"];
  e.local && n.push(""), e.offset && n.push("([+-](?:[01]\\d|2[0-3]):[0-5]\\d)");
  const o = `${t}(?:${n.join("|")})`;
  return new RegExp(`^${Mi}T(?:${o})$`);
}
const Ba = (e) => {
  const t = e ? `[\\s\\S]{${e?.minimum ?? 0},${e?.maximum ?? ""}}` : "[\\s\\S]*";
  return new RegExp(`^${t}$`);
}, Ha = /^-?\d+$/, Ei = /^-?\d+(?:\.\d+)?$/, Ya = /^(?:true|false)$/i, Xa = /^[^A-Z]*$/, Ua = /^[^a-z]*$/, ye = /* @__PURE__ */ S("$ZodCheck", (e, t) => {
  var n;
  e._zod ?? (e._zod = {}), e._zod.def = t, (n = e._zod).onattach ?? (n.onattach = []);
}), zi = {
  number: "number",
  bigint: "bigint",
  object: "date"
}, Ci = /* @__PURE__ */ S("$ZodCheckLessThan", (e, t) => {
  ye.init(e, t);
  const n = zi[typeof t.value];
  e._zod.onattach.push((o) => {
    const i = o._zod.bag, s = (t.inclusive ? i.maximum : i.exclusiveMaximum) ?? Number.POSITIVE_INFINITY;
    t.value < s && (t.inclusive ? i.maximum = t.value : i.exclusiveMaximum = t.value);
  }), e._zod.check = (o) => {
    (t.inclusive ? o.value <= t.value : o.value < t.value) || o.issues.push({
      origin: n,
      code: "too_big",
      maximum: typeof t.value == "object" ? t.value.getTime() : t.value,
      input: o.value,
      inclusive: t.inclusive,
      inst: e,
      continue: !t.abort
    });
  };
}), Ti = /* @__PURE__ */ S("$ZodCheckGreaterThan", (e, t) => {
  ye.init(e, t);
  const n = zi[typeof t.value];
  e._zod.onattach.push((o) => {
    const i = o._zod.bag, s = (t.inclusive ? i.minimum : i.exclusiveMinimum) ?? Number.NEGATIVE_INFINITY;
    t.value > s && (t.inclusive ? i.minimum = t.value : i.exclusiveMinimum = t.value);
  }), e._zod.check = (o) => {
    (t.inclusive ? o.value >= t.value : o.value > t.value) || o.issues.push({
      origin: n,
      code: "too_small",
      minimum: typeof t.value == "object" ? t.value.getTime() : t.value,
      input: o.value,
      inclusive: t.inclusive,
      inst: e,
      continue: !t.abort
    });
  };
}), ja = /* @__PURE__ */ S("$ZodCheckMultipleOf", (e, t) => {
  ye.init(e, t), e._zod.onattach.push((n) => {
    var o;
    (o = n._zod.bag).multipleOf ?? (o.multipleOf = t.value);
  }), e._zod.check = (n) => {
    if (typeof n.value != typeof t.value)
      throw new Error("Cannot mix number and bigint in multiple_of check.");
    (typeof n.value == "bigint" ? n.value % t.value === BigInt(0) : Gs(n.value, t.value) === 0) || n.issues.push({
      origin: typeof n.value,
      code: "not_multiple_of",
      divisor: t.value,
      input: n.value,
      inst: e,
      continue: !t.abort
    });
  };
}), Va = /* @__PURE__ */ S("$ZodCheckNumberFormat", (e, t) => {
  ye.init(e, t), t.format = t.format || "float64";
  const n = t.format?.includes("int"), o = n ? "int" : "number", [i, s] = ea[t.format];
  e._zod.onattach.push((r) => {
    const a = r._zod.bag;
    a.format = t.format, a.minimum = i, a.maximum = s, n && (a.pattern = Ha);
  }), e._zod.check = (r) => {
    const a = r.value;
    if (n) {
      if (!Number.isInteger(a)) {
        r.issues.push({
          expected: o,
          format: t.format,
          code: "invalid_type",
          continue: !1,
          input: a,
          inst: e
        });
        return;
      }
      if (!Number.isSafeInteger(a)) {
        a > 0 ? r.issues.push({
          input: a,
          code: "too_big",
          maximum: Number.MAX_SAFE_INTEGER,
          note: "Integers must be within the safe integer range.",
          inst: e,
          origin: o,
          inclusive: !0,
          continue: !t.abort
        }) : r.issues.push({
          input: a,
          code: "too_small",
          minimum: Number.MIN_SAFE_INTEGER,
          note: "Integers must be within the safe integer range.",
          inst: e,
          origin: o,
          inclusive: !0,
          continue: !t.abort
        });
        return;
      }
    }
    a < i && r.issues.push({
      origin: "number",
      input: a,
      code: "too_small",
      minimum: i,
      inclusive: !0,
      inst: e,
      continue: !t.abort
    }), a > s && r.issues.push({
      origin: "number",
      input: a,
      code: "too_big",
      maximum: s,
      inclusive: !0,
      inst: e,
      continue: !t.abort
    });
  };
}), Ga = /* @__PURE__ */ S("$ZodCheckMaxLength", (e, t) => {
  var n;
  ye.init(e, t), (n = e._zod.def).when ?? (n.when = (o) => {
    const i = o.value;
    return !Rn(i) && i.length !== void 0;
  }), e._zod.onattach.push((o) => {
    const i = o._zod.bag.maximum ?? Number.POSITIVE_INFINITY;
    t.maximum < i && (o._zod.bag.maximum = t.maximum);
  }), e._zod.check = (o) => {
    const i = o.value;
    if (i.length <= t.maximum)
      return;
    const r = zn(i);
    o.issues.push({
      origin: r,
      code: "too_big",
      maximum: t.maximum,
      inclusive: !0,
      input: i,
      inst: e,
      continue: !t.abort
    });
  };
}), Ja = /* @__PURE__ */ S("$ZodCheckMinLength", (e, t) => {
  var n;
  ye.init(e, t), (n = e._zod.def).when ?? (n.when = (o) => {
    const i = o.value;
    return !Rn(i) && i.length !== void 0;
  }), e._zod.onattach.push((o) => {
    const i = o._zod.bag.minimum ?? Number.NEGATIVE_INFINITY;
    t.minimum > i && (o._zod.bag.minimum = t.minimum);
  }), e._zod.check = (o) => {
    const i = o.value;
    if (i.length >= t.minimum)
      return;
    const r = zn(i);
    o.issues.push({
      origin: r,
      code: "too_small",
      minimum: t.minimum,
      inclusive: !0,
      input: i,
      inst: e,
      continue: !t.abort
    });
  };
}), qa = /* @__PURE__ */ S("$ZodCheckLengthEquals", (e, t) => {
  var n;
  ye.init(e, t), (n = e._zod.def).when ?? (n.when = (o) => {
    const i = o.value;
    return !Rn(i) && i.length !== void 0;
  }), e._zod.onattach.push((o) => {
    const i = o._zod.bag;
    i.minimum = t.length, i.maximum = t.length, i.length = t.length;
  }), e._zod.check = (o) => {
    const i = o.value, s = i.length;
    if (s === t.length)
      return;
    const r = zn(i), a = s > t.length;
    o.issues.push({
      origin: r,
      ...a ? { code: "too_big", maximum: t.length } : { code: "too_small", minimum: t.length },
      inclusive: !0,
      exact: !0,
      input: o.value,
      inst: e,
      continue: !t.abort
    });
  };
}), Vt = /* @__PURE__ */ S("$ZodCheckStringFormat", (e, t) => {
  var n, o;
  ye.init(e, t), e._zod.onattach.push((i) => {
    const s = i._zod.bag;
    s.format = t.format, t.pattern && (s.patterns ?? (s.patterns = /* @__PURE__ */ new Set()), s.patterns.add(t.pattern));
  }), t.pattern ? (n = e._zod).check ?? (n.check = (i) => {
    t.pattern.lastIndex = 0, !t.pattern.test(i.value) && i.issues.push({
      origin: "string",
      code: "invalid_format",
      format: t.format,
      input: i.value,
      ...t.pattern ? { pattern: t.pattern.toString() } : {},
      inst: e,
      continue: !t.abort
    });
  }) : (o = e._zod).check ?? (o.check = () => {
  });
}), Ka = /* @__PURE__ */ S("$ZodCheckRegex", (e, t) => {
  Vt.init(e, t), e._zod.check = (n) => {
    t.pattern.lastIndex = 0, !t.pattern.test(n.value) && n.issues.push({
      origin: "string",
      code: "invalid_format",
      format: "regex",
      input: n.value,
      pattern: t.pattern.toString(),
      inst: e,
      continue: !t.abort
    });
  };
}), Qa = /* @__PURE__ */ S("$ZodCheckLowerCase", (e, t) => {
  t.pattern ?? (t.pattern = Xa), Vt.init(e, t);
}), ec = /* @__PURE__ */ S("$ZodCheckUpperCase", (e, t) => {
  t.pattern ?? (t.pattern = Ua), Vt.init(e, t);
}), tc = /* @__PURE__ */ S("$ZodCheckIncludes", (e, t) => {
  ye.init(e, t);
  const n = ht(t.includes), o = new RegExp(typeof t.position == "number" ? `^.{${t.position}}${n}` : n);
  t.pattern = o, e._zod.onattach.push((i) => {
    const s = i._zod.bag;
    s.patterns ?? (s.patterns = /* @__PURE__ */ new Set()), s.patterns.add(o);
  }), e._zod.check = (i) => {
    i.value.includes(t.includes, t.position) || i.issues.push({
      origin: "string",
      code: "invalid_format",
      format: "includes",
      includes: t.includes,
      input: i.value,
      inst: e,
      continue: !t.abort
    });
  };
}), nc = /* @__PURE__ */ S("$ZodCheckStartsWith", (e, t) => {
  ye.init(e, t);
  const n = new RegExp(`^${ht(t.prefix)}.*`);
  t.pattern ?? (t.pattern = n), e._zod.onattach.push((o) => {
    const i = o._zod.bag;
    i.patterns ?? (i.patterns = /* @__PURE__ */ new Set()), i.patterns.add(n);
  }), e._zod.check = (o) => {
    o.value.startsWith(t.prefix) || o.issues.push({
      origin: "string",
      code: "invalid_format",
      format: "starts_with",
      prefix: t.prefix,
      input: o.value,
      inst: e,
      continue: !t.abort
    });
  };
}), oc = /* @__PURE__ */ S("$ZodCheckEndsWith", (e, t) => {
  ye.init(e, t);
  const n = new RegExp(`.*${ht(t.suffix)}$`);
  t.pattern ?? (t.pattern = n), e._zod.onattach.push((o) => {
    const i = o._zod.bag;
    i.patterns ?? (i.patterns = /* @__PURE__ */ new Set()), i.patterns.add(n);
  }), e._zod.check = (o) => {
    o.value.endsWith(t.suffix) || o.issues.push({
      origin: "string",
      code: "invalid_format",
      format: "ends_with",
      suffix: t.suffix,
      input: o.value,
      inst: e,
      continue: !t.abort
    });
  };
}), ic = /* @__PURE__ */ S("$ZodCheckOverwrite", (e, t) => {
  ye.init(e, t), e._zod.check = (n) => {
    n.value = t.tx(n.value);
  };
});
class rc {
  constructor(t = []) {
    this.content = [], this.indent = 0, this && (this.args = t);
  }
  indented(t) {
    this.indent += 1, t(this), this.indent -= 1;
  }
  write(t) {
    if (typeof t == "function") {
      t(this, { execution: "sync" }), t(this, { execution: "async" });
      return;
    }
    const o = t.split(`
`).filter((r) => r), i = Math.min(...o.map((r) => r.length - r.trimStart().length)), s = o.map((r) => r.slice(i)).map((r) => " ".repeat(this.indent * 2) + r);
    for (const r of s)
      this.content.push(r);
  }
  compile() {
    const t = Function, n = this?.args, i = [...(this?.content ?? [""]).map((s) => `  ${s}`)];
    return new t(...n, i.join(`
`));
  }
}
const sc = {
  major: 4,
  minor: 4,
  patch: 3
}, K = /* @__PURE__ */ S("$ZodType", (e, t) => {
  var n;
  e ?? (e = {}), e._zod.def = t, e._zod.bag = e._zod.bag || {}, e._zod.version = sc;
  const o = [...e._zod.def.checks ?? []];
  e._zod.traits.has("$ZodCheck") && o.unshift(e);
  for (const i of o)
    for (const s of i._zod.onattach)
      s(e);
  if (o.length === 0)
    (n = e._zod).deferred ?? (n.deferred = []), e._zod.deferred?.push(() => {
      e._zod.run = e._zod.parse;
    });
  else {
    const i = (r, a, c) => {
      let u = it(r), h;
      for (const d of a) {
        if (d._zod.def.when) {
          if (ca(r) || !d._zod.def.when(r))
            continue;
        } else if (u)
          continue;
        const f = r.issues.length, m = d._zod.check(r);
        if (m instanceof Promise && c?.async === !1)
          throw new st();
        if (h || m instanceof Promise)
          h = (h ?? Promise.resolve()).then(async () => {
            await m, r.issues.length !== f && (u || (u = it(r, f)));
          });
        else {
          if (r.issues.length === f)
            continue;
          u || (u = it(r, f));
        }
      }
      return h ? h.then(() => r) : r;
    }, s = (r, a, c) => {
      if (it(r))
        return r.aborted = !0, r;
      const u = i(a, o, c);
      if (u instanceof Promise) {
        if (c.async === !1)
          throw new st();
        return u.then((h) => e._zod.parse(h, c));
      }
      return e._zod.parse(u, c);
    };
    e._zod.run = (r, a) => {
      if (a.skipChecks)
        return e._zod.parse(r, a);
      if (a.direction === "backward") {
        const u = e._zod.parse({ value: r.value, issues: [] }, { ...a, skipChecks: !0 });
        return u instanceof Promise ? u.then((h) => s(h, r, a)) : s(u, r, a);
      }
      const c = e._zod.parse(r, a);
      if (c instanceof Promise) {
        if (a.async === !1)
          throw new st();
        return c.then((u) => i(u, o, a));
      }
      return i(c, o, a);
    };
  }
  G(e, "~standard", () => ({
    validate: (i) => {
      try {
        const s = da(e, i);
        return s.success ? { value: s.data } : { issues: s.error?.issues };
      } catch {
        return la(e, i).then((r) => r.success ? { value: r.data } : { issues: r.error?.issues });
      }
    },
    vendor: "zod",
    version: 1
  }));
}), Ln = /* @__PURE__ */ S("$ZodString", (e, t) => {
  K.init(e, t), e._zod.pattern = [...e?._zod.bag?.patterns ?? []].pop() ?? Ba(e._zod.bag), e._zod.parse = (n, o) => {
    if (t.coerce)
      try {
        n.value = String(n.value);
      } catch {
      }
    return typeof n.value == "string" || n.issues.push({
      expected: "string",
      code: "invalid_type",
      input: n.value,
      inst: e
    }), n;
  };
}), ee = /* @__PURE__ */ S("$ZodStringFormat", (e, t) => {
  Vt.init(e, t), Ln.init(e, t);
}), ac = /* @__PURE__ */ S("$ZodGUID", (e, t) => {
  t.pattern ?? (t.pattern = Ra), ee.init(e, t);
}), cc = /* @__PURE__ */ S("$ZodUUID", (e, t) => {
  if (t.version) {
    const o = {
      v1: 1,
      v2: 2,
      v3: 3,
      v4: 4,
      v5: 5,
      v6: 6,
      v7: 7,
      v8: 8
    }[t.version];
    if (o === void 0)
      throw new Error(`Invalid UUID version: "${t.version}"`);
    t.pattern ?? (t.pattern = lo(o));
  } else
    t.pattern ?? (t.pattern = lo());
  ee.init(e, t);
}), uc = /* @__PURE__ */ S("$ZodEmail", (e, t) => {
  t.pattern ?? (t.pattern = Ea), ee.init(e, t);
}), hc = /* @__PURE__ */ S("$ZodURL", (e, t) => {
  ee.init(e, t), e._zod.check = (n) => {
    try {
      const o = n.value.trim();
      if (!t.normalize && t.protocol?.source === Na.source && !/^https?:\/\//i.test(o)) {
        n.issues.push({
          code: "invalid_format",
          format: "url",
          note: "Invalid URL format",
          input: n.value,
          inst: e,
          continue: !t.abort
        });
        return;
      }
      const i = new URL(o);
      t.hostname && (t.hostname.lastIndex = 0, t.hostname.test(i.hostname) || n.issues.push({
        code: "invalid_format",
        format: "url",
        note: "Invalid hostname",
        pattern: t.hostname.source,
        input: n.value,
        inst: e,
        continue: !t.abort
      })), t.protocol && (t.protocol.lastIndex = 0, t.protocol.test(i.protocol.endsWith(":") ? i.protocol.slice(0, -1) : i.protocol) || n.issues.push({
        code: "invalid_format",
        format: "url",
        note: "Invalid protocol",
        pattern: t.protocol.source,
        input: n.value,
        inst: e,
        continue: !t.abort
      })), t.normalize ? n.value = i.href : n.value = o;
      return;
    } catch {
      n.issues.push({
        code: "invalid_format",
        format: "url",
        input: n.value,
        inst: e,
        continue: !t.abort
      });
    }
  };
}), dc = /* @__PURE__ */ S("$ZodEmoji", (e, t) => {
  t.pattern ?? (t.pattern = Ca()), ee.init(e, t);
}), lc = /* @__PURE__ */ S("$ZodNanoID", (e, t) => {
  t.pattern ?? (t.pattern = Sa), ee.init(e, t);
}), fc = /* @__PURE__ */ S("$ZodCUID", (e, t) => {
  t.pattern ?? (t.pattern = _a), ee.init(e, t);
}), pc = /* @__PURE__ */ S("$ZodCUID2", (e, t) => {
  t.pattern ?? (t.pattern = ka), ee.init(e, t);
}), mc = /* @__PURE__ */ S("$ZodULID", (e, t) => {
  t.pattern ?? (t.pattern = wa), ee.init(e, t);
}), gc = /* @__PURE__ */ S("$ZodXID", (e, t) => {
  t.pattern ?? (t.pattern = Ia), ee.init(e, t);
}), yc = /* @__PURE__ */ S("$ZodKSUID", (e, t) => {
  t.pattern ?? (t.pattern = Pa), ee.init(e, t);
}), vc = /* @__PURE__ */ S("$ZodISODateTime", (e, t) => {
  t.pattern ?? (t.pattern = Wa(t)), ee.init(e, t);
}), xc = /* @__PURE__ */ S("$ZodISODate", (e, t) => {
  t.pattern ?? (t.pattern = Da), ee.init(e, t);
}), bc = /* @__PURE__ */ S("$ZodISOTime", (e, t) => {
  t.pattern ?? (t.pattern = Fa(t)), ee.init(e, t);
}), _c = /* @__PURE__ */ S("$ZodISODuration", (e, t) => {
  t.pattern ?? (t.pattern = Ma), ee.init(e, t);
}), kc = /* @__PURE__ */ S("$ZodIPv4", (e, t) => {
  t.pattern ?? (t.pattern = Ta), ee.init(e, t), e._zod.bag.format = "ipv4";
}), wc = /* @__PURE__ */ S("$ZodIPv6", (e, t) => {
  t.pattern ?? (t.pattern = La), ee.init(e, t), e._zod.bag.format = "ipv6", e._zod.check = (n) => {
    try {
      new URL(`http://[${n.value}]`);
    } catch {
      n.issues.push({
        code: "invalid_format",
        format: "ipv6",
        input: n.value,
        inst: e,
        continue: !t.abort
      });
    }
  };
}), Ic = /* @__PURE__ */ S("$ZodCIDRv4", (e, t) => {
  t.pattern ?? (t.pattern = Oa), ee.init(e, t);
}), Pc = /* @__PURE__ */ S("$ZodCIDRv6", (e, t) => {
  t.pattern ?? (t.pattern = $a), ee.init(e, t), e._zod.check = (n) => {
    const o = n.value.split("/");
    try {
      if (o.length !== 2)
        throw new Error();
      const [i, s] = o;
      if (!s)
        throw new Error();
      const r = Number(s);
      if (`${r}` !== s)
        throw new Error();
      if (r < 0 || r > 128)
        throw new Error();
      new URL(`http://[${i}]`);
    } catch {
      n.issues.push({
        code: "invalid_format",
        format: "cidrv6",
        input: n.value,
        inst: e,
        continue: !t.abort
      });
    }
  };
});
function Li(e) {
  if (e === "")
    return !0;
  if (/\s/.test(e) || e.length % 4 !== 0)
    return !1;
  try {
    return atob(e), !0;
  } catch {
    return !1;
  }
}
const Sc = /* @__PURE__ */ S("$ZodBase64", (e, t) => {
  t.pattern ?? (t.pattern = Aa), ee.init(e, t), e._zod.bag.contentEncoding = "base64", e._zod.check = (n) => {
    Li(n.value) || n.issues.push({
      code: "invalid_format",
      format: "base64",
      input: n.value,
      inst: e,
      continue: !t.abort
    });
  };
});
function Mc(e) {
  if (!Si.test(e))
    return !1;
  const t = e.replace(/[-_]/g, (o) => o === "-" ? "+" : "/"), n = t.padEnd(Math.ceil(t.length / 4) * 4, "=");
  return Li(n);
}
const Rc = /* @__PURE__ */ S("$ZodBase64URL", (e, t) => {
  t.pattern ?? (t.pattern = Si), ee.init(e, t), e._zod.bag.contentEncoding = "base64url", e._zod.check = (n) => {
    Mc(n.value) || n.issues.push({
      code: "invalid_format",
      format: "base64url",
      input: n.value,
      inst: e,
      continue: !t.abort
    });
  };
}), Ec = /* @__PURE__ */ S("$ZodE164", (e, t) => {
  t.pattern ?? (t.pattern = Za), ee.init(e, t);
});
function zc(e, t = null) {
  try {
    const n = e.split(".");
    if (n.length !== 3)
      return !1;
    const [o] = n;
    if (!o)
      return !1;
    const i = JSON.parse(atob(o));
    return !("typ" in i && i?.typ !== "JWT" || !i.alg || t && (!("alg" in i) || i.alg !== t));
  } catch {
    return !1;
  }
}
const Cc = /* @__PURE__ */ S("$ZodJWT", (e, t) => {
  ee.init(e, t), e._zod.check = (n) => {
    zc(n.value, t.alg) || n.issues.push({
      code: "invalid_format",
      format: "jwt",
      input: n.value,
      inst: e,
      continue: !t.abort
    });
  };
}), Oi = /* @__PURE__ */ S("$ZodNumber", (e, t) => {
  K.init(e, t), e._zod.pattern = e._zod.bag.pattern ?? Ei, e._zod.parse = (n, o) => {
    if (t.coerce)
      try {
        n.value = Number(n.value);
      } catch {
      }
    const i = n.value;
    if (typeof i == "number" && !Number.isNaN(i) && Number.isFinite(i))
      return n;
    const s = typeof i == "number" ? Number.isNaN(i) ? "NaN" : Number.isFinite(i) ? void 0 : "Infinity" : void 0;
    return n.issues.push({
      expected: "number",
      code: "invalid_type",
      input: i,
      inst: e,
      ...s ? { received: s } : {}
    }), n;
  };
}), Tc = /* @__PURE__ */ S("$ZodNumberFormat", (e, t) => {
  Va.init(e, t), Oi.init(e, t);
}), Lc = /* @__PURE__ */ S("$ZodBoolean", (e, t) => {
  K.init(e, t), e._zod.pattern = Ya, e._zod.parse = (n, o) => {
    if (t.coerce)
      try {
        n.value = !!n.value;
      } catch {
      }
    const i = n.value;
    return typeof i == "boolean" || n.issues.push({
      expected: "boolean",
      code: "invalid_type",
      input: i,
      inst: e
    }), n;
  };
}), Oc = /* @__PURE__ */ S("$ZodAny", (e, t) => {
  K.init(e, t), e._zod.parse = (n) => n;
}), $c = /* @__PURE__ */ S("$ZodUnknown", (e, t) => {
  K.init(e, t), e._zod.parse = (n) => n;
}), Ac = /* @__PURE__ */ S("$ZodNever", (e, t) => {
  K.init(e, t), e._zod.parse = (n, o) => (n.issues.push({
    expected: "never",
    code: "invalid_type",
    input: n.value,
    inst: e
  }), n);
});
function fo(e, t, n) {
  e.issues.length && t.issues.push(...Ue(n, e.issues)), t.value[n] = e.value;
}
const Nc = /* @__PURE__ */ S("$ZodArray", (e, t) => {
  K.init(e, t), e._zod.parse = (n, o) => {
    const i = n.value;
    if (!Array.isArray(i))
      return n.issues.push({
        expected: "array",
        code: "invalid_type",
        input: i,
        inst: e
      }), n;
    n.value = Array(i.length);
    const s = [];
    for (let r = 0; r < i.length; r++) {
      const a = i[r], c = t.element._zod.run({
        value: a,
        issues: []
      }, o);
      c instanceof Promise ? s.push(c.then((u) => fo(u, n, r))) : fo(c, n, r);
    }
    return s.length ? Promise.all(s).then(() => n) : n;
  };
});
function Wt(e, t, n, o, i, s) {
  const r = n in o;
  if (e.issues.length) {
    if (i && s && !r)
      return;
    t.issues.push(...Ue(n, e.issues));
  }
  if (!r && !i) {
    e.issues.length || t.issues.push({
      code: "invalid_type",
      expected: "nonoptional",
      input: void 0,
      path: [n]
    });
    return;
  }
  e.value === void 0 ? r && (t.value[n] = void 0) : t.value[n] = e.value;
}
function $i(e) {
  const t = Object.keys(e.shape);
  for (const o of t)
    if (!e.shape?.[o]?._zod?.traits?.has("$ZodType"))
      throw new Error(`Invalid element at key "${o}": expected a Zod schema`);
  const n = Qs(e.shape);
  return {
    ...e,
    keys: t,
    keySet: new Set(t),
    numKeys: t.length,
    optionalKeys: new Set(n)
  };
}
function Ai(e, t, n, o, i, s) {
  const r = [], a = i.keySet, c = i.catchall._zod, u = c.def.type, h = c.optin === "optional", d = c.optout === "optional";
  for (const f in t) {
    if (f === "__proto__" || a.has(f))
      continue;
    if (u === "never") {
      r.push(f);
      continue;
    }
    const m = c.run({ value: t[f], issues: [] }, o);
    m instanceof Promise ? e.push(m.then((l) => Wt(l, n, f, t, h, d))) : Wt(m, n, f, t, h, d);
  }
  return r.length && n.issues.push({
    code: "unrecognized_keys",
    keys: r,
    input: t,
    inst: s
  }), e.length ? Promise.all(e).then(() => n) : n;
}
const Zc = /* @__PURE__ */ S("$ZodObject", (e, t) => {
  if (K.init(e, t), !Object.getOwnPropertyDescriptor(t, "shape")?.get) {
    const a = t.shape;
    Object.defineProperty(t, "shape", {
      get: () => {
        const c = { ...a };
        return Object.defineProperty(t, "shape", {
          value: c
        }), c;
      }
    });
  }
  const o = Xt(() => $i(t));
  G(e._zod, "propValues", () => {
    const a = t.shape, c = {};
    for (const u in a) {
      const h = a[u]._zod;
      if (h.values) {
        c[u] ?? (c[u] = /* @__PURE__ */ new Set());
        for (const d of h.values)
          c[u].add(d);
      }
    }
    return c;
  });
  const i = It, s = t.catchall;
  let r;
  e._zod.parse = (a, c) => {
    r ?? (r = o.value);
    const u = a.value;
    if (!i(u))
      return a.issues.push({
        expected: "object",
        code: "invalid_type",
        input: u,
        inst: e
      }), a;
    a.value = {};
    const h = [], d = r.shape;
    for (const f of r.keys) {
      const m = d[f], l = m._zod.optin === "optional", p = m._zod.optout === "optional", x = m._zod.run({ value: u[f], issues: [] }, c);
      x instanceof Promise ? h.push(x.then((k) => Wt(k, a, f, u, l, p))) : Wt(x, a, f, u, l, p);
    }
    return s ? Ai(h, u, a, c, o.value, e) : h.length ? Promise.all(h).then(() => a) : a;
  };
}), Dc = /* @__PURE__ */ S("$ZodObjectJIT", (e, t) => {
  Zc.init(e, t);
  const n = e._zod.parse, o = Xt(() => $i(t)), i = (f) => {
    const m = new rc(["shape", "payload", "ctx"]), l = o.value, p = (P) => {
      const M = ho(P);
      return `shape[${M}]._zod.run({ value: input[${M}], issues: [] }, ctx)`;
    };
    m.write("const input = payload.value;");
    const x = /* @__PURE__ */ Object.create(null);
    let k = 0;
    for (const P of l.keys)
      x[P] = `key_${k++}`;
    m.write("const newResult = {};");
    for (const P of l.keys) {
      const M = x[P], _ = ho(P), B = f[P], $ = B?._zod?.optin === "optional", z = B?._zod?.optout === "optional";
      m.write(`const ${M} = ${p(P)};`), $ && z ? m.write(`
        if (${M}.issues.length) {
          if (${_} in input) {
            payload.issues = payload.issues.concat(${M}.issues.map(iss => ({
              ...iss,
              path: iss.path ? [${_}, ...iss.path] : [${_}]
            })));
          }
        }
        
        if (${M}.value === undefined) {
          if (${_} in input) {
            newResult[${_}] = undefined;
          }
        } else {
          newResult[${_}] = ${M}.value;
        }
        
      `) : $ ? m.write(`
        if (${M}.issues.length) {
          payload.issues = payload.issues.concat(${M}.issues.map(iss => ({
            ...iss,
            path: iss.path ? [${_}, ...iss.path] : [${_}]
          })));
        }
        
        if (${M}.value === undefined) {
          if (${_} in input) {
            newResult[${_}] = undefined;
          }
        } else {
          newResult[${_}] = ${M}.value;
        }
        
      `) : m.write(`
        const ${M}_present = ${_} in input;
        if (${M}.issues.length) {
          payload.issues = payload.issues.concat(${M}.issues.map(iss => ({
            ...iss,
            path: iss.path ? [${_}, ...iss.path] : [${_}]
          })));
        }
        if (!${M}_present && !${M}.issues.length) {
          payload.issues.push({
            code: "invalid_type",
            expected: "nonoptional",
            input: undefined,
            path: [${_}]
          });
        }

        if (${M}_present) {
          if (${M}.value === undefined) {
            newResult[${_}] = undefined;
          } else {
            newResult[${_}] = ${M}.value;
          }
        }

      `);
    }
    m.write("payload.value = newResult;"), m.write("return payload;");
    const w = m.compile();
    return (P, M) => w(f, P, M);
  };
  let s;
  const r = It, a = !Mn.jitless, u = a && qs.value, h = t.catchall;
  let d;
  e._zod.parse = (f, m) => {
    d ?? (d = o.value);
    const l = f.value;
    return r(l) ? a && u && m?.async === !1 && m.jitless !== !0 ? (s || (s = i(t.shape)), f = s(f, m), h ? Ai([], l, f, m, d, e) : f) : n(f, m) : (f.issues.push({
      expected: "object",
      code: "invalid_type",
      input: l,
      inst: e
    }), f);
  };
});
function po(e, t, n, o) {
  for (const s of e)
    if (s.issues.length === 0)
      return t.value = s.value, t;
  const i = e.filter((s) => !it(s));
  return i.length === 1 ? (t.value = i[0].value, i[0]) : (t.issues.push({
    code: "invalid_union",
    input: t.value,
    inst: n,
    errors: e.map((s) => s.issues.map((r) => De(r, o, Ze())))
  }), t);
}
const Ni = /* @__PURE__ */ S("$ZodUnion", (e, t) => {
  K.init(e, t), G(e._zod, "optin", () => t.options.some((o) => o._zod.optin === "optional") ? "optional" : void 0), G(e._zod, "optout", () => t.options.some((o) => o._zod.optout === "optional") ? "optional" : void 0), G(e._zod, "values", () => {
    if (t.options.every((o) => o._zod.values))
      return new Set(t.options.flatMap((o) => Array.from(o._zod.values)));
  }), G(e._zod, "pattern", () => {
    if (t.options.every((o) => o._zod.pattern)) {
      const o = t.options.map((i) => i._zod.pattern);
      return new RegExp(`^(${o.map((i) => En(i.source)).join("|")})$`);
    }
  });
  const n = t.options.length === 1 ? t.options[0]._zod.run : null;
  e._zod.parse = (o, i) => {
    if (n)
      return n(o, i);
    let s = !1;
    const r = [];
    for (const a of t.options) {
      const c = a._zod.run({
        value: o.value,
        issues: []
      }, i);
      if (c instanceof Promise)
        r.push(c), s = !0;
      else {
        if (c.issues.length === 0)
          return c;
        r.push(c);
      }
    }
    return s ? Promise.all(r).then((a) => po(a, o, e, i)) : po(r, o, e, i);
  };
}), Fc = /* @__PURE__ */ S("$ZodDiscriminatedUnion", (e, t) => {
  t.inclusive = !1, Ni.init(e, t);
  const n = e._zod.parse;
  G(e._zod, "propValues", () => {
    const i = {};
    for (const s of t.options) {
      const r = s._zod.propValues;
      if (!r || Object.keys(r).length === 0)
        throw new Error(`Invalid discriminated union option at index "${t.options.indexOf(s)}"`);
      for (const [a, c] of Object.entries(r)) {
        i[a] || (i[a] = /* @__PURE__ */ new Set());
        for (const u of c)
          i[a].add(u);
      }
    }
    return i;
  });
  const o = Xt(() => {
    const i = t.options, s = /* @__PURE__ */ new Map();
    for (const r of i) {
      const a = r._zod.propValues?.[t.discriminator];
      if (!a || a.size === 0)
        throw new Error(`Invalid discriminated union option at index "${t.options.indexOf(r)}"`);
      for (const c of a) {
        if (s.has(c))
          throw new Error(`Duplicate discriminator value "${String(c)}"`);
        s.set(c, r);
      }
    }
    return s;
  });
  e._zod.parse = (i, s) => {
    const r = i.value;
    if (!It(r))
      return i.issues.push({
        code: "invalid_type",
        expected: "object",
        input: r,
        inst: e
      }), i;
    const a = o.value.get(r?.[t.discriminator]);
    return a ? a._zod.run(i, s) : t.unionFallback || s.direction === "backward" ? n(i, s) : (i.issues.push({
      code: "invalid_union",
      errors: [],
      note: "No matching discriminator",
      discriminator: t.discriminator,
      options: Array.from(o.value.keys()),
      input: r,
      path: [t.discriminator],
      inst: e
    }), i);
  };
}), Wc = /* @__PURE__ */ S("$ZodIntersection", (e, t) => {
  K.init(e, t), e._zod.parse = (n, o) => {
    const i = n.value, s = t.left._zod.run({ value: i, issues: [] }, o), r = t.right._zod.run({ value: i, issues: [] }, o);
    return s instanceof Promise || r instanceof Promise ? Promise.all([s, r]).then(([c, u]) => mo(n, c, u)) : mo(n, s, r);
  };
});
function vn(e, t) {
  if (e === t)
    return { valid: !0, data: e };
  if (e instanceof Date && t instanceof Date && +e == +t)
    return { valid: !0, data: e };
  if (ut(e) && ut(t)) {
    const n = Object.keys(t), o = Object.keys(e).filter((s) => n.indexOf(s) !== -1), i = { ...e, ...t };
    for (const s of o) {
      const r = vn(e[s], t[s]);
      if (!r.valid)
        return {
          valid: !1,
          mergeErrorPath: [s, ...r.mergeErrorPath]
        };
      i[s] = r.data;
    }
    return { valid: !0, data: i };
  }
  if (Array.isArray(e) && Array.isArray(t)) {
    if (e.length !== t.length)
      return { valid: !1, mergeErrorPath: [] };
    const n = [];
    for (let o = 0; o < e.length; o++) {
      const i = e[o], s = t[o], r = vn(i, s);
      if (!r.valid)
        return {
          valid: !1,
          mergeErrorPath: [o, ...r.mergeErrorPath]
        };
      n.push(r.data);
    }
    return { valid: !0, data: n };
  }
  return { valid: !1, mergeErrorPath: [] };
}
function mo(e, t, n) {
  const o = /* @__PURE__ */ new Map();
  let i;
  for (const a of t.issues)
    if (a.code === "unrecognized_keys") {
      i ?? (i = a);
      for (const c of a.keys)
        o.has(c) || o.set(c, {}), o.get(c).l = !0;
    } else
      e.issues.push(a);
  for (const a of n.issues)
    if (a.code === "unrecognized_keys")
      for (const c of a.keys)
        o.has(c) || o.set(c, {}), o.get(c).r = !0;
    else
      e.issues.push(a);
  const s = [...o].filter(([, a]) => a.l && a.r).map(([a]) => a);
  if (s.length && i && e.issues.push({ ...i, keys: s }), it(e))
    return e;
  const r = vn(t.value, n.value);
  if (!r.valid)
    throw new Error(`Unmergable intersection. Error path: ${JSON.stringify(r.mergeErrorPath)}`);
  return e.value = r.data, e;
}
const Bc = /* @__PURE__ */ S("$ZodTuple", (e, t) => {
  K.init(e, t);
  const n = t.items;
  e._zod.parse = (o, i) => {
    const s = o.value;
    if (!Array.isArray(s))
      return o.issues.push({
        input: s,
        inst: e,
        expected: "tuple",
        code: "invalid_type"
      }), o;
    o.value = [];
    const r = [], a = go(n, "optin"), c = go(n, "optout");
    if (!t.rest) {
      if (s.length < a)
        return o.issues.push({
          code: "too_small",
          minimum: a,
          inclusive: !0,
          input: s,
          inst: e,
          origin: "array"
        }), o;
      s.length > n.length && o.issues.push({
        code: "too_big",
        maximum: n.length,
        inclusive: !0,
        input: s,
        inst: e,
        origin: "array"
      });
    }
    const u = new Array(n.length);
    for (let h = 0; h < n.length; h++) {
      const d = n[h]._zod.run({ value: s[h], issues: [] }, i);
      d instanceof Promise ? r.push(d.then((f) => {
        u[h] = f;
      })) : u[h] = d;
    }
    if (t.rest) {
      let h = n.length - 1;
      const d = s.slice(n.length);
      for (const f of d) {
        h++;
        const m = t.rest._zod.run({ value: f, issues: [] }, i);
        m instanceof Promise ? r.push(m.then((l) => yo(l, o, h))) : yo(m, o, h);
      }
    }
    return r.length ? Promise.all(r).then(() => vo(u, o, n, s, c)) : vo(u, o, n, s, c);
  };
});
function go(e, t) {
  for (let n = e.length - 1; n >= 0; n--)
    if (e[n]._zod[t] !== "optional")
      return n + 1;
  return 0;
}
function yo(e, t, n) {
  e.issues.length && t.issues.push(...Ue(n, e.issues)), t.value[n] = e.value;
}
function vo(e, t, n, o, i) {
  for (let s = 0; s < n.length; s++) {
    const r = e[s], a = s < o.length;
    if (r.issues.length) {
      if (!a && s >= i) {
        t.value.length = s;
        break;
      }
      t.issues.push(...Ue(s, r.issues));
    }
    t.value[s] = r.value;
  }
  for (let s = t.value.length - 1; s >= o.length && (n[s]._zod.optout === "optional" && t.value[s] === void 0); s--)
    t.value.length = s;
  return t;
}
const Hc = /* @__PURE__ */ S("$ZodRecord", (e, t) => {
  K.init(e, t), e._zod.parse = (n, o) => {
    const i = n.value;
    if (!ut(i))
      return n.issues.push({
        expected: "record",
        code: "invalid_type",
        input: i,
        inst: e
      }), n;
    const s = [], r = t.keyType._zod.values;
    if (r) {
      n.value = {};
      const a = /* @__PURE__ */ new Set();
      for (const u of r)
        if (typeof u == "string" || typeof u == "number" || typeof u == "symbol") {
          a.add(typeof u == "number" ? u.toString() : u);
          const h = t.keyType._zod.run({ value: u, issues: [] }, o);
          if (h instanceof Promise)
            throw new Error("Async schemas not supported in object keys currently");
          if (h.issues.length) {
            n.issues.push({
              code: "invalid_key",
              origin: "record",
              issues: h.issues.map((m) => De(m, o, Ze())),
              input: u,
              path: [u],
              inst: e
            });
            continue;
          }
          const d = h.value, f = t.valueType._zod.run({ value: i[u], issues: [] }, o);
          f instanceof Promise ? s.push(f.then((m) => {
            m.issues.length && n.issues.push(...Ue(u, m.issues)), n.value[d] = m.value;
          })) : (f.issues.length && n.issues.push(...Ue(u, f.issues)), n.value[d] = f.value);
        }
      let c;
      for (const u in i)
        a.has(u) || (c = c ?? [], c.push(u));
      c && c.length > 0 && n.issues.push({
        code: "unrecognized_keys",
        input: i,
        inst: e,
        keys: c
      });
    } else {
      n.value = {};
      for (const a of Reflect.ownKeys(i)) {
        if (a === "__proto__" || !Object.prototype.propertyIsEnumerable.call(i, a))
          continue;
        let c = t.keyType._zod.run({ value: a, issues: [] }, o);
        if (c instanceof Promise)
          throw new Error("Async schemas not supported in object keys currently");
        if (typeof a == "string" && Ei.test(a) && c.issues.length) {
          const d = t.keyType._zod.run({ value: Number(a), issues: [] }, o);
          if (d instanceof Promise)
            throw new Error("Async schemas not supported in object keys currently");
          d.issues.length === 0 && (c = d);
        }
        if (c.issues.length) {
          t.mode === "loose" ? n.value[a] = i[a] : n.issues.push({
            code: "invalid_key",
            origin: "record",
            issues: c.issues.map((d) => De(d, o, Ze())),
            input: a,
            path: [a],
            inst: e
          });
          continue;
        }
        const h = t.valueType._zod.run({ value: i[a], issues: [] }, o);
        h instanceof Promise ? s.push(h.then((d) => {
          d.issues.length && n.issues.push(...Ue(a, d.issues)), n.value[c.value] = d.value;
        })) : (h.issues.length && n.issues.push(...Ue(a, h.issues)), n.value[c.value] = h.value);
      }
    }
    return s.length ? Promise.all(s).then(() => n) : n;
  };
}), Yc = /* @__PURE__ */ S("$ZodEnum", (e, t) => {
  K.init(e, t);
  const n = bi(t.entries), o = new Set(n);
  e._zod.values = o, e._zod.pattern = new RegExp(`^(${n.filter((i) => Ks.has(typeof i)).map((i) => typeof i == "string" ? ht(i) : i.toString()).join("|")})$`), e._zod.parse = (i, s) => {
    const r = i.value;
    return o.has(r) || i.issues.push({
      code: "invalid_value",
      values: n,
      input: r,
      inst: e
    }), i;
  };
}), Xc = /* @__PURE__ */ S("$ZodLiteral", (e, t) => {
  if (K.init(e, t), t.values.length === 0)
    throw new Error("Cannot create literal schema with no valid values");
  const n = new Set(t.values);
  e._zod.values = n, e._zod.pattern = new RegExp(`^(${t.values.map((o) => typeof o == "string" ? ht(o) : o ? ht(o.toString()) : String(o)).join("|")})$`), e._zod.parse = (o, i) => {
    const s = o.value;
    return n.has(s) || o.issues.push({
      code: "invalid_value",
      values: t.values,
      input: s,
      inst: e
    }), o;
  };
}), Uc = /* @__PURE__ */ S("$ZodTransform", (e, t) => {
  K.init(e, t), e._zod.optin = "optional", e._zod.parse = (n, o) => {
    if (o.direction === "backward")
      throw new xi(e.constructor.name);
    const i = t.transform(n.value, n);
    if (o.async)
      return (i instanceof Promise ? i : Promise.resolve(i)).then((r) => (n.value = r, n.fallback = !0, n));
    if (i instanceof Promise)
      throw new st();
    return n.value = i, n.fallback = !0, n;
  };
});
function xo(e, t) {
  return t === void 0 && (e.issues.length || e.fallback) ? { issues: [], value: void 0 } : e;
}
const Zi = /* @__PURE__ */ S("$ZodOptional", (e, t) => {
  K.init(e, t), e._zod.optin = "optional", e._zod.optout = "optional", G(e._zod, "values", () => t.innerType._zod.values ? /* @__PURE__ */ new Set([...t.innerType._zod.values, void 0]) : void 0), G(e._zod, "pattern", () => {
    const n = t.innerType._zod.pattern;
    return n ? new RegExp(`^(${En(n.source)})?$`) : void 0;
  }), e._zod.parse = (n, o) => {
    if (t.innerType._zod.optin === "optional") {
      const i = n.value, s = t.innerType._zod.run(n, o);
      return s instanceof Promise ? s.then((r) => xo(r, i)) : xo(s, i);
    }
    return n.value === void 0 ? n : t.innerType._zod.run(n, o);
  };
}), jc = /* @__PURE__ */ S("$ZodExactOptional", (e, t) => {
  Zi.init(e, t), G(e._zod, "values", () => t.innerType._zod.values), G(e._zod, "pattern", () => t.innerType._zod.pattern), e._zod.parse = (n, o) => t.innerType._zod.run(n, o);
}), Vc = /* @__PURE__ */ S("$ZodNullable", (e, t) => {
  K.init(e, t), G(e._zod, "optin", () => t.innerType._zod.optin), G(e._zod, "optout", () => t.innerType._zod.optout), G(e._zod, "pattern", () => {
    const n = t.innerType._zod.pattern;
    return n ? new RegExp(`^(${En(n.source)}|null)$`) : void 0;
  }), G(e._zod, "values", () => t.innerType._zod.values ? /* @__PURE__ */ new Set([...t.innerType._zod.values, null]) : void 0), e._zod.parse = (n, o) => n.value === null ? n : t.innerType._zod.run(n, o);
}), Gc = /* @__PURE__ */ S("$ZodDefault", (e, t) => {
  K.init(e, t), e._zod.optin = "optional", G(e._zod, "values", () => t.innerType._zod.values), e._zod.parse = (n, o) => {
    if (o.direction === "backward")
      return t.innerType._zod.run(n, o);
    if (n.value === void 0)
      return n.value = t.defaultValue, n;
    const i = t.innerType._zod.run(n, o);
    return i instanceof Promise ? i.then((s) => bo(s, t)) : bo(i, t);
  };
});
function bo(e, t) {
  return e.value === void 0 && (e.value = t.defaultValue), e;
}
const Jc = /* @__PURE__ */ S("$ZodPrefault", (e, t) => {
  K.init(e, t), e._zod.optin = "optional", G(e._zod, "values", () => t.innerType._zod.values), e._zod.parse = (n, o) => (o.direction === "backward" || n.value === void 0 && (n.value = t.defaultValue), t.innerType._zod.run(n, o));
}), qc = /* @__PURE__ */ S("$ZodNonOptional", (e, t) => {
  K.init(e, t), G(e._zod, "values", () => {
    const n = t.innerType._zod.values;
    return n ? new Set([...n].filter((o) => o !== void 0)) : void 0;
  }), e._zod.parse = (n, o) => {
    const i = t.innerType._zod.run(n, o);
    return i instanceof Promise ? i.then((s) => _o(s, e)) : _o(i, e);
  };
});
function _o(e, t) {
  return !e.issues.length && e.value === void 0 && e.issues.push({
    code: "invalid_type",
    expected: "nonoptional",
    input: e.value,
    inst: t
  }), e;
}
const Kc = /* @__PURE__ */ S("$ZodCatch", (e, t) => {
  K.init(e, t), e._zod.optin = "optional", G(e._zod, "optout", () => t.innerType._zod.optout), G(e._zod, "values", () => t.innerType._zod.values), e._zod.parse = (n, o) => {
    if (o.direction === "backward")
      return t.innerType._zod.run(n, o);
    const i = t.innerType._zod.run(n, o);
    return i instanceof Promise ? i.then((s) => (n.value = s.value, s.issues.length && (n.value = t.catchValue({
      ...n,
      error: {
        issues: s.issues.map((r) => De(r, o, Ze()))
      },
      input: n.value
    }), n.issues = [], n.fallback = !0), n)) : (n.value = i.value, i.issues.length && (n.value = t.catchValue({
      ...n,
      error: {
        issues: i.issues.map((s) => De(s, o, Ze()))
      },
      input: n.value
    }), n.issues = [], n.fallback = !0), n);
  };
}), Qc = /* @__PURE__ */ S("$ZodPipe", (e, t) => {
  K.init(e, t), G(e._zod, "values", () => t.in._zod.values), G(e._zod, "optin", () => t.in._zod.optin), G(e._zod, "optout", () => t.out._zod.optout), G(e._zod, "propValues", () => t.in._zod.propValues), e._zod.parse = (n, o) => {
    if (o.direction === "backward") {
      const s = t.out._zod.run(n, o);
      return s instanceof Promise ? s.then((r) => $t(r, t.in, o)) : $t(s, t.in, o);
    }
    const i = t.in._zod.run(n, o);
    return i instanceof Promise ? i.then((s) => $t(s, t.out, o)) : $t(i, t.out, o);
  };
});
function $t(e, t, n) {
  return e.issues.length ? (e.aborted = !0, e) : t._zod.run({ value: e.value, issues: e.issues, fallback: e.fallback }, n);
}
const eu = /* @__PURE__ */ S("$ZodReadonly", (e, t) => {
  K.init(e, t), G(e._zod, "propValues", () => t.innerType._zod.propValues), G(e._zod, "values", () => t.innerType._zod.values), G(e._zod, "optin", () => t.innerType?._zod?.optin), G(e._zod, "optout", () => t.innerType?._zod?.optout), e._zod.parse = (n, o) => {
    if (o.direction === "backward")
      return t.innerType._zod.run(n, o);
    const i = t.innerType._zod.run(n, o);
    return i instanceof Promise ? i.then(ko) : ko(i);
  };
});
function ko(e) {
  return e.value = Object.freeze(e.value), e;
}
const tu = /* @__PURE__ */ S("$ZodCustom", (e, t) => {
  ye.init(e, t), K.init(e, t), e._zod.parse = (n, o) => n, e._zod.check = (n) => {
    const o = n.value, i = t.fn(o);
    if (i instanceof Promise)
      return i.then((s) => wo(s, n, o, e));
    wo(i, n, o, e);
  };
});
function wo(e, t, n, o) {
  if (!e) {
    const i = {
      code: "custom",
      input: n,
      inst: o,
      // incorporates params.error into issue reporting
      path: [...o._zod.def.path ?? []],
      // incorporates params.error into issue reporting
      continue: !o._zod.def.abort
      // params: inst._zod.def.params,
    };
    o._zod.def.params && (i.params = o._zod.def.params), t.issues.push(Pt(i));
  }
}
var Io;
class nu {
  constructor() {
    this._map = /* @__PURE__ */ new WeakMap(), this._idmap = /* @__PURE__ */ new Map();
  }
  add(t, ...n) {
    const o = n[0];
    return this._map.set(t, o), o && typeof o == "object" && "id" in o && this._idmap.set(o.id, t), this;
  }
  clear() {
    return this._map = /* @__PURE__ */ new WeakMap(), this._idmap = /* @__PURE__ */ new Map(), this;
  }
  remove(t) {
    const n = this._map.get(t);
    return n && typeof n == "object" && "id" in n && this._idmap.delete(n.id), this._map.delete(t), this;
  }
  get(t) {
    const n = t._zod.parent;
    if (n) {
      const o = { ...this.get(n) ?? {} };
      delete o.id;
      const i = { ...o, ...this._map.get(t) };
      return Object.keys(i).length ? i : void 0;
    }
    return this._map.get(t);
  }
  has(t) {
    return this._map.has(t);
  }
}
function ou() {
  return new nu();
}
(Io = globalThis).__zod_globalRegistry ?? (Io.__zod_globalRegistry = ou());
const _t = globalThis.__zod_globalRegistry;
// @__NO_SIDE_EFFECTS__
function iu(e, t) {
  return new e({
    type: "string",
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function ru(e, t) {
  return new e({
    type: "string",
    format: "email",
    check: "string_format",
    abort: !1,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function Po(e, t) {
  return new e({
    type: "string",
    format: "guid",
    check: "string_format",
    abort: !1,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function su(e, t) {
  return new e({
    type: "string",
    format: "uuid",
    check: "string_format",
    abort: !1,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function au(e, t) {
  return new e({
    type: "string",
    format: "uuid",
    check: "string_format",
    abort: !1,
    version: "v4",
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function cu(e, t) {
  return new e({
    type: "string",
    format: "uuid",
    check: "string_format",
    abort: !1,
    version: "v6",
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function uu(e, t) {
  return new e({
    type: "string",
    format: "uuid",
    check: "string_format",
    abort: !1,
    version: "v7",
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function hu(e, t) {
  return new e({
    type: "string",
    format: "url",
    check: "string_format",
    abort: !1,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function du(e, t) {
  return new e({
    type: "string",
    format: "emoji",
    check: "string_format",
    abort: !1,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function lu(e, t) {
  return new e({
    type: "string",
    format: "nanoid",
    check: "string_format",
    abort: !1,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function fu(e, t) {
  return new e({
    type: "string",
    format: "cuid",
    check: "string_format",
    abort: !1,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function pu(e, t) {
  return new e({
    type: "string",
    format: "cuid2",
    check: "string_format",
    abort: !1,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function mu(e, t) {
  return new e({
    type: "string",
    format: "ulid",
    check: "string_format",
    abort: !1,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function gu(e, t) {
  return new e({
    type: "string",
    format: "xid",
    check: "string_format",
    abort: !1,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function yu(e, t) {
  return new e({
    type: "string",
    format: "ksuid",
    check: "string_format",
    abort: !1,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function vu(e, t) {
  return new e({
    type: "string",
    format: "ipv4",
    check: "string_format",
    abort: !1,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function xu(e, t) {
  return new e({
    type: "string",
    format: "ipv6",
    check: "string_format",
    abort: !1,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function bu(e, t) {
  return new e({
    type: "string",
    format: "cidrv4",
    check: "string_format",
    abort: !1,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function _u(e, t) {
  return new e({
    type: "string",
    format: "cidrv6",
    check: "string_format",
    abort: !1,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function ku(e, t) {
  return new e({
    type: "string",
    format: "base64",
    check: "string_format",
    abort: !1,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function wu(e, t) {
  return new e({
    type: "string",
    format: "base64url",
    check: "string_format",
    abort: !1,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function Iu(e, t) {
  return new e({
    type: "string",
    format: "e164",
    check: "string_format",
    abort: !1,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function Pu(e, t) {
  return new e({
    type: "string",
    format: "jwt",
    check: "string_format",
    abort: !1,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function Su(e, t) {
  return new e({
    type: "string",
    format: "datetime",
    check: "string_format",
    offset: !1,
    local: !1,
    precision: null,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function Mu(e, t) {
  return new e({
    type: "string",
    format: "date",
    check: "string_format",
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function Ru(e, t) {
  return new e({
    type: "string",
    format: "time",
    check: "string_format",
    precision: null,
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function Eu(e, t) {
  return new e({
    type: "string",
    format: "duration",
    check: "string_format",
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function zu(e, t) {
  return new e({
    type: "number",
    checks: [],
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function Cu(e, t) {
  return new e({
    type: "number",
    check: "number_format",
    abort: !1,
    format: "safeint",
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function Tu(e, t) {
  return new e({
    type: "boolean",
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function Lu(e) {
  return new e({
    type: "any"
  });
}
// @__NO_SIDE_EFFECTS__
function Ou(e) {
  return new e({
    type: "unknown"
  });
}
// @__NO_SIDE_EFFECTS__
function $u(e, t) {
  return new e({
    type: "never",
    ...Z(t)
  });
}
// @__NO_SIDE_EFFECTS__
function So(e, t) {
  return new Ci({
    check: "less_than",
    ...Z(t),
    value: e,
    inclusive: !1
  });
}
// @__NO_SIDE_EFFECTS__
function on(e, t) {
  return new Ci({
    check: "less_than",
    ...Z(t),
    value: e,
    inclusive: !0
  });
}
// @__NO_SIDE_EFFECTS__
function Mo(e, t) {
  return new Ti({
    check: "greater_than",
    ...Z(t),
    value: e,
    inclusive: !1
  });
}
// @__NO_SIDE_EFFECTS__
function rn(e, t) {
  return new Ti({
    check: "greater_than",
    ...Z(t),
    value: e,
    inclusive: !0
  });
}
// @__NO_SIDE_EFFECTS__
function Ro(e, t) {
  return new ja({
    check: "multiple_of",
    ...Z(t),
    value: e
  });
}
// @__NO_SIDE_EFFECTS__
function Di(e, t) {
  return new Ga({
    check: "max_length",
    ...Z(t),
    maximum: e
  });
}
// @__NO_SIDE_EFFECTS__
function Bt(e, t) {
  return new Ja({
    check: "min_length",
    ...Z(t),
    minimum: e
  });
}
// @__NO_SIDE_EFFECTS__
function Fi(e, t) {
  return new qa({
    check: "length_equals",
    ...Z(t),
    length: e
  });
}
// @__NO_SIDE_EFFECTS__
function Au(e, t) {
  return new Ka({
    check: "string_format",
    format: "regex",
    ...Z(t),
    pattern: e
  });
}
// @__NO_SIDE_EFFECTS__
function Nu(e) {
  return new Qa({
    check: "string_format",
    format: "lowercase",
    ...Z(e)
  });
}
// @__NO_SIDE_EFFECTS__
function Zu(e) {
  return new ec({
    check: "string_format",
    format: "uppercase",
    ...Z(e)
  });
}
// @__NO_SIDE_EFFECTS__
function Du(e, t) {
  return new tc({
    check: "string_format",
    format: "includes",
    ...Z(t),
    includes: e
  });
}
// @__NO_SIDE_EFFECTS__
function Fu(e, t) {
  return new nc({
    check: "string_format",
    format: "starts_with",
    ...Z(t),
    prefix: e
  });
}
// @__NO_SIDE_EFFECTS__
function Wu(e, t) {
  return new oc({
    check: "string_format",
    format: "ends_with",
    ...Z(t),
    suffix: e
  });
}
// @__NO_SIDE_EFFECTS__
function dt(e) {
  return new ic({
    check: "overwrite",
    tx: e
  });
}
// @__NO_SIDE_EFFECTS__
function Bu(e) {
  return /* @__PURE__ */ dt((t) => t.normalize(e));
}
// @__NO_SIDE_EFFECTS__
function Hu() {
  return /* @__PURE__ */ dt((e) => e.trim());
}
// @__NO_SIDE_EFFECTS__
function Yu() {
  return /* @__PURE__ */ dt((e) => e.toLowerCase());
}
// @__NO_SIDE_EFFECTS__
function Xu() {
  return /* @__PURE__ */ dt((e) => e.toUpperCase());
}
// @__NO_SIDE_EFFECTS__
function Uu() {
  return /* @__PURE__ */ dt((e) => Js(e));
}
// @__NO_SIDE_EFFECTS__
function ju(e, t, n) {
  return new e({
    type: "array",
    element: t,
    // get element() {
    //   return element;
    // },
    ...Z(n)
  });
}
// @__NO_SIDE_EFFECTS__
function Vu(e, t, n) {
  return new e({
    type: "custom",
    check: "custom",
    fn: t,
    ...Z(n)
  });
}
// @__NO_SIDE_EFFECTS__
function Gu(e, t) {
  const n = /* @__PURE__ */ Ju((o) => (o.addIssue = (i) => {
    if (typeof i == "string")
      o.issues.push(Pt(i, o.value, n._zod.def));
    else {
      const s = i;
      s.fatal && (s.continue = !1), s.code ?? (s.code = "custom"), s.input ?? (s.input = o.value), s.inst ?? (s.inst = n), s.continue ?? (s.continue = !n._zod.def.abort), o.issues.push(Pt(s));
    }
  }, e(o.value, o)), t);
  return n;
}
// @__NO_SIDE_EFFECTS__
function Ju(e, t) {
  const n = new ye({
    check: "custom",
    ...Z(t)
  });
  return n._zod.check = e, n;
}
function Wi(e) {
  let t = e?.target ?? "draft-2020-12";
  return t === "draft-4" && (t = "draft-04"), t === "draft-7" && (t = "draft-07"), {
    processors: e.processors ?? {},
    metadataRegistry: e?.metadata ?? _t,
    target: t,
    unrepresentable: e?.unrepresentable ?? "throw",
    override: e?.override ?? (() => {
    }),
    io: e?.io ?? "output",
    counter: 0,
    seen: /* @__PURE__ */ new Map(),
    cycles: e?.cycles ?? "ref",
    reused: e?.reused ?? "inline",
    external: e?.external ?? void 0
  };
}
function re(e, t, n = { path: [], schemaPath: [] }) {
  var o;
  const i = e._zod.def, s = t.seen.get(e);
  if (s)
    return s.count++, n.schemaPath.includes(e) && (s.cycle = n.path), s.schema;
  const r = { schema: {}, count: 1, cycle: void 0, path: n.path };
  t.seen.set(e, r);
  const a = e._zod.toJSONSchema?.();
  if (a)
    r.schema = a;
  else {
    const h = {
      ...n,
      schemaPath: [...n.schemaPath, e],
      path: n.path
    };
    if (e._zod.processJSONSchema)
      e._zod.processJSONSchema(t, r.schema, h);
    else {
      const f = r.schema, m = t.processors[i.type];
      if (!m)
        throw new Error(`[toJSONSchema]: Non-representable type encountered: ${i.type}`);
      m(e, t, f, h);
    }
    const d = e._zod.parent;
    d && (r.ref || (r.ref = d), re(d, t, h), t.seen.get(d).isParent = !0);
  }
  const c = t.metadataRegistry.get(e);
  return c && Object.assign(r.schema, c), t.io === "input" && me(e) && (delete r.schema.examples, delete r.schema.default), t.io === "input" && "_prefault" in r.schema && ((o = r.schema).default ?? (o.default = r.schema._prefault)), delete r.schema._prefault, t.seen.get(e).schema;
}
function Bi(e, t) {
  const n = e.seen.get(t);
  if (!n)
    throw new Error("Unprocessed schema. This is a bug in Zod.");
  const o = /* @__PURE__ */ new Map();
  for (const r of e.seen.entries()) {
    const a = e.metadataRegistry.get(r[0])?.id;
    if (a) {
      const c = o.get(a);
      if (c && c !== r[0])
        throw new Error(`Duplicate schema id "${a}" detected during JSON Schema conversion. Two different schemas cannot share the same id when converted together.`);
      o.set(a, r[0]);
    }
  }
  const i = (r) => {
    const a = e.target === "draft-2020-12" ? "$defs" : "definitions";
    if (e.external) {
      const d = e.external.registry.get(r[0])?.id, f = e.external.uri ?? ((l) => l);
      if (d)
        return { ref: f(d) };
      const m = r[1].defId ?? r[1].schema.id ?? `schema${e.counter++}`;
      return r[1].defId = m, { defId: m, ref: `${f("__shared")}#/${a}/${m}` };
    }
    if (r[1] === n)
      return { ref: "#" };
    const u = `#/${a}/`, h = r[1].schema.id ?? `__schema${e.counter++}`;
    return { defId: h, ref: u + h };
  }, s = (r) => {
    if (r[1].schema.$ref)
      return;
    const a = r[1], { ref: c, defId: u } = i(r);
    a.def = { ...a.schema }, u && (a.defId = u);
    const h = a.schema;
    for (const d in h)
      delete h[d];
    h.$ref = c;
  };
  if (e.cycles === "throw")
    for (const r of e.seen.entries()) {
      const a = r[1];
      if (a.cycle)
        throw new Error(`Cycle detected: #/${a.cycle?.join("/")}/<root>

Set the \`cycles\` parameter to \`"ref"\` to resolve cyclical schemas with defs.`);
    }
  for (const r of e.seen.entries()) {
    const a = r[1];
    if (t === r[0]) {
      s(r);
      continue;
    }
    if (e.external) {
      const u = e.external.registry.get(r[0])?.id;
      if (t !== r[0] && u) {
        s(r);
        continue;
      }
    }
    if (e.metadataRegistry.get(r[0])?.id) {
      s(r);
      continue;
    }
    if (a.cycle) {
      s(r);
      continue;
    }
    if (a.count > 1 && e.reused === "ref") {
      s(r);
      continue;
    }
  }
}
function Hi(e, t) {
  const n = e.seen.get(t);
  if (!n)
    throw new Error("Unprocessed schema. This is a bug in Zod.");
  const o = (a) => {
    const c = e.seen.get(a);
    if (c.ref === null)
      return;
    const u = c.def ?? c.schema, h = { ...u }, d = c.ref;
    if (c.ref = null, d) {
      o(d);
      const m = e.seen.get(d), l = m.schema;
      if (l.$ref && (e.target === "draft-07" || e.target === "draft-04" || e.target === "openapi-3.0") ? (u.allOf = u.allOf ?? [], u.allOf.push(l)) : Object.assign(u, l), Object.assign(u, h), a._zod.parent === d)
        for (const x in u)
          x === "$ref" || x === "allOf" || x in h || delete u[x];
      if (l.$ref && m.def)
        for (const x in u)
          x === "$ref" || x === "allOf" || x in m.def && JSON.stringify(u[x]) === JSON.stringify(m.def[x]) && delete u[x];
    }
    const f = a._zod.parent;
    if (f && f !== d) {
      o(f);
      const m = e.seen.get(f);
      if (m?.schema.$ref && (u.$ref = m.schema.$ref, m.def))
        for (const l in u)
          l === "$ref" || l === "allOf" || l in m.def && JSON.stringify(u[l]) === JSON.stringify(m.def[l]) && delete u[l];
    }
    e.override({
      zodSchema: a,
      jsonSchema: u,
      path: c.path ?? []
    });
  };
  for (const a of [...e.seen.entries()].reverse())
    o(a[0]);
  const i = {};
  if (e.target === "draft-2020-12" ? i.$schema = "https://json-schema.org/draft/2020-12/schema" : e.target === "draft-07" ? i.$schema = "http://json-schema.org/draft-07/schema#" : e.target === "draft-04" ? i.$schema = "http://json-schema.org/draft-04/schema#" : e.target, e.external?.uri) {
    const a = e.external.registry.get(t)?.id;
    if (!a)
      throw new Error("Schema is missing an `id` property");
    i.$id = e.external.uri(a);
  }
  Object.assign(i, n.def ?? n.schema);
  const s = e.metadataRegistry.get(t)?.id;
  s !== void 0 && i.id === s && delete i.id;
  const r = e.external?.defs ?? {};
  for (const a of e.seen.entries()) {
    const c = a[1];
    c.def && c.defId && (c.def.id === c.defId && delete c.def.id, r[c.defId] = c.def);
  }
  e.external || Object.keys(r).length > 0 && (e.target === "draft-2020-12" ? i.$defs = r : i.definitions = r);
  try {
    const a = JSON.parse(JSON.stringify(i));
    return Object.defineProperty(a, "~standard", {
      value: {
        ...t["~standard"],
        jsonSchema: {
          input: Ht(t, "input", e.processors),
          output: Ht(t, "output", e.processors)
        }
      },
      enumerable: !1,
      writable: !1
    }), a;
  } catch {
    throw new Error("Error converting schema to JSON.");
  }
}
function me(e, t) {
  const n = t ?? { seen: /* @__PURE__ */ new Set() };
  if (n.seen.has(e))
    return !1;
  n.seen.add(e);
  const o = e._zod.def;
  if (o.type === "transform")
    return !0;
  if (o.type === "array")
    return me(o.element, n);
  if (o.type === "set")
    return me(o.valueType, n);
  if (o.type === "lazy")
    return me(o.getter(), n);
  if (o.type === "promise" || o.type === "optional" || o.type === "nonoptional" || o.type === "nullable" || o.type === "readonly" || o.type === "default" || o.type === "prefault")
    return me(o.innerType, n);
  if (o.type === "intersection")
    return me(o.left, n) || me(o.right, n);
  if (o.type === "record" || o.type === "map")
    return me(o.keyType, n) || me(o.valueType, n);
  if (o.type === "pipe")
    return e._zod.traits.has("$ZodCodec") ? !0 : me(o.in, n) || me(o.out, n);
  if (o.type === "object") {
    for (const i in o.shape)
      if (me(o.shape[i], n))
        return !0;
    return !1;
  }
  if (o.type === "union") {
    for (const i of o.options)
      if (me(i, n))
        return !0;
    return !1;
  }
  if (o.type === "tuple") {
    for (const i of o.items)
      if (me(i, n))
        return !0;
    return !!(o.rest && me(o.rest, n));
  }
  return !1;
}
const qu = (e, t = {}) => (n) => {
  const o = Wi({ ...n, processors: t });
  return re(e, o), Bi(o, e), Hi(o, e);
}, Ht = (e, t, n = {}) => (o) => {
  const { libraryOptions: i, target: s } = o ?? {}, r = Wi({ ...i ?? {}, target: s, io: t, processors: n });
  return re(e, r), Bi(r, e), Hi(r, e);
}, Ku = {
  guid: "uuid",
  url: "uri",
  datetime: "date-time",
  json_string: "json-string",
  regex: ""
  // do not set
}, Qu = (e, t, n, o) => {
  const i = n;
  i.type = "string";
  const { minimum: s, maximum: r, format: a, patterns: c, contentEncoding: u } = e._zod.bag;
  if (typeof s == "number" && (i.minLength = s), typeof r == "number" && (i.maxLength = r), a && (i.format = Ku[a] ?? a, i.format === "" && delete i.format, a === "time" && delete i.format), u && (i.contentEncoding = u), c && c.size > 0) {
    const h = [...c];
    h.length === 1 ? i.pattern = h[0].source : h.length > 1 && (i.allOf = [
      ...h.map((d) => ({
        ...t.target === "draft-07" || t.target === "draft-04" || t.target === "openapi-3.0" ? { type: "string" } : {},
        pattern: d.source
      }))
    ]);
  }
}, eh = (e, t, n, o) => {
  const i = n, { minimum: s, maximum: r, format: a, multipleOf: c, exclusiveMaximum: u, exclusiveMinimum: h } = e._zod.bag;
  typeof a == "string" && a.includes("int") ? i.type = "integer" : i.type = "number";
  const d = typeof h == "number" && h >= (s ?? Number.NEGATIVE_INFINITY), f = typeof u == "number" && u <= (r ?? Number.POSITIVE_INFINITY), m = t.target === "draft-04" || t.target === "openapi-3.0";
  d ? m ? (i.minimum = h, i.exclusiveMinimum = !0) : i.exclusiveMinimum = h : typeof s == "number" && (i.minimum = s), f ? m ? (i.maximum = u, i.exclusiveMaximum = !0) : i.exclusiveMaximum = u : typeof r == "number" && (i.maximum = r), typeof c == "number" && (i.multipleOf = c);
}, th = (e, t, n, o) => {
  n.type = "boolean";
}, nh = (e, t, n, o) => {
  n.not = {};
}, oh = (e, t, n, o) => {
}, ih = (e, t, n, o) => {
}, rh = (e, t, n, o) => {
  const i = e._zod.def, s = bi(i.entries);
  s.every((r) => typeof r == "number") && (n.type = "number"), s.every((r) => typeof r == "string") && (n.type = "string"), n.enum = s;
}, sh = (e, t, n, o) => {
  const i = e._zod.def, s = [];
  for (const r of i.values)
    if (r === void 0) {
      if (t.unrepresentable === "throw")
        throw new Error("Literal `undefined` cannot be represented in JSON Schema");
    } else if (typeof r == "bigint") {
      if (t.unrepresentable === "throw")
        throw new Error("BigInt literals cannot be represented in JSON Schema");
      s.push(Number(r));
    } else
      s.push(r);
  if (s.length !== 0) if (s.length === 1) {
    const r = s[0];
    n.type = r === null ? "null" : typeof r, t.target === "draft-04" || t.target === "openapi-3.0" ? n.enum = [r] : n.const = r;
  } else
    s.every((r) => typeof r == "number") && (n.type = "number"), s.every((r) => typeof r == "string") && (n.type = "string"), s.every((r) => typeof r == "boolean") && (n.type = "boolean"), s.every((r) => r === null) && (n.type = "null"), n.enum = s;
}, ah = (e, t, n, o) => {
  if (t.unrepresentable === "throw")
    throw new Error("Custom types cannot be represented in JSON Schema");
}, ch = (e, t, n, o) => {
  if (t.unrepresentable === "throw")
    throw new Error("Transforms cannot be represented in JSON Schema");
}, uh = (e, t, n, o) => {
  const i = n, s = e._zod.def, { minimum: r, maximum: a } = e._zod.bag;
  typeof r == "number" && (i.minItems = r), typeof a == "number" && (i.maxItems = a), i.type = "array", i.items = re(s.element, t, {
    ...o,
    path: [...o.path, "items"]
  });
}, hh = (e, t, n, o) => {
  const i = n, s = e._zod.def;
  i.type = "object", i.properties = {};
  const r = s.shape;
  for (const u in r)
    i.properties[u] = re(r[u], t, {
      ...o,
      path: [...o.path, "properties", u]
    });
  const a = new Set(Object.keys(r)), c = new Set([...a].filter((u) => {
    const h = s.shape[u]._zod;
    return t.io === "input" ? h.optin === void 0 : h.optout === void 0;
  }));
  c.size > 0 && (i.required = Array.from(c)), s.catchall?._zod.def.type === "never" ? i.additionalProperties = !1 : s.catchall ? s.catchall && (i.additionalProperties = re(s.catchall, t, {
    ...o,
    path: [...o.path, "additionalProperties"]
  })) : t.io === "output" && (i.additionalProperties = !1);
}, dh = (e, t, n, o) => {
  const i = e._zod.def, s = i.inclusive === !1, r = i.options.map((a, c) => re(a, t, {
    ...o,
    path: [...o.path, s ? "oneOf" : "anyOf", c]
  }));
  s ? n.oneOf = r : n.anyOf = r;
}, lh = (e, t, n, o) => {
  const i = e._zod.def, s = re(i.left, t, {
    ...o,
    path: [...o.path, "allOf", 0]
  }), r = re(i.right, t, {
    ...o,
    path: [...o.path, "allOf", 1]
  }), a = (u) => "allOf" in u && Object.keys(u).length === 1, c = [
    ...a(s) ? s.allOf : [s],
    ...a(r) ? r.allOf : [r]
  ];
  n.allOf = c;
}, fh = (e, t, n, o) => {
  const i = n, s = e._zod.def;
  i.type = "array";
  const r = t.target === "draft-2020-12" ? "prefixItems" : "items", a = t.target === "draft-2020-12" || t.target === "openapi-3.0" ? "items" : "additionalItems", c = s.items.map((f, m) => re(f, t, {
    ...o,
    path: [...o.path, r, m]
  })), u = s.rest ? re(s.rest, t, {
    ...o,
    path: [...o.path, a, ...t.target === "openapi-3.0" ? [s.items.length] : []]
  }) : null;
  t.target === "draft-2020-12" ? (i.prefixItems = c, u && (i.items = u)) : t.target === "openapi-3.0" ? (i.items = {
    anyOf: c
  }, u && i.items.anyOf.push(u), i.minItems = c.length, u || (i.maxItems = c.length)) : (i.items = c, u && (i.additionalItems = u));
  const { minimum: h, maximum: d } = e._zod.bag;
  typeof h == "number" && (i.minItems = h), typeof d == "number" && (i.maxItems = d);
}, ph = (e, t, n, o) => {
  const i = n, s = e._zod.def;
  i.type = "object";
  const r = s.keyType, c = r._zod.bag?.patterns;
  if (s.mode === "loose" && c && c.size > 0) {
    const h = re(s.valueType, t, {
      ...o,
      path: [...o.path, "patternProperties", "*"]
    });
    i.patternProperties = {};
    for (const d of c)
      i.patternProperties[d.source] = h;
  } else
    (t.target === "draft-07" || t.target === "draft-2020-12") && (i.propertyNames = re(s.keyType, t, {
      ...o,
      path: [...o.path, "propertyNames"]
    })), i.additionalProperties = re(s.valueType, t, {
      ...o,
      path: [...o.path, "additionalProperties"]
    });
  const u = r._zod.values;
  if (u) {
    const h = [...u].filter((d) => typeof d == "string" || typeof d == "number");
    h.length > 0 && (i.required = h);
  }
}, mh = (e, t, n, o) => {
  const i = e._zod.def, s = re(i.innerType, t, o), r = t.seen.get(e);
  t.target === "openapi-3.0" ? (r.ref = i.innerType, n.nullable = !0) : n.anyOf = [s, { type: "null" }];
}, gh = (e, t, n, o) => {
  const i = e._zod.def;
  re(i.innerType, t, o);
  const s = t.seen.get(e);
  s.ref = i.innerType;
}, yh = (e, t, n, o) => {
  const i = e._zod.def;
  re(i.innerType, t, o);
  const s = t.seen.get(e);
  s.ref = i.innerType, n.default = JSON.parse(JSON.stringify(i.defaultValue));
}, vh = (e, t, n, o) => {
  const i = e._zod.def;
  re(i.innerType, t, o);
  const s = t.seen.get(e);
  s.ref = i.innerType, t.io === "input" && (n._prefault = JSON.parse(JSON.stringify(i.defaultValue)));
}, xh = (e, t, n, o) => {
  const i = e._zod.def;
  re(i.innerType, t, o);
  const s = t.seen.get(e);
  s.ref = i.innerType;
  let r;
  try {
    r = i.catchValue(void 0);
  } catch {
    throw new Error("Dynamic catch values are not supported in JSON Schema");
  }
  n.default = r;
}, bh = (e, t, n, o) => {
  const i = e._zod.def, s = i.in._zod.traits.has("$ZodTransform"), r = t.io === "input" ? s ? i.out : i.in : i.out;
  re(r, t, o);
  const a = t.seen.get(e);
  a.ref = r;
}, _h = (e, t, n, o) => {
  const i = e._zod.def;
  re(i.innerType, t, o);
  const s = t.seen.get(e);
  s.ref = i.innerType, n.readOnly = !0;
}, Yi = (e, t, n, o) => {
  const i = e._zod.def;
  re(i.innerType, t, o);
  const s = t.seen.get(e);
  s.ref = i.innerType;
}, kh = /* @__PURE__ */ S("ZodISODateTime", (e, t) => {
  vc.init(e, t), oe.init(e, t);
});
function wh(e) {
  return /* @__PURE__ */ Su(kh, e);
}
const Ih = /* @__PURE__ */ S("ZodISODate", (e, t) => {
  xc.init(e, t), oe.init(e, t);
});
function Ph(e) {
  return /* @__PURE__ */ Mu(Ih, e);
}
const Sh = /* @__PURE__ */ S("ZodISOTime", (e, t) => {
  bc.init(e, t), oe.init(e, t);
});
function Mh(e) {
  return /* @__PURE__ */ Ru(Sh, e);
}
const Rh = /* @__PURE__ */ S("ZodISODuration", (e, t) => {
  _c.init(e, t), oe.init(e, t);
});
function Eh(e) {
  return /* @__PURE__ */ Eu(Rh, e);
}
const zh = (e, t) => {
  Ii.init(e, t), e.name = "ZodError", Object.defineProperties(e, {
    format: {
      value: (n) => ha(e, n)
      // enumerable: false,
    },
    flatten: {
      value: (n) => ua(e, n)
      // enumerable: false,
    },
    addIssue: {
      value: (n) => {
        e.issues.push(n), e.message = JSON.stringify(e.issues, yn, 2);
      }
      // enumerable: false,
    },
    addIssues: {
      value: (n) => {
        e.issues.push(...n), e.message = JSON.stringify(e.issues, yn, 2);
      }
      // enumerable: false,
    },
    isEmpty: {
      get() {
        return e.issues.length === 0;
      }
      // enumerable: false,
    }
  });
}, xe = /* @__PURE__ */ S("ZodError", zh, {
  Parent: Error
}), Ch = /* @__PURE__ */ Cn(xe), Th = /* @__PURE__ */ Tn(xe), Lh = /* @__PURE__ */ Ut(xe), Oh = /* @__PURE__ */ jt(xe), $h = /* @__PURE__ */ fa(xe), Ah = /* @__PURE__ */ pa(xe), Nh = /* @__PURE__ */ ma(xe), Zh = /* @__PURE__ */ ga(xe), Dh = /* @__PURE__ */ ya(xe), Fh = /* @__PURE__ */ va(xe), Wh = /* @__PURE__ */ xa(xe), Bh = /* @__PURE__ */ ba(xe), Eo = /* @__PURE__ */ new WeakMap();
function Rt(e, t, n) {
  const o = Object.getPrototypeOf(e);
  let i = Eo.get(o);
  if (i || (i = /* @__PURE__ */ new Set(), Eo.set(o, i)), !i.has(t)) {
    i.add(t);
    for (const s in n) {
      const r = n[s];
      Object.defineProperty(o, s, {
        configurable: !0,
        enumerable: !1,
        get() {
          const a = r.bind(this);
          return Object.defineProperty(this, s, {
            configurable: !0,
            writable: !0,
            enumerable: !0,
            value: a
          }), a;
        },
        set(a) {
          Object.defineProperty(this, s, {
            configurable: !0,
            writable: !0,
            enumerable: !0,
            value: a
          });
        }
      });
    }
  }
}
const Q = /* @__PURE__ */ S("ZodType", (e, t) => (K.init(e, t), Object.assign(e["~standard"], {
  jsonSchema: {
    input: Ht(e, "input"),
    output: Ht(e, "output")
  }
}), e.toJSONSchema = qu(e, {}), e.def = t, e.type = t.type, Object.defineProperty(e, "_def", { value: t }), e.parse = (n, o) => Ch(e, n, o, { callee: e.parse }), e.safeParse = (n, o) => Lh(e, n, o), e.parseAsync = async (n, o) => Th(e, n, o, { callee: e.parseAsync }), e.safeParseAsync = async (n, o) => Oh(e, n, o), e.spa = e.safeParseAsync, e.encode = (n, o) => $h(e, n, o), e.decode = (n, o) => Ah(e, n, o), e.encodeAsync = async (n, o) => Nh(e, n, o), e.decodeAsync = async (n, o) => Zh(e, n, o), e.safeEncode = (n, o) => Dh(e, n, o), e.safeDecode = (n, o) => Fh(e, n, o), e.safeEncodeAsync = async (n, o) => Wh(e, n, o), e.safeDecodeAsync = async (n, o) => Bh(e, n, o), Rt(e, "ZodType", {
  check(...n) {
    const o = this.def;
    return this.clone(Ve(o, {
      checks: [
        ...o.checks ?? [],
        ...n.map((i) => typeof i == "function" ? { _zod: { check: i, def: { check: "custom" }, onattach: [] } } : i)
      ]
    }), { parent: !0 });
  },
  with(...n) {
    return this.check(...n);
  },
  clone(n, o) {
    return We(this, n, o);
  },
  brand() {
    return this;
  },
  register(n, o) {
    return n.add(this, o), this;
  },
  refine(n, o) {
    return this.check(Ad(n, o));
  },
  superRefine(n, o) {
    return this.check(Nd(n, o));
  },
  overwrite(n) {
    return this.check(/* @__PURE__ */ dt(n));
  },
  optional() {
    return Oo(this);
  },
  exactOptional() {
    return wd(this);
  },
  nullable() {
    return $o(this);
  },
  nullish() {
    return Oo($o(this));
  },
  nonoptional(n) {
    return Ed(this, n);
  },
  array() {
    return O(this);
  },
  or(n) {
    return lt([this, n]);
  },
  and(n) {
    return yd(this, n);
  },
  transform(n) {
    return Ao(this, _d(n));
  },
  default(n) {
    return Sd(this, n);
  },
  prefault(n) {
    return Rd(this, n);
  },
  catch(n) {
    return Cd(this, n);
  },
  pipe(n) {
    return Ao(this, n);
  },
  readonly() {
    return Od(this);
  },
  describe(n) {
    const o = this.clone();
    return _t.add(o, { description: n }), o;
  },
  meta(...n) {
    if (n.length === 0)
      return _t.get(this);
    const o = this.clone();
    return _t.add(o, n[0]), o;
  },
  isOptional() {
    return this.safeParse(void 0).success;
  },
  isNullable() {
    return this.safeParse(null).success;
  },
  apply(n) {
    return n(this);
  }
}), Object.defineProperty(e, "description", {
  get() {
    return _t.get(e)?.description;
  },
  configurable: !0
}), e)), Xi = /* @__PURE__ */ S("_ZodString", (e, t) => {
  Ln.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (o, i, s) => Qu(e, o, i);
  const n = e._zod.bag;
  e.format = n.format ?? null, e.minLength = n.minimum ?? null, e.maxLength = n.maximum ?? null, Rt(e, "_ZodString", {
    regex(...o) {
      return this.check(/* @__PURE__ */ Au(...o));
    },
    includes(...o) {
      return this.check(/* @__PURE__ */ Du(...o));
    },
    startsWith(...o) {
      return this.check(/* @__PURE__ */ Fu(...o));
    },
    endsWith(...o) {
      return this.check(/* @__PURE__ */ Wu(...o));
    },
    min(...o) {
      return this.check(/* @__PURE__ */ Bt(...o));
    },
    max(...o) {
      return this.check(/* @__PURE__ */ Di(...o));
    },
    length(...o) {
      return this.check(/* @__PURE__ */ Fi(...o));
    },
    nonempty(...o) {
      return this.check(/* @__PURE__ */ Bt(1, ...o));
    },
    lowercase(o) {
      return this.check(/* @__PURE__ */ Nu(o));
    },
    uppercase(o) {
      return this.check(/* @__PURE__ */ Zu(o));
    },
    trim() {
      return this.check(/* @__PURE__ */ Hu());
    },
    normalize(...o) {
      return this.check(/* @__PURE__ */ Bu(...o));
    },
    toLowerCase() {
      return this.check(/* @__PURE__ */ Yu());
    },
    toUpperCase() {
      return this.check(/* @__PURE__ */ Xu());
    },
    slugify() {
      return this.check(/* @__PURE__ */ Uu());
    }
  });
}), Hh = /* @__PURE__ */ S("ZodString", (e, t) => {
  Ln.init(e, t), Xi.init(e, t), e.email = (n) => e.check(/* @__PURE__ */ ru(Yh, n)), e.url = (n) => e.check(/* @__PURE__ */ hu(Xh, n)), e.jwt = (n) => e.check(/* @__PURE__ */ Pu(sd, n)), e.emoji = (n) => e.check(/* @__PURE__ */ du(Uh, n)), e.guid = (n) => e.check(/* @__PURE__ */ Po(zo, n)), e.uuid = (n) => e.check(/* @__PURE__ */ su(At, n)), e.uuidv4 = (n) => e.check(/* @__PURE__ */ au(At, n)), e.uuidv6 = (n) => e.check(/* @__PURE__ */ cu(At, n)), e.uuidv7 = (n) => e.check(/* @__PURE__ */ uu(At, n)), e.nanoid = (n) => e.check(/* @__PURE__ */ lu(jh, n)), e.guid = (n) => e.check(/* @__PURE__ */ Po(zo, n)), e.cuid = (n) => e.check(/* @__PURE__ */ fu(Vh, n)), e.cuid2 = (n) => e.check(/* @__PURE__ */ pu(Gh, n)), e.ulid = (n) => e.check(/* @__PURE__ */ mu(Jh, n)), e.base64 = (n) => e.check(/* @__PURE__ */ ku(od, n)), e.base64url = (n) => e.check(/* @__PURE__ */ wu(id, n)), e.xid = (n) => e.check(/* @__PURE__ */ gu(qh, n)), e.ksuid = (n) => e.check(/* @__PURE__ */ yu(Kh, n)), e.ipv4 = (n) => e.check(/* @__PURE__ */ vu(Qh, n)), e.ipv6 = (n) => e.check(/* @__PURE__ */ xu(ed, n)), e.cidrv4 = (n) => e.check(/* @__PURE__ */ bu(td, n)), e.cidrv6 = (n) => e.check(/* @__PURE__ */ _u(nd, n)), e.e164 = (n) => e.check(/* @__PURE__ */ Iu(rd, n)), e.datetime = (n) => e.check(wh(n)), e.date = (n) => e.check(Ph(n)), e.time = (n) => e.check(Mh(n)), e.duration = (n) => e.check(Eh(n));
});
function I(e) {
  return /* @__PURE__ */ iu(Hh, e);
}
const oe = /* @__PURE__ */ S("ZodStringFormat", (e, t) => {
  ee.init(e, t), Xi.init(e, t);
}), Yh = /* @__PURE__ */ S("ZodEmail", (e, t) => {
  uc.init(e, t), oe.init(e, t);
}), zo = /* @__PURE__ */ S("ZodGUID", (e, t) => {
  ac.init(e, t), oe.init(e, t);
}), At = /* @__PURE__ */ S("ZodUUID", (e, t) => {
  cc.init(e, t), oe.init(e, t);
}), Xh = /* @__PURE__ */ S("ZodURL", (e, t) => {
  hc.init(e, t), oe.init(e, t);
}), Uh = /* @__PURE__ */ S("ZodEmoji", (e, t) => {
  dc.init(e, t), oe.init(e, t);
}), jh = /* @__PURE__ */ S("ZodNanoID", (e, t) => {
  lc.init(e, t), oe.init(e, t);
}), Vh = /* @__PURE__ */ S("ZodCUID", (e, t) => {
  fc.init(e, t), oe.init(e, t);
}), Gh = /* @__PURE__ */ S("ZodCUID2", (e, t) => {
  pc.init(e, t), oe.init(e, t);
}), Jh = /* @__PURE__ */ S("ZodULID", (e, t) => {
  mc.init(e, t), oe.init(e, t);
}), qh = /* @__PURE__ */ S("ZodXID", (e, t) => {
  gc.init(e, t), oe.init(e, t);
}), Kh = /* @__PURE__ */ S("ZodKSUID", (e, t) => {
  yc.init(e, t), oe.init(e, t);
}), Qh = /* @__PURE__ */ S("ZodIPv4", (e, t) => {
  kc.init(e, t), oe.init(e, t);
}), ed = /* @__PURE__ */ S("ZodIPv6", (e, t) => {
  wc.init(e, t), oe.init(e, t);
}), td = /* @__PURE__ */ S("ZodCIDRv4", (e, t) => {
  Ic.init(e, t), oe.init(e, t);
}), nd = /* @__PURE__ */ S("ZodCIDRv6", (e, t) => {
  Pc.init(e, t), oe.init(e, t);
}), od = /* @__PURE__ */ S("ZodBase64", (e, t) => {
  Sc.init(e, t), oe.init(e, t);
}), id = /* @__PURE__ */ S("ZodBase64URL", (e, t) => {
  Rc.init(e, t), oe.init(e, t);
}), rd = /* @__PURE__ */ S("ZodE164", (e, t) => {
  Ec.init(e, t), oe.init(e, t);
}), sd = /* @__PURE__ */ S("ZodJWT", (e, t) => {
  Cc.init(e, t), oe.init(e, t);
}), Ui = /* @__PURE__ */ S("ZodNumber", (e, t) => {
  Oi.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (o, i, s) => eh(e, o, i), Rt(e, "ZodNumber", {
    gt(o, i) {
      return this.check(/* @__PURE__ */ Mo(o, i));
    },
    gte(o, i) {
      return this.check(/* @__PURE__ */ rn(o, i));
    },
    min(o, i) {
      return this.check(/* @__PURE__ */ rn(o, i));
    },
    lt(o, i) {
      return this.check(/* @__PURE__ */ So(o, i));
    },
    lte(o, i) {
      return this.check(/* @__PURE__ */ on(o, i));
    },
    max(o, i) {
      return this.check(/* @__PURE__ */ on(o, i));
    },
    int(o) {
      return this.check(Co(o));
    },
    safe(o) {
      return this.check(Co(o));
    },
    positive(o) {
      return this.check(/* @__PURE__ */ Mo(0, o));
    },
    nonnegative(o) {
      return this.check(/* @__PURE__ */ rn(0, o));
    },
    negative(o) {
      return this.check(/* @__PURE__ */ So(0, o));
    },
    nonpositive(o) {
      return this.check(/* @__PURE__ */ on(0, o));
    },
    multipleOf(o, i) {
      return this.check(/* @__PURE__ */ Ro(o, i));
    },
    step(o, i) {
      return this.check(/* @__PURE__ */ Ro(o, i));
    },
    finite() {
      return this;
    }
  });
  const n = e._zod.bag;
  e.minValue = Math.max(n.minimum ?? Number.NEGATIVE_INFINITY, n.exclusiveMinimum ?? Number.NEGATIVE_INFINITY) ?? null, e.maxValue = Math.min(n.maximum ?? Number.POSITIVE_INFINITY, n.exclusiveMaximum ?? Number.POSITIVE_INFINITY) ?? null, e.isInt = (n.format ?? "").includes("int") || Number.isSafeInteger(n.multipleOf ?? 0.5), e.isFinite = !0, e.format = n.format ?? null;
});
function g(e) {
  return /* @__PURE__ */ zu(Ui, e);
}
const ad = /* @__PURE__ */ S("ZodNumberFormat", (e, t) => {
  Tc.init(e, t), Ui.init(e, t);
});
function Co(e) {
  return /* @__PURE__ */ Cu(ad, e);
}
const cd = /* @__PURE__ */ S("ZodBoolean", (e, t) => {
  Lc.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => th(e, n, o);
});
function J(e) {
  return /* @__PURE__ */ Tu(cd, e);
}
const ud = /* @__PURE__ */ S("ZodAny", (e, t) => {
  Oc.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => oh();
});
function To() {
  return /* @__PURE__ */ Lu(ud);
}
const hd = /* @__PURE__ */ S("ZodUnknown", (e, t) => {
  $c.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => ih();
});
function Lo() {
  return /* @__PURE__ */ Ou(hd);
}
const dd = /* @__PURE__ */ S("ZodNever", (e, t) => {
  Ac.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => nh(e, n, o);
});
function ld(e) {
  return /* @__PURE__ */ $u(dd, e);
}
const fd = /* @__PURE__ */ S("ZodArray", (e, t) => {
  Nc.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => uh(e, n, o, i), e.element = t.element, Rt(e, "ZodArray", {
    min(n, o) {
      return this.check(/* @__PURE__ */ Bt(n, o));
    },
    nonempty(n) {
      return this.check(/* @__PURE__ */ Bt(1, n));
    },
    max(n, o) {
      return this.check(/* @__PURE__ */ Di(n, o));
    },
    length(n, o) {
      return this.check(/* @__PURE__ */ Fi(n, o));
    },
    unwrap() {
      return this.element;
    }
  });
});
function O(e, t) {
  return /* @__PURE__ */ ju(fd, e, t);
}
const pd = /* @__PURE__ */ S("ZodObject", (e, t) => {
  Dc.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => hh(e, n, o, i), G(e, "shape", () => t.shape), Rt(e, "ZodObject", {
    keyof() {
      return H(Object.keys(this._zod.def.shape));
    },
    catchall(n) {
      return this.clone({ ...this._zod.def, catchall: n });
    },
    passthrough() {
      return this.clone({ ...this._zod.def, catchall: Lo() });
    },
    loose() {
      return this.clone({ ...this._zod.def, catchall: Lo() });
    },
    strict() {
      return this.clone({ ...this._zod.def, catchall: ld() });
    },
    strip() {
      return this.clone({ ...this._zod.def, catchall: void 0 });
    },
    extend(n) {
      return oa(this, n);
    },
    safeExtend(n) {
      return ia(this, n);
    },
    merge(n) {
      return ra(this, n);
    },
    pick(n) {
      return ta(this, n);
    },
    omit(n) {
      return na(this, n);
    },
    partial(...n) {
      return sa(Vi, this, n[0]);
    },
    required(...n) {
      return aa(Gi, this, n[0]);
    }
  });
});
function R(e, t) {
  const n = {
    type: "object",
    shape: e ?? {},
    ...Z(t)
  };
  return new pd(n);
}
const ji = /* @__PURE__ */ S("ZodUnion", (e, t) => {
  Ni.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => dh(e, n, o, i), e.options = t.options;
});
function lt(e, t) {
  return new ji({
    type: "union",
    options: e,
    ...Z(t)
  });
}
const md = /* @__PURE__ */ S("ZodDiscriminatedUnion", (e, t) => {
  ji.init(e, t), Fc.init(e, t);
});
function Gt(e, t, n) {
  return new md({
    type: "union",
    options: t,
    discriminator: e,
    ...Z(n)
  });
}
const gd = /* @__PURE__ */ S("ZodIntersection", (e, t) => {
  Wc.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => lh(e, n, o, i);
});
function yd(e, t) {
  return new gd({
    type: "intersection",
    left: e,
    right: t
  });
}
const vd = /* @__PURE__ */ S("ZodTuple", (e, t) => {
  Bc.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => fh(e, n, o, i), e.rest = (n) => e.clone({
    ...e._zod.def,
    rest: n
  });
});
function Fe(e, t, n) {
  const o = t instanceof K, i = o ? n : t, s = o ? t : null;
  return new vd({
    type: "tuple",
    items: e,
    rest: s,
    ...Z(i)
  });
}
const xn = /* @__PURE__ */ S("ZodRecord", (e, t) => {
  Hc.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => ph(e, n, o, i), e.keyType = t.keyType, e.valueType = t.valueType;
});
function Se(e, t, n) {
  return !t || !t._zod ? new xn({
    type: "record",
    keyType: I(),
    valueType: e,
    ...Z(t)
  }) : new xn({
    type: "record",
    keyType: e,
    valueType: t,
    ...Z(n)
  });
}
function bn(e, t, n) {
  const o = We(e);
  return o._zod.values = void 0, new xn({
    type: "record",
    keyType: o,
    valueType: t,
    ...Z(n)
  });
}
const _n = /* @__PURE__ */ S("ZodEnum", (e, t) => {
  Yc.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (o, i, s) => rh(e, o, i), e.enum = t.entries, e.options = Object.values(t.entries);
  const n = new Set(Object.keys(t.entries));
  e.extract = (o, i) => {
    const s = {};
    for (const r of o)
      if (n.has(r))
        s[r] = t.entries[r];
      else
        throw new Error(`Key ${r} not found in enum`);
    return new _n({
      ...t,
      checks: [],
      ...Z(i),
      entries: s
    });
  }, e.exclude = (o, i) => {
    const s = { ...t.entries };
    for (const r of o)
      if (n.has(r))
        delete s[r];
      else
        throw new Error(`Key ${r} not found in enum`);
    return new _n({
      ...t,
      checks: [],
      ...Z(i),
      entries: s
    });
  };
});
function H(e, t) {
  const n = Array.isArray(e) ? Object.fromEntries(e.map((o) => [o, o])) : e;
  return new _n({
    type: "enum",
    entries: n,
    ...Z(t)
  });
}
const xd = /* @__PURE__ */ S("ZodLiteral", (e, t) => {
  Xc.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => sh(e, n, o), e.values = new Set(t.values), Object.defineProperty(e, "value", {
    get() {
      if (t.values.length > 1)
        throw new Error("This schema contains multiple valid literal values. Use `.values` instead.");
      return t.values[0];
    }
  });
});
function D(e, t) {
  return new xd({
    type: "literal",
    values: Array.isArray(e) ? e : [e],
    ...Z(t)
  });
}
const bd = /* @__PURE__ */ S("ZodTransform", (e, t) => {
  Uc.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => ch(e, n), e._zod.parse = (n, o) => {
    if (o.direction === "backward")
      throw new xi(e.constructor.name);
    n.addIssue = (s) => {
      if (typeof s == "string")
        n.issues.push(Pt(s, n.value, t));
      else {
        const r = s;
        r.fatal && (r.continue = !1), r.code ?? (r.code = "custom"), r.input ?? (r.input = n.value), r.inst ?? (r.inst = e), n.issues.push(Pt(r));
      }
    };
    const i = t.transform(n.value, n);
    return i instanceof Promise ? i.then((s) => (n.value = s, n.fallback = !0, n)) : (n.value = i, n.fallback = !0, n);
  };
});
function _d(e) {
  return new bd({
    type: "transform",
    transform: e
  });
}
const Vi = /* @__PURE__ */ S("ZodOptional", (e, t) => {
  Zi.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => Yi(e, n, o, i), e.unwrap = () => e._zod.def.innerType;
});
function Oo(e) {
  return new Vi({
    type: "optional",
    innerType: e
  });
}
const kd = /* @__PURE__ */ S("ZodExactOptional", (e, t) => {
  jc.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => Yi(e, n, o, i), e.unwrap = () => e._zod.def.innerType;
});
function wd(e) {
  return new kd({
    type: "optional",
    innerType: e
  });
}
const Id = /* @__PURE__ */ S("ZodNullable", (e, t) => {
  Vc.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => mh(e, n, o, i), e.unwrap = () => e._zod.def.innerType;
});
function $o(e) {
  return new Id({
    type: "nullable",
    innerType: e
  });
}
const Pd = /* @__PURE__ */ S("ZodDefault", (e, t) => {
  Gc.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => yh(e, n, o, i), e.unwrap = () => e._zod.def.innerType, e.removeDefault = e.unwrap;
});
function Sd(e, t) {
  return new Pd({
    type: "default",
    innerType: e,
    get defaultValue() {
      return typeof t == "function" ? t() : ki(t);
    }
  });
}
const Md = /* @__PURE__ */ S("ZodPrefault", (e, t) => {
  Jc.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => vh(e, n, o, i), e.unwrap = () => e._zod.def.innerType;
});
function Rd(e, t) {
  return new Md({
    type: "prefault",
    innerType: e,
    get defaultValue() {
      return typeof t == "function" ? t() : ki(t);
    }
  });
}
const Gi = /* @__PURE__ */ S("ZodNonOptional", (e, t) => {
  qc.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => gh(e, n, o, i), e.unwrap = () => e._zod.def.innerType;
});
function Ed(e, t) {
  return new Gi({
    type: "nonoptional",
    innerType: e,
    ...Z(t)
  });
}
const zd = /* @__PURE__ */ S("ZodCatch", (e, t) => {
  Kc.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => xh(e, n, o, i), e.unwrap = () => e._zod.def.innerType, e.removeCatch = e.unwrap;
});
function Cd(e, t) {
  return new zd({
    type: "catch",
    innerType: e,
    catchValue: typeof t == "function" ? t : () => t
  });
}
const Td = /* @__PURE__ */ S("ZodPipe", (e, t) => {
  Qc.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => bh(e, n, o, i), e.in = t.in, e.out = t.out;
});
function Ao(e, t) {
  return new Td({
    type: "pipe",
    in: e,
    out: t
    // ...util.normalizeParams(params),
  });
}
const Ld = /* @__PURE__ */ S("ZodReadonly", (e, t) => {
  eu.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => _h(e, n, o, i), e.unwrap = () => e._zod.def.innerType;
});
function Od(e) {
  return new Ld({
    type: "readonly",
    innerType: e
  });
}
const $d = /* @__PURE__ */ S("ZodCustom", (e, t) => {
  tu.init(e, t), Q.init(e, t), e._zod.processJSONSchema = (n, o, i) => ah(e, n);
});
function Ad(e, t = {}) {
  return /* @__PURE__ */ Vu($d, e, t);
}
function Nd(e, t) {
  return /* @__PURE__ */ Gu(e, t);
}
const Zd = {
  custom: "custom"
}, L = R({ x: g().finite(), y: g().finite() }), On = R({ x: g().finite(), y: g().finite(), width: g().positive(), height: g().positive() }), Dd = R({
  outer: O(L).min(3),
  holes: O(O(L).min(3))
}), Fd = R({
  textureSize: R({ width: g().int().positive(), height: g().int().positive() }),
  alphaThreshold: g().int().min(0).max(255),
  detail: g().min(4).max(256),
  regions: O(Dd).min(1)
}), Ji = R({
  topology: H(["art", "grid"]).optional(),
  rows: g().int().min(2).optional(),
  cols: g().int().min(2).optional(),
  art: Fd.optional(),
  points: O(L),
  uvs: O(L),
  triangles: O(g().int().nonnegative()),
  influences: R({
    face: O(g().min(0).max(1)).optional(),
    skull: O(g().min(0).max(1)).optional(),
    head: O(g().min(0).max(1)).optional(),
    body: O(g().min(0).max(1)).optional(),
    gaze: O(g().min(0).max(1)).optional(),
    physics: O(g().min(0).max(1)).optional(),
    pin: O(g().min(0).max(1)).optional(),
    headAttachment: O(g().min(0).max(1)).optional(),
    physicsRelease: O(g().min(0).max(1)).optional()
  }).optional()
}), qi = R({
  id: I().min(1),
  root: L,
  tip: L,
  width: g().positive().max(1),
  confidence: g().min(0).max(1),
  source: H(["alpha-contour", "corrected"]),
  physics: R({
    stiffness: g().min(1).max(100),
    damping: g().min(0).max(40),
    segments: g().int().min(2).max(12),
    maxDisplacement: g().positive().max(0.5)
  }),
  weights: O(g().min(0).max(1)),
  release: O(g().min(0).max(1))
}), ne = R({
  position: L,
  confidence: g().min(0).max(1),
  source: H(["layer-alpha", "face-alpha", "head-alpha", "inferred", "corrected"])
}), kt = H([
  "headTop",
  "forehead",
  "skullLeft",
  "skullRight",
  "faceLeft",
  "faceRight",
  "eyeLeftOuter",
  "eyeLeft",
  "eyeLeftInner",
  "eyeRightInner",
  "eyeRight",
  "eyeRightOuter",
  "nose",
  "cheekLeft",
  "cheekRight",
  "mouthLeft",
  "mouth",
  "mouthRight",
  "jawLeft",
  "jawRight",
  "chin",
  "neckLeft",
  "neckRight"
]), Wd = R({
  headTop: ne,
  forehead: ne,
  skullLeft: ne,
  skullRight: ne,
  faceLeft: ne,
  faceRight: ne,
  eyeLeftOuter: ne,
  eyeLeft: ne,
  eyeLeftInner: ne,
  eyeRightInner: ne,
  eyeRight: ne,
  eyeRightOuter: ne,
  nose: ne,
  cheekLeft: ne,
  cheekRight: ne,
  mouthLeft: ne,
  mouth: ne,
  mouthRight: ne,
  jawLeft: ne,
  jawRight: ne,
  chin: ne,
  neckLeft: ne,
  neckRight: ne
}), No = Fe([kt, kt, kt]), Bd = H(["forehead", "noseRoot", "noseTip", "upperLip", "lowerLip", "chin"]), Ki = R({
  kind: D("semantic-depth-v1"),
  points: O(R({
    id: Bd,
    position: g().min(0).max(1),
    depth: g().min(-0.5).max(0.5)
  })).length(6)
}).superRefine((e, t) => {
  new Set(e.points.map((o) => o.id)).size !== e.points.length && t.addIssue({ code: "custom", path: ["points"], message: "侧脸深度语义点不能重复。" });
  for (let o = 1; o < e.points.length; o += 1)
    if (e.points[o].position <= e.points[o - 1].position) {
      t.addIssue({ code: "custom", path: ["points", o, "position"], message: "侧脸深度语义点必须从额头到下巴严格递增。" });
      break;
    }
}), Hd = H(["upperChest", "chest", "waist", "hip"]), Qi = R({
  kind: D("torso-volume-v1"),
  strength: g().min(0).max(2),
  points: O(R({
    id: Hd,
    position: g().min(0).max(1),
    depth: g().min(-0.5).max(0.5)
  })).length(4)
}).superRefine((e, t) => {
  new Set(e.points.map((n) => n.id)).size !== e.points.length && t.addIssue({ code: "custom", path: ["points"], message: "躯干体积语义点不能重复。" });
  for (let n = 1; n < e.points.length; n += 1)
    if (e.points[n].position <= e.points[n - 1].position) {
      t.addIssue({ code: "custom", path: ["points", n, "position"], message: "躯干体积语义点必须从肩部到髋部严格递增。" });
      break;
    }
}), er = H([
  "head-yaw",
  "head-pitch",
  "head-roll",
  "body-sway",
  "body-pitch",
  "body-roll",
  "gaze-x",
  "gaze-y",
  "breath",
  "blink",
  "mouth-open",
  "ear-x",
  "ear-y",
  "tail-x",
  "tail-y",
  "blink-left",
  "blink-right",
  "brow-left",
  "brow-right",
  "smile",
  "cheek-puff",
  "mouth-a",
  "mouth-i",
  "mouth-u",
  "mouth-e",
  "mouth-o",
  "arm-left",
  "arm-right",
  "hand-left-x",
  "hand-left-y",
  "hand-right-x",
  "hand-right-y",
  "hand-left-open",
  "hand-right-open"
]), Zo = Se(I().regex(/^\d+$/), L), Yd = R({
  translation: L.optional(),
  rotationDegrees: g().finite().optional(),
  scale: R({ x: g().positive(), y: g().positive() }).optional()
}), Xd = R({
  values: lt([Fe([g().finite()]), Fe([g().finite(), g().finite()])]),
  meshPointDeltas: Zo.optional(),
  warpPointDeltas: Zo.optional(),
  transform: Yd.optional(),
  opacityMultiplier: g().min(0).max(4).optional(),
  drawOrderOffset: g().min(-1e4).max(1e4).optional()
}), tr = R({
  id: I().min(1),
  name: I().min(1),
  group: I().min(1),
  kind: H(["continuous", "toggle"]),
  min: g().finite(),
  default: g().finite(),
  max: g().finite(),
  semantic: er.optional(),
  repeat: J().optional()
}), nr = Gt("kind", [
  R({ id: I().min(1), name: I().min(1), kind: D("rotation"), parentId: I().min(1).optional(), pivot: L }),
  R({
    id: I().min(1),
    name: I().min(1),
    kind: D("warp"),
    parentId: I().min(1).optional(),
    bounds: On,
    rows: g().int().min(2).max(64),
    cols: g().int().min(2).max(64),
    controlPoints: O(L)
  })
]), or = R({
  id: I().min(1),
  parameterIds: lt([Fe([I().min(1)]), Fe([I().min(1), I().min(1)])]),
  blinkMode: H(["geometry", "texture"]).optional(),
  target: R({ kind: H(["layer", "deformer"]), id: I().min(1) }),
  keyforms: O(Xd).min(1)
}), ir = R({
  id: I().min(1),
  name: I().min(1),
  parameters: Se(I().min(1), g().finite())
}), rr = R({
  id: I().min(1),
  name: I().min(1),
  inputParameterId: I().min(1),
  outputParameterId: I().min(1),
  inputScale: g().finite(),
  outputScale: g().finite(),
  response: g().min(0.1).max(30),
  damping: g().min(0).max(2)
}), Ud = R({
  time: g().min(0),
  value: g().finite(),
  easing: H(["linear", "smoothstep", "hold"]).optional()
}), sr = R({
  id: I().min(1),
  name: I().min(1),
  duration: g().positive().max(3600),
  loop: J(),
  autoplay: J().optional(),
  tracks: O(R({
    target: R({ kind: H(["parameter", "expression"]), id: I().min(1) }),
    keyframes: O(Ud).min(1)
  })).min(1)
}), ar = R({
  parameters: O(tr),
  deformers: O(nr),
  bindings: O(or),
  expressions: O(ir).default([]),
  physics: O(rr).default([]),
  behaviors: O(sr).default([])
}), jd = Gt("kind", [
  R({ kind: D("all") }).strict(),
  R({ kind: D("indices"), indices: O(g().int().nonnegative()).min(1) }).strict(),
  R({ kind: D("rect"), rect: R({ x: g().finite(), y: g().finite(), width: g().positive(), height: g().positive() }).strict(), feather: g().min(0).max(1).optional() }).strict(),
  R({ kind: D("circle"), center: L, radius: g().positive(), feather: g().min(0).max(1).optional() }).strict(),
  R({ kind: D("line"), start: L, end: L, radius: g().positive(), feather: g().min(0).max(1).optional() }).strict()
]), Vd = Gt("kind", [
  R({ kind: D("fit-landmarks"), radiusPixels: g().finite().positive(), points: O(R({ label: I().trim().min(1), source: L, target: L }).strict()).min(1).max(64) }).strict(),
  R({ kind: D("curve-warp"), source: Fe([L, L, L]), target: Fe([L, L, L]), profile: O(R({ source: g().finite(), target: g().finite() }).strict()).min(2).max(16), taper: R({ start: g().finite().positive(), middle: g().finite().positive(), end: g().finite().positive() }).strict().optional() }).strict(),
  R({ kind: D("translate"), delta: L }).strict(),
  R({ kind: D("scale"), origin: L, factors: R({ x: g().positive(), y: g().positive() }).strict() }).strict(),
  R({ kind: D("rotate"), origin: L, degrees: g().finite() }).strict(),
  R({ kind: D("bend"), axis: H(["x", "y"]), center: g().finite(), halfSpan: g().positive(), amount: g().finite() }).strict(),
  R({ kind: D("smooth"), strength: g().min(0).max(1), iterations: g().int().min(1).max(32), preserveBoundary: J().optional() }).strict()
]), Gd = R({
  op: D("transform-keyform"),
  bindingId: I().min(1),
  values: lt([Fe([g().finite()]), Fe([g().finite(), g().finite()])]),
  coordinateSpace: D("rest-canvas"),
  selection: jd,
  transforms: O(Vd).min(1).max(32)
}).strict(), cr = Gt("op", [
  Gd,
  R({ op: D("insert-binding-key"), bindingId: I().min(1), parameterId: I().min(1), value: g().finite() }).strict(),
  R({ op: D("upsert-parameter"), parameter: tr }),
  R({ op: D("remove-parameter"), id: I().min(1), cascade: J().optional() }),
  R({ op: D("upsert-deformer"), deformer: nr }),
  R({ op: D("remove-deformer"), id: I().min(1), cascade: J().optional() }),
  R({ op: D("set-layer-deformer"), layerId: I().min(1), deformerId: I().min(1).nullable() }),
  R({ op: D("set-layer-head-pose"), layerId: I().min(1), mode: H(["keyforms", "procedural"]) }),
  R({ op: D("move-layer"), layerId: I().min(1), beforeLayerId: I().min(1).optional(), afterLayerId: I().min(1).optional() }),
  R({ op: D("upsert-binding"), binding: or }),
  R({ op: D("remove-binding"), id: I().min(1) }),
  R({ op: D("upsert-expression"), expression: ir }),
  R({ op: D("remove-expression"), id: I().min(1), cascade: J().optional() }),
  R({ op: D("upsert-physics"), physics: rr }),
  R({ op: D("remove-physics"), id: I().min(1) }),
  R({ op: D("upsert-behavior"), behavior: sr }),
  R({ op: D("remove-behavior"), id: I().min(1) })
]), ur = R({
  id: I().regex(/^[a-zA-Z0-9][a-zA-Z0-9_-]*$/),
  label: I().trim().min(1).max(80),
  parameters: Se(I().min(1), g().finite()).optional(),
  expressions: Se(I().min(1), g().min(0).max(1)).optional(),
  behavior: R({ id: I().min(1), timeSeconds: g().min(0) }).optional(),
  settleSeconds: g().min(0).max(10).optional()
}).refine((e) => e.parameters || e.expressions || e.behavior, { message: "Authoring 预览必须驱动参数、表情或行为。" });
function hr(e, t) {
  e.operations.forEach((n, o) => {
    n.op === "move-layer" && !!n.beforeLayerId == !!n.afterLayerId && t.addIssue({
      code: Zd.custom,
      path: ["operations", o],
      message: "move-layer 必须且只能提供 beforeLayerId 或 afterLayerId。"
    });
  });
}
const Jd = R({
  version: D(1),
  changes: O(R({ collection: H(["parameters", "deformers", "bindings", "expressions", "physics", "behaviors", "layers"]), id: I().min(1), kind: H(["added", "removed", "updated"]), fields: O(I()) })).optional(),
  operations: O(cr).min(1).max(200),
  previews: O(ur).max(12)
}).superRefine(hr);
R({
  version: D(1),
  baseRevision: g().int().nonnegative(),
  label: I().trim().min(1).max(160).optional(),
  operations: O(cr).min(1).max(200),
  previews: O(ur).max(12).optional()
}).superRefine(hr);
const qd = R({
  variants: O(R({
    id: I().min(1).max(128),
    name: I().min(1).max(256),
    defaultOptionId: I().min(1).max(128),
    options: O(R({ id: I().min(1).max(128), name: I().min(1).max(256), layerIds: O(I().min(1)).min(1) })).min(1)
  })).default([]),
  props: O(R({
    id: I().min(1).max(128),
    name: I().min(1).max(256),
    layerIds: O(I().min(1)).min(1),
    slot: H(["head", "face", "body", "hand-left", "hand-right", "free"]),
    defaultEnabled: J().optional()
  })).default([]),
  presets: O(R({
    id: I().min(1).max(128),
    name: I().min(1).max(256),
    variants: Se(I().min(1), I().min(1)).optional(),
    props: O(I().min(1)).optional(),
    parameters: Se(I().min(1), g().finite()).optional(),
    expressions: Se(I().min(1), g().min(0).max(1)).optional()
  })).default([])
}), Kd = R({
  motionLimits: O(R({ id: I().min(1), semantic: er, min: g().finite(), max: g().finite() })).default([]),
  collisions: O(R({
    id: I().min(1),
    name: I().min(1),
    movingLayerIds: O(I().min(1)).min(1),
    colliderLayerIds: O(I().min(1)).min(1),
    padding: g().min(0).max(0.25),
    maxCorrection: g().min(0).max(0.25),
    strength: g().min(0).max(1)
  })).default([])
}), Qd = R({
  version: lt([D(1), D(2), D(3), D(4)]),
  name: I().min(1),
  canvas: R({ width: g().int().positive(), height: g().int().positive() }),
  source: R({
    originalFileName: I().min(1),
    psdSha256: I().regex(/^[a-f0-9]{64}$/),
    psdPath: I().min(1),
    referencePath: I().optional(),
    referenceSha256: I().regex(/^[a-f0-9]{64}$/).optional()
  }),
  rigLevel: H(["semantic", "grouped", "minimal"]),
  layers: O(R({
    id: I().min(1),
    sourceName: I(),
    sourcePath: O(I()),
    sourceLayerId: I().min(1).optional(),
    generatedAsset: R({ referenceId: I().min(1), registrationId: I().min(1), imageSha256: I().regex(/^[a-f0-9]{64}$/), templateLayerId: I().min(1) }).optional(),
    role: H([
      "backHair",
      "frontHair",
      "sideHair",
      "face",
      "eyeWhite",
      "iris",
      "eyelash",
      "eyeClosed",
      "eyebrow",
      "nose",
      "mouth",
      "ear",
      "neck",
      "topWear",
      "bottomWear",
      "arm",
      "hand",
      "leg",
      "foot",
      "headwear",
      "tail",
      "accessory",
      "unknown"
    ]),
    side: H(["left", "right", "center"]),
    order: g().int(),
    opacity: g().min(0).max(1),
    blendMode: I(),
    bounds: On,
    texture: I().min(1),
    pivot: L,
    garmentStructure: H(["soft", "supported"]).optional(),
    garmentFlexibility: g().min(0).max(0.5).optional(),
    headwearPerspective: D("crown").optional(),
    blinkMode: H(["geometry", "texture"]).optional(),
    headPoseMode: D("keyforms").optional(),
    secondaryAnchors: R({
      earHingeLeft: L.optional(),
      earHingeRight: L.optional(),
      frontHairRoot: L.optional(),
      frontHairRootLeft: L.optional(),
      frontHairRootRight: L.optional(),
      frontHairTipLeft: L.optional(),
      frontHairTipRight: L.optional(),
      ahogeRoot: L.optional()
    }).optional(),
    hairStrands: O(qi).min(2).max(12).optional(),
    mesh: Ji,
    weights: R({ head: g(), body: g(), gaze: g(), physics: g() }),
    clipLayerId: I().optional(),
    mouthVariant: H(["closed", "slight", "open", "a", "i", "u", "e", "o"]).optional(),
    parentGroup: H(["head", "body", "root"]),
    parentLayerId: I().min(1).optional(),
    deformerId: I().min(1).optional(),
    visible: J().optional(),
    locked: J().optional()
  })).min(1),
  model: ar.optional(),
  anchors: R({
    headTop: L.optional(),
    forehead: L.optional(),
    eyeLeft: L.optional(),
    eyeRight: L.optional(),
    cheekLeft: L.optional(),
    cheekRight: L.optional(),
    nose: L.optional(),
    mouth: L.optional(),
    chin: L.optional(),
    neck: L.optional(),
    shoulderLeft: L.optional(),
    shoulderRight: L.optional(),
    bodyCenter: L.optional()
  }),
  runtime: R({
    seed: g().int(),
    profile: H(["calm-v1", "coherent-v1", "coherent-v2", "coherent-v3"]),
    envelope: R({
      headYaw: g().nonnegative(),
      headPitch: g().nonnegative(),
      headRollDegrees: g().nonnegative(),
      bodySway: g().nonnegative(),
      bodyRollDegrees: g().nonnegative(),
      gazeX: g().nonnegative(),
      gazeY: g().nonnegative(),
      breath: g().nonnegative(),
      globalScale: g().positive()
    }),
    features: R({ headTurn: J(), bodyFollow: J(), gaze: J(), hairPhysics: J(), blink: J(), mouthMotion: J(), asymmetricBlink: J().optional(), visemes: J().optional(), upperBodyTracking: J().optional() }),
    poseField: R({
      kind: H(["ellipsoid-v1", "head-surfaces-v2"]),
      center: L,
      radiusX: g().positive(),
      radiusY: g().positive(),
      skullCenter: L.optional(),
      skullRadiusX: g().positive().optional(),
      skullRadiusY: g().positive().optional(),
      maxYawRadians: g().nonnegative(),
      maxPitchRadians: g().nonnegative(),
      maxPitchUpRadians: g().nonnegative().optional(),
      maxPitchDownRadians: g().nonnegative().optional(),
      perspective: g().min(0).max(0.5),
      contourStrength: g().min(0).max(1.6).optional(),
      depthStrength: g().min(0).max(1.6).optional(),
      faceDepthProfile: Ki.optional()
    }).optional(),
    poseOcclusion: R({
      kind: D("semantic-occlusion-v1"),
      fadeStart: g().min(0).max(0.95),
      farEyeOpacity: g().min(0).max(1),
      farBrowOpacity: g().min(0).max(1),
      farEarOpacity: g().min(0).max(1),
      farSideHairOpacity: g().min(0).max(1),
      sideHairDepthSwap: J()
    }).optional(),
    torsoVolumeProfile: Qi.optional(),
    semanticCage: R({
      kind: D("semantic-face-cage-v1"),
      coordinateConvention: D("screen-space"),
      points: Wd,
      faceTriangles: O(No),
      skullTriangles: O(No),
      roleGroups: R({
        face: O(H(["backHair", "frontHair", "sideHair", "face", "eyeWhite", "iris", "eyelash", "eyeClosed", "eyebrow", "nose", "mouth", "ear", "neck", "topWear", "bottomWear", "arm", "hand", "leg", "foot", "headwear", "tail", "accessory", "unknown"])),
        skull: O(H(["backHair", "frontHair", "sideHair", "face", "eyeWhite", "iris", "eyelash", "eyeClosed", "eyebrow", "nose", "mouth", "ear", "neck", "topWear", "bottomWear", "arm", "hand", "leg", "foot", "headwear", "tail", "accessory", "unknown"]))
      }),
      validation: R({
        status: H(["passed", "corrected"]),
        confidence: g().min(0).max(1),
        corrections: O(I()),
        checks: O(I())
      })
    }).optional(),
    motionTuning: R({
      amplitude: g().min(0).max(1.5),
      response: g().min(0).max(1),
      stability: g().min(0).max(1)
    }).optional(),
    secondaryMotionTuning: bn(H(["frontHair", "backHair", "ahoge", "headwear", "ears", "topCloth", "skirt", "tail", "accessory"]), R({
      amplitude: g().min(0).max(1.5),
      response: g().min(0).max(1),
      stability: g().min(0).max(1)
    })).optional(),
    constraints: Kd.optional()
  }),
  production: qd.optional(),
  quality: R({
    neutralSimilarity: g().min(-1).max(1).optional(),
    poseValidations: O(R({ id: I(), headYaw: g(), headPitch: g(), headRoll: g(), score: g().min(0).max(1), passed: J(), issues: O(To()) })),
    safetyScale: g().min(0).max(1),
    issues: O(To())
  }),
  disabledReasons: O(I())
}).superRefine((e, t) => {
  const n = /* @__PURE__ */ new Set(), o = /* @__PURE__ */ new Set(), i = (l) => !/^(?:[a-z]:|[/\\])/i.test(l) && !l.split(/[\\/]+/).some((p) => p === "..");
  i(e.source.psdPath) || t.addIssue({ code: "custom", path: ["source", "psdPath"], message: "PSD 路径必须位于项目目录内。" }), e.source.referencePath && !i(e.source.referencePath) && t.addIssue({ code: "custom", path: ["source", "referencePath"], message: "参考图路径必须位于项目目录内。" });
  for (let l = 0; l < e.layers.length; l += 1) {
    const p = e.layers[l];
    n.has(p.id) && t.addIssue({ code: "custom", path: ["layers", l, "id"], message: `图层 ID 重复：${p.id}` }), n.add(p.id), i(p.texture) || t.addIssue({ code: "custom", path: ["layers", l, "texture"], message: "纹理路径必须位于项目目录内。" });
    const x = p.mesh.topology ?? "grid";
    if (e.version === 4 && !p.mesh.topology && t.addIssue({ code: "custom", path: ["layers", l, "mesh", "topology"], message: "v4 网格必须声明 topology。" }), x === "grid") {
      if (p.mesh.rows === void 0 || p.mesh.cols === void 0)
        t.addIssue({ code: "custom", path: ["layers", l, "mesh"], message: "规则网格必须包含 rows 和 cols。" });
      else {
        const w = p.mesh.rows * p.mesh.cols;
        p.mesh.points.length !== w && t.addIssue({ code: "custom", path: ["layers", l, "mesh", "points"], message: `网格点数应为 ${w}。` });
      }
      p.mesh.art && t.addIssue({ code: "custom", path: ["layers", l, "mesh", "art"], message: "规则网格不能携带 ArtMesh 轮廓来源。" });
    } else p.mesh.art ? ((p.mesh.rows !== void 0 || p.mesh.cols !== void 0) && t.addIssue({ code: "custom", path: ["layers", l, "mesh"], message: "Alpha ArtMesh 不使用 rows 或 cols。" }), p.mesh.art.regions.flatMap((P) => [P.outer, ...P.holes]).flat().some((P) => P.x < 0 || P.x > 1 || P.y < 0 || P.y > 1) && t.addIssue({ code: "custom", path: ["layers", l, "mesh", "art", "regions"], message: "ArtMesh 轮廓 UV 必须位于 0..1。" })) : t.addIssue({ code: "custom", path: ["layers", l, "mesh", "art"], message: "Alpha ArtMesh 必须保存可重建的轮廓来源。" });
    p.mesh.uvs.length !== p.mesh.points.length && t.addIssue({ code: "custom", path: ["layers", l, "mesh", "uvs"], message: "UV 数量必须与网格点数一致。" }), p.mesh.uvs.some((w) => w.x < 0 || w.x > 1 || w.y < 0 || w.y > 1) && t.addIssue({ code: "custom", path: ["layers", l, "mesh", "uvs"], message: "UV 必须位于 0..1。" }), p.mesh.points.length > 65535 && t.addIssue({ code: "custom", path: ["layers", l, "mesh", "points"], message: "WebGL 索引网格不能超过 65535 个点。" }), p.mesh.triangles.length % 3 !== 0 && t.addIssue({ code: "custom", path: ["layers", l, "mesh", "triangles"], message: "三角形索引数量必须是 3 的倍数。" }), p.mesh.triangles.some((w) => w >= p.mesh.points.length || w > 65535) && t.addIssue({ code: "custom", path: ["layers", l, "mesh", "triangles"], message: "三角形引用了不存在或无法渲染的网格点。" });
    for (let w = 0; w < p.mesh.triangles.length; w += 3) {
      const P = p.mesh.triangles.slice(w, w + 3);
      if (P.length === 3 && new Set(P).size !== 3) {
        t.addIssue({ code: "custom", path: ["layers", l, "mesh", "triangles", w], message: "三角形不能重复引用同一顶点。" });
        break;
      }
    }
    for (const [w, P] of Object.entries(p.mesh.influences ?? {}))
      P && P.length !== p.mesh.points.length && t.addIssue({ code: "custom", path: ["layers", l, "mesh", "influences", w], message: "顶点权重数量必须与网格点数一致。" });
    const k = /* @__PURE__ */ new Set();
    p.hairStrands && !["frontHair", "backHair", "sideHair"].includes(p.role) && t.addIssue({ code: "custom", path: ["layers", l, "hairStrands"], message: "只有前发、后发和侧发图层可以包含房束。" });
    for (let w = 0; w < (p.hairStrands?.length ?? 0); w += 1) {
      const P = p.hairStrands[w];
      k.has(P.id) && t.addIssue({ code: "custom", path: ["layers", l, "hairStrands", w, "id"], message: "同一图层的房束 ID 不能重复。" }), o.has(P.id) && t.addIssue({ code: "custom", path: ["layers", l, "hairStrands", w, "id"], message: "房束 ID 必须在整个项目中唯一。" }), k.add(P.id), o.add(P.id), (P.weights.length !== p.mesh.points.length || P.release.length !== p.mesh.points.length) && t.addIssue({ code: "custom", path: ["layers", l, "hairStrands", w], message: "房束权重与释放数组必须对应全部网格顶点。" });
    }
  }
  for (let l = 0; l < e.layers.length; l += 1) {
    const p = e.layers[l];
    p.clipLayerId && (!n.has(p.clipLayerId) || p.clipLayerId === p.id) && t.addIssue({ code: "custom", path: ["layers", l, "clipLayerId"], message: "裁剪图层引用无效。" }), p.parentLayerId && (!n.has(p.parentLayerId) || p.parentLayerId === p.id) && t.addIssue({ code: "custom", path: ["layers", l, "parentLayerId"], message: "父图层引用无效。" });
    const x = /* @__PURE__ */ new Set([p.id]);
    let k = p.parentLayerId;
    for (; k; ) {
      if (x.has(k)) {
        t.addIssue({ code: "custom", path: ["layers", l, "parentLayerId"], message: "父图层关系形成循环。" });
        break;
      }
      x.add(k), k = e.layers.find((w) => w.id === k)?.parentLayerId;
    }
  }
  const s = /* @__PURE__ */ new Set();
  for (const [l, p] of (e.runtime.constraints?.motionLimits ?? []).entries())
    s.has(p.id) && t.addIssue({ code: "custom", path: ["runtime", "constraints", "motionLimits", l, "id"], message: "约束 ID 不能重复。" }), s.add(p.id), p.min > p.max && t.addIssue({ code: "custom", path: ["runtime", "constraints", "motionLimits", l], message: "运动范围必须满足 min <= max。" });
  for (const [l, p] of (e.runtime.constraints?.collisions ?? []).entries()) {
    s.has(p.id) && t.addIssue({ code: "custom", path: ["runtime", "constraints", "collisions", l, "id"], message: "约束 ID 不能重复。" }), s.add(p.id);
    for (const x of [...p.movingLayerIds, ...p.colliderLayerIds])
      n.has(x) || t.addIssue({ code: "custom", path: ["runtime", "constraints", "collisions", l], message: `碰撞约束引用了不存在的图层：${x}` });
    p.movingLayerIds.some((x) => p.colliderLayerIds.includes(x)) && t.addIssue({ code: "custom", path: ["runtime", "constraints", "collisions", l], message: "移动图层不能同时作为自身碰撞体。" });
  }
  if (e.production) {
    const l = /* @__PURE__ */ new Set(), p = /* @__PURE__ */ new Set(), x = /* @__PURE__ */ new Set(), k = /* @__PURE__ */ new Set();
    for (const [w, P] of e.production.variants.entries()) {
      l.has(P.id) && t.addIssue({ code: "custom", path: ["production", "variants", w, "id"], message: "换装组 ID 不能重复。" }), l.add(P.id), new Set(P.options.map((_) => _.id)).has(P.defaultOptionId) || t.addIssue({ code: "custom", path: ["production", "variants", w, "defaultOptionId"], message: "默认选项必须属于当前换装组。" });
      for (const [_, B] of P.options.entries()) {
        p.has(B.id) && t.addIssue({ code: "custom", path: ["production", "variants", w, "options", _, "id"], message: "换装选项 ID 必须在项目中唯一。" }), p.add(B.id);
        for (const $ of B.layerIds)
          n.has($) || t.addIssue({ code: "custom", path: ["production", "variants", w, "options", _], message: `换装选项引用了不存在的图层：${$}` });
      }
    }
    for (const [w, P] of e.production.props.entries()) {
      x.has(P.id) && t.addIssue({ code: "custom", path: ["production", "props", w, "id"], message: "道具 ID 不能重复。" }), x.add(P.id);
      for (const M of P.layerIds)
        n.has(M) || t.addIssue({ code: "custom", path: ["production", "props", w], message: `道具引用了不存在的图层：${M}` });
    }
    for (const [w, P] of e.production.presets.entries()) {
      k.has(P.id) && t.addIssue({ code: "custom", path: ["production", "presets", w, "id"], message: "状态预设 ID 不能重复。" }), k.add(P.id);
      for (const [M, _] of Object.entries(P.variants ?? {}))
        (!l.has(M) || !p.has(_)) && t.addIssue({ code: "custom", path: ["production", "presets", w, "variants"], message: `状态预设引用了无效换装：${M}=${_}` });
      for (const M of P.props ?? [])
        x.has(M) || t.addIssue({ code: "custom", path: ["production", "presets", w, "props"], message: `状态预设引用了不存在的道具：${M}` });
    }
  }
  if (e.version >= 3 && !e.model && t.addIssue({ code: "custom", path: ["model"], message: "v3 及以上项目必须包含 authoring model。" }), !e.model)
    return;
  const r = /* @__PURE__ */ new Set(), a = /* @__PURE__ */ new Set();
  for (let l = 0; l < e.model.parameters.length; l += 1) {
    const p = e.model.parameters[l];
    r.has(p.id) && t.addIssue({ code: "custom", path: ["model", "parameters", l, "id"], message: `参数 ID 重复：${p.id}` }), r.add(p.id), (!(p.min <= p.default && p.default <= p.max) || p.min === p.max) && t.addIssue({ code: "custom", path: ["model", "parameters", l], message: "参数必须满足 min < max 且 default 位于范围内。" }), p.semantic && (a.has(p.semantic) && t.addIssue({ code: "custom", path: ["model", "parameters", l, "semantic"], message: `语义参数重复：${p.semantic}` }), a.add(p.semantic));
  }
  const c = /* @__PURE__ */ new Set();
  for (let l = 0; l < e.model.deformers.length; l += 1) {
    const p = e.model.deformers[l];
    c.has(p.id) && t.addIssue({ code: "custom", path: ["model", "deformers", l, "id"], message: `变形器 ID 重复：${p.id}` }), c.add(p.id), p.kind === "warp" && p.controlPoints.length !== p.rows * p.cols && t.addIssue({ code: "custom", path: ["model", "deformers", l, "controlPoints"], message: `网格变形器控制点数应为 ${p.rows * p.cols}。` });
  }
  for (let l = 0; l < e.model.deformers.length; l += 1) {
    const p = e.model.deformers[l];
    p.parentId && (!c.has(p.parentId) || p.parentId === p.id) && t.addIssue({ code: "custom", path: ["model", "deformers", l, "parentId"], message: "父变形器引用无效。" });
    const x = /* @__PURE__ */ new Set([p.id]);
    let k = p.parentId;
    for (; k; ) {
      if (x.has(k)) {
        t.addIssue({ code: "custom", path: ["model", "deformers", l, "parentId"], message: "变形器层级形成循环。" });
        break;
      }
      x.add(k), k = e.model.deformers.find((w) => w.id === k)?.parentId;
    }
  }
  for (let l = 0; l < e.layers.length; l += 1) {
    const p = e.layers[l].deformerId;
    p && !c.has(p) && t.addIssue({ code: "custom", path: ["layers", l, "deformerId"], message: "图层引用了不存在的变形器。" });
  }
  const u = /* @__PURE__ */ new Set();
  for (let l = 0; l < e.model.bindings.length; l += 1) {
    const p = e.model.bindings[l];
    u.has(p.id) && t.addIssue({ code: "custom", path: ["model", "bindings", l, "id"], message: `绑定 ID 重复：${p.id}` }), u.add(p.id), (new Set(p.parameterIds).size !== p.parameterIds.length || p.parameterIds.some((P) => !r.has(P))) && t.addIssue({ code: "custom", path: ["model", "bindings", l, "parameterIds"], message: "绑定引用了重复或不存在的参数。" }), (p.target.kind === "layer" ? n.has(p.target.id) : c.has(p.target.id)) || t.addIssue({ code: "custom", path: ["model", "bindings", l, "target"], message: "绑定目标不存在。" });
    const k = p.parameterIds.map((P) => e.model.parameters.find((M) => M.id === P)), w = /* @__PURE__ */ new Set();
    for (let P = 0; P < p.keyforms.length; P += 1) {
      const M = p.keyforms[P];
      M.values.length !== p.parameterIds.length && t.addIssue({ code: "custom", path: ["model", "bindings", l, "keyforms", P, "values"], message: "关键形态坐标维度必须与绑定参数数量一致。" });
      const _ = M.values.join("\0");
      w.has(_) && t.addIssue({ code: "custom", path: ["model", "bindings", l, "keyforms", P, "values"], message: "关键形态坐标重复。" }), w.add(_), M.values.forEach((z, A) => {
        const C = k[A];
        C && (z < C.min || z > C.max) && t.addIssue({ code: "custom", path: ["model", "bindings", l, "keyforms", P, "values", A], message: "关键形态坐标超出参数范围。" });
      });
      const B = p.target.kind === "layer" ? e.layers.find((z) => z.id === p.target.id) : void 0, $ = p.target.kind === "deformer" ? e.model.deformers.find((z) => z.id === p.target.id) : void 0;
      M.meshPointDeltas && (!B || Object.keys(M.meshPointDeltas).some((z) => Number(z) >= B.mesh.points.length)) && t.addIssue({ code: "custom", path: ["model", "bindings", l, "keyforms", P, "meshPointDeltas"], message: "网格点增量只能引用目标图层已有的点。" }), M.warpPointDeltas && ($?.kind !== "warp" || Object.keys(M.warpPointDeltas).some((z) => Number(z) >= $.controlPoints.length)) && t.addIssue({ code: "custom", path: ["model", "bindings", l, "keyforms", P, "warpPointDeltas"], message: "控制点增量只能引用目标网格变形器已有的点。" }), p.target.kind !== "layer" && (M.opacityMultiplier !== void 0 || M.drawOrderOffset !== void 0) && t.addIssue({ code: "custom", path: ["model", "bindings", l, "keyforms", P], message: "透明度和绘制顺序只适用于图层绑定。" });
    }
    if (p.parameterIds.length === 2) {
      const P = new Set(p.keyforms.map((_) => _.values[0])), M = new Set(p.keyforms.map((_) => _.values[1]));
      p.keyforms.length !== P.size * M.size && t.addIssue({ code: "custom", path: ["model", "bindings", l, "keyforms"], message: "双参数绑定必须提供完整的矩形关键形态网格。" });
    }
  }
  const h = /* @__PURE__ */ new Set();
  for (let l = 0; l < e.model.expressions.length; l += 1) {
    const p = e.model.expressions[l];
    h.has(p.id) && t.addIssue({ code: "custom", path: ["model", "expressions", l, "id"], message: `表情 ID 重复：${p.id}` }), h.add(p.id);
    for (const [x, k] of Object.entries(p.parameters)) {
      const w = e.model.parameters.find((P) => P.id === x);
      (!w || k < w.min || k > w.max) && t.addIssue({ code: "custom", path: ["model", "expressions", l, "parameters", x], message: "表情引用了不存在的参数或超出参数范围。" });
    }
  }
  const d = /* @__PURE__ */ new Set(), f = /* @__PURE__ */ new Set();
  for (let l = 0; l < e.model.physics.length; l += 1) {
    const p = e.model.physics[l];
    d.has(p.id) && t.addIssue({ code: "custom", path: ["model", "physics", l, "id"], message: `物理组 ID 重复：${p.id}` }), d.add(p.id), (!r.has(p.inputParameterId) || !r.has(p.outputParameterId) || p.inputParameterId === p.outputParameterId) && t.addIssue({ code: "custom", path: ["model", "physics", l], message: "物理组必须引用两个不同且存在的输入、输出参数。" }), f.has(p.outputParameterId) && t.addIssue({ code: "custom", path: ["model", "physics", l, "outputParameterId"], message: "一个参数只能由一个物理组输出。" }), f.add(p.outputParameterId);
    const x = /* @__PURE__ */ new Set([p.inputParameterId]);
    let k = p.outputParameterId;
    for (; k; ) {
      if (x.has(k)) {
        t.addIssue({ code: "custom", path: ["model", "physics", l], message: "物理参数依赖形成循环。" });
        break;
      }
      x.add(k), k = e.model.physics.find((w) => w.inputParameterId === k)?.outputParameterId ?? "";
    }
  }
  const m = /* @__PURE__ */ new Set();
  for (let l = 0; l < e.model.behaviors.length; l += 1) {
    const p = e.model.behaviors[l];
    m.has(p.id) && t.addIssue({ code: "custom", path: ["model", "behaviors", l, "id"], message: `行为 ID 重复：${p.id}` }), m.add(p.id);
    for (let x = 0; x < p.tracks.length; x += 1) {
      const k = p.tracks[x];
      (k.target.kind === "parameter" ? r.has(k.target.id) : h.has(k.target.id)) || t.addIssue({ code: "custom", path: ["model", "behaviors", l, "tracks", x, "target"], message: "行为轨道目标不存在。" });
      let P = -1;
      for (let M = 0; M < k.keyframes.length; M += 1) {
        const _ = k.keyframes[M];
        (_.time <= P || _.time > p.duration) && t.addIssue({ code: "custom", path: ["model", "behaviors", l, "tracks", x, "keyframes", M, "time"], message: "行为关键帧时间必须严格递增且不超过 duration。" }), P = _.time;
        const B = k.target.kind === "parameter" ? e.model.parameters.find(($) => $.id === k.target.id) : void 0;
        (B && (_.value < B.min || _.value > B.max) || k.target.kind === "expression" && (_.value < 0 || _.value > 1)) && t.addIssue({ code: "custom", path: ["model", "behaviors", l, "tracks", x, "keyframes", M, "value"], message: "行为关键帧值超出目标范围。" });
      }
    }
  }
}), el = R({
  headTop: L.optional(),
  forehead: L.optional(),
  eyeLeft: L.optional(),
  eyeRight: L.optional(),
  cheekLeft: L.optional(),
  cheekRight: L.optional(),
  nose: L.optional(),
  mouth: L.optional(),
  chin: L.optional(),
  neck: L.optional(),
  shoulderLeft: L.optional(),
  shoulderRight: L.optional(),
  bodyCenter: L.optional()
}).partial(), tl = R({
  earHingeLeft: L.nullable().optional(),
  earHingeRight: L.nullable().optional(),
  frontHairRoot: L.nullable().optional(),
  frontHairRootLeft: L.nullable().optional(),
  frontHairRootRight: L.nullable().optional(),
  frontHairTipLeft: L.nullable().optional(),
  frontHairTipRight: L.nullable().optional(),
  ahogeRoot: L.nullable().optional()
}).partial(), nl = Se(I().regex(/^\d+$/), R({
  x: g().min(-0.25).max(0.25),
  y: g().min(-0.25).max(0.25)
})), Oe = Se(I().regex(/^\d+$/), g().min(0).max(1)), ol = R({
  role: H([
    "backHair",
    "frontHair",
    "sideHair",
    "face",
    "eyeWhite",
    "iris",
    "eyelash",
    "eyeClosed",
    "eyebrow",
    "nose",
    "mouth",
    "ear",
    "neck",
    "topWear",
    "bottomWear",
    "arm",
    "hand",
    "leg",
    "foot",
    "headwear",
    "tail",
    "accessory",
    "unknown"
  ]).optional(),
  side: H(["left", "right", "center"]).optional(),
  parentGroup: H(["head", "body", "root"]).optional(),
  parentLayerId: I().min(1).nullable().optional(),
  deformerId: I().min(1).nullable().optional(),
  order: g().int().optional(),
  visible: J().optional(),
  locked: J().optional(),
  pivot: L.optional(),
  garmentStructure: H(["soft", "supported"]).optional(),
  garmentFlexibility: g().min(0).max(0.5).optional(),
  headwearPerspective: D("crown").nullable().optional(),
  blinkMode: H(["geometry", "texture"]).nullable().optional(),
  headPoseMode: D("keyforms").nullable().optional(),
  clipLayerId: I().min(1).nullable().optional(),
  secondaryAnchors: tl.optional(),
  hairStrands: O(qi).min(2).max(12).optional(),
  weights: R({
    head: g().min(0).max(1).optional(),
    body: g().min(0).max(1).optional(),
    gaze: g().min(0).max(1).optional(),
    physics: g().min(0).max(1).optional()
  }).partial().optional(),
  mesh: Ji.optional(),
  meshPointDeltas: nl.optional(),
  meshDetail: g().min(4).max(256).optional(),
  vertexInfluences: R({
    face: Oe.optional(),
    skull: Oe.optional(),
    head: Oe.optional(),
    body: Oe.optional(),
    gaze: Oe.optional(),
    physics: Oe.optional(),
    pin: Oe.optional(),
    headAttachment: Oe.optional(),
    physicsRelease: Oe.optional()
  }).partial().optional(),
  meshDensity: R({ rows: g().int().min(2).max(64), cols: g().int().min(2).max(64) }).optional()
}).refine((e) => e.meshDetail === void 0 || e.meshDensity === void 0, { message: "不能同时按细节尺度和行列数重建网格。" }), St = R({
  assetLayers: Se(I().min(1), Qd.shape.layers.element).optional(),
  model: ar.optional(),
  anchors: el.optional(),
  semanticPoints: bn(kt, L).optional(),
  layers: Se(I().min(1), ol).optional(),
  runtime: R({
    features: R({ blink: J().optional(), asymmetricBlink: J().optional() }).strict().optional(),
    envelope: R({
      headYaw: g().min(0).max(1).optional(),
      headPitch: g().min(0).max(1).optional(),
      headRollDegrees: g().min(0).max(45).optional(),
      bodySway: g().min(0).max(0.25).optional(),
      bodyRollDegrees: g().min(0).max(30).optional(),
      gazeX: g().min(0).max(1).optional(),
      gazeY: g().min(0).max(1).optional(),
      breath: g().min(0).max(0.08).optional(),
      globalScale: g().min(0.5).max(1.5).optional()
    }).partial().optional(),
    poseField: R({
      center: L.optional(),
      radiusX: g().finite().positive().optional(),
      radiusY: g().finite().positive().optional(),
      skullCenter: L.optional(),
      skullRadiusX: g().finite().positive().optional(),
      skullRadiusY: g().finite().positive().optional(),
      maxYawRadians: g().min(0).max(0.7).optional(),
      maxPitchRadians: g().min(0).max(0.55).optional(),
      maxPitchUpRadians: g().min(0).max(0.55).optional(),
      maxPitchDownRadians: g().min(0).max(0.55).optional(),
      perspective: g().min(0).max(0.5).optional(),
      contourStrength: g().min(0).max(1.6).optional(),
      depthStrength: g().min(0).max(1.6).optional(),
      faceDepthProfile: Ki.optional()
    }).partial().optional(),
    poseOcclusion: R({
      fadeStart: g().min(0).max(0.95).optional(),
      farEyeOpacity: g().min(0).max(1).optional(),
      farBrowOpacity: g().min(0).max(1).optional(),
      farEarOpacity: g().min(0).max(1).optional(),
      farSideHairOpacity: g().min(0).max(1).optional(),
      sideHairDepthSwap: J().optional()
    }).partial().optional(),
    torsoVolumeProfile: Qi.optional(),
    motionTuning: R({
      amplitude: g().min(0).max(1.5).optional(),
      response: g().min(0).max(1).optional(),
      stability: g().min(0).max(1).optional()
    }).partial().optional(),
    secondaryMotionTuning: bn(H(["frontHair", "backHair", "ahoge", "headwear", "ears", "topCloth", "skirt", "tail", "accessory"]), R({
      amplitude: g().min(0).max(1.5).optional(),
      response: g().min(0).max(1).optional(),
      stability: g().min(0).max(1).optional()
    }).partial()).optional()
  }).optional()
}), il = R({
  baseRevision: g().int().nonnegative(),
  label: I().trim().min(1).max(160).optional(),
  overrides: St,
  authoring: Jd.optional(),
  clear: R({
    model: J().optional(),
    anchors: O(H(["headTop", "forehead", "eyeLeft", "eyeRight", "cheekLeft", "cheekRight", "nose", "mouth", "chin", "neck", "shoulderLeft", "shoulderRight", "bodyCenter"])).optional(),
    semanticPoints: O(kt).optional(),
    layers: O(I().min(1)).optional(),
    runtime: O(H(["envelope", "poseField", "poseOcclusion", "torsoVolumeProfile", "motionTuning", "secondaryMotionTuning"])).optional()
  }).optional()
});
R({
  version: lt([D(1), D(2)]),
  baseProjectSha256: I().regex(/^[a-f0-9]{64}$/),
  revision: g().int().nonnegative(),
  updatedAt: I().datetime(),
  label: I().trim().min(1).max(160).optional(),
  overrides: St,
  headSessionId: I().min(1).optional()
});
R({
  version: D(1),
  id: I().min(1),
  createdAt: I().datetime(),
  label: I().trim().min(1).max(160),
  fromRevision: g().int().nonnegative(),
  toRevision: g().int().positive(),
  beforeFingerprint: I().regex(/^[a-f0-9]{64}$/),
  afterFingerprint: I().regex(/^[a-f0-9]{64}$/),
  patch: il,
  beforeOverrides: St,
  afterOverrides: St,
  evidenceStatus: H(["unreviewed", "accepted", "rejected"]),
  parentSessionId: I().min(1).optional(),
  operationId: I().min(1).optional(),
  evidenceDirectory: I().min(1).optional()
});
R({
  version: D(1),
  id: I().min(1),
  kind: D("calibration-commit"),
  status: H(["pending", "succeeded", "failed", "interrupted"]),
  createdAt: I().datetime(),
  updatedAt: I().datetime(),
  baseRevision: g().int().nonnegative(),
  targetRevision: g().int().positive(),
  sessionId: I().min(1),
  processId: g().int().positive(),
  evidenceDirectory: I().min(1),
  sessionPath: I().min(1).optional(),
  completedAt: I().datetime().optional(),
  error: I().optional()
});
R({
  version: D(1),
  baseProjectSha256: I().regex(/^[a-f0-9]{64}$/),
  baseRevision: g().int().nonnegative(),
  updatedAt: I().datetime(),
  label: I().trim().min(1).max(160).optional(),
  overrides: St
});
R({
  version: D(1),
  optional: D(!0),
  requests: O(R({
    id: I(),
    kind: H(["closed-eye", "mouth-shape"]),
    side: H(["left", "right", "center"]),
    variant: H(["closed", "slight", "open", "a", "i", "u", "e", "o"]).optional(),
    sourceLayerIds: O(I()),
    crop: On,
    reference: R({ path: I().min(1) }).optional(),
    output: R({ path: I(), width: g().int().positive(), height: g().int().positive(), transparent: D(!0) }),
    prompt: I(),
    constraints: O(I()),
    validation: R({ requireAlpha: D(!0), maxOpaqueCoverage: g(), minOpaqueCoverage: g() })
  }))
});
var Do = typeof globalThis < "u" ? globalThis : typeof window < "u" ? window : typeof global < "u" ? global : typeof self < "u" ? self : {}, sn = {};
const rl = "1.5.0", sl = {
  version: rl
};
var an, Fo;
function $n() {
  if (Fo) return an;
  Fo = 1;
  function e(i) {
    return "(" + i.x + ";" + i.y + ")";
  }
  function t(i) {
    var s = i.toString();
    return s === "[object Object]" ? e(i) : s;
  }
  function n(i, s) {
    return i.y === s.y ? i.x - s.x : i.y - s.y;
  }
  function o(i, s) {
    return i.x === s.x && i.y === s.y;
  }
  return an = {
    toString: t,
    toStringBase: e,
    compare: n,
    equals: o
  }, an;
}
var cn, Wo;
function An() {
  if (Wo) return cn;
  Wo = 1;
  var e = $n(), t = function(n, o) {
    this.name = "PointError", this.points = o = o || [], this.message = n || "Invalid Points!";
    for (var i = 0; i < o.length; i++)
      this.message += " " + e.toString(o[i]);
  };
  return t.prototype = new Error(), t.prototype.constructor = t, cn = t, cn;
}
var un, Bo;
function dr() {
  if (Bo) return un;
  Bo = 1;
  var e = $n(), t = function(n, o) {
    this.x = +n || 0, this.y = +o || 0, this._p2t_edge_list = null;
  };
  return t.prototype.toString = function() {
    return e.toStringBase(this);
  }, t.prototype.toJSON = function() {
    return { x: this.x, y: this.y };
  }, t.prototype.clone = function() {
    return new t(this.x, this.y);
  }, t.prototype.set_zero = function() {
    return this.x = 0, this.y = 0, this;
  }, t.prototype.set = function(n, o) {
    return this.x = +n || 0, this.y = +o || 0, this;
  }, t.prototype.negate = function() {
    return this.x = -this.x, this.y = -this.y, this;
  }, t.prototype.add = function(n) {
    return this.x += n.x, this.y += n.y, this;
  }, t.prototype.sub = function(n) {
    return this.x -= n.x, this.y -= n.y, this;
  }, t.prototype.mul = function(n) {
    return this.x *= n, this.y *= n, this;
  }, t.prototype.length = function() {
    return Math.sqrt(this.x * this.x + this.y * this.y);
  }, t.prototype.normalize = function() {
    var n = this.length();
    return this.x /= n, this.y /= n, n;
  }, t.prototype.equals = function(n) {
    return this.x === n.x && this.y === n.y;
  }, t.negate = function(n) {
    return new t(-n.x, -n.y);
  }, t.add = function(n, o) {
    return new t(n.x + o.x, n.y + o.y);
  }, t.sub = function(n, o) {
    return new t(n.x - o.x, n.y - o.y);
  }, t.mul = function(n, o) {
    return new t(n * o.x, n * o.y);
  }, t.cross = function(n, o) {
    return typeof n == "number" ? typeof o == "number" ? n * o : new t(-n * o.y, n * o.x) : typeof o == "number" ? new t(o * n.y, -o * n.x) : n.x * o.y - n.y * o.x;
  }, t.toString = e.toString, t.compare = e.compare, t.cmp = e.compare, t.equals = e.equals, t.dot = function(n, o) {
    return n.x * o.x + n.y * o.y;
  }, un = t, un;
}
var hn, Ho;
function Nn() {
  if (Ho) return hn;
  Ho = 1;
  var e = $n(), t = function(o, i, s) {
    this.points_ = [o, i, s], this.neighbors_ = [null, null, null], this.interior_ = !1, this.constrained_edge = [!1, !1, !1], this.delaunay_edge = [!1, !1, !1];
  }, n = e.toString;
  return t.prototype.toString = function() {
    return "[" + n(this.points_[0]) + n(this.points_[1]) + n(this.points_[2]) + "]";
  }, t.prototype.getPoint = function(o) {
    return this.points_[o];
  }, t.prototype.GetPoint = t.prototype.getPoint, t.prototype.getPoints = function() {
    return this.points_;
  }, t.prototype.getNeighbor = function(o) {
    return this.neighbors_[o];
  }, t.prototype.containsPoint = function(o) {
    var i = this.points_;
    return o === i[0] || o === i[1] || o === i[2];
  }, t.prototype.containsEdge = function(o) {
    return this.containsPoint(o.p) && this.containsPoint(o.q);
  }, t.prototype.containsPoints = function(o, i) {
    return this.containsPoint(o) && this.containsPoint(i);
  }, t.prototype.isInterior = function() {
    return this.interior_;
  }, t.prototype.setInterior = function(o) {
    return this.interior_ = o, this;
  }, t.prototype.markNeighborPointers = function(o, i, s) {
    var r = this.points_;
    if (o === r[2] && i === r[1] || o === r[1] && i === r[2])
      this.neighbors_[0] = s;
    else if (o === r[0] && i === r[2] || o === r[2] && i === r[0])
      this.neighbors_[1] = s;
    else if (o === r[0] && i === r[1] || o === r[1] && i === r[0])
      this.neighbors_[2] = s;
    else
      throw new Error("poly2tri Invalid Triangle.markNeighborPointers() call");
  }, t.prototype.markNeighbor = function(o) {
    var i = this.points_;
    o.containsPoints(i[1], i[2]) ? (this.neighbors_[0] = o, o.markNeighborPointers(i[1], i[2], this)) : o.containsPoints(i[0], i[2]) ? (this.neighbors_[1] = o, o.markNeighborPointers(i[0], i[2], this)) : o.containsPoints(i[0], i[1]) && (this.neighbors_[2] = o, o.markNeighborPointers(i[0], i[1], this));
  }, t.prototype.clearNeighbors = function() {
    this.neighbors_[0] = null, this.neighbors_[1] = null, this.neighbors_[2] = null;
  }, t.prototype.clearDelaunayEdges = function() {
    this.delaunay_edge[0] = !1, this.delaunay_edge[1] = !1, this.delaunay_edge[2] = !1;
  }, t.prototype.pointCW = function(o) {
    var i = this.points_;
    return o === i[0] ? i[2] : o === i[1] ? i[0] : o === i[2] ? i[1] : null;
  }, t.prototype.pointCCW = function(o) {
    var i = this.points_;
    return o === i[0] ? i[1] : o === i[1] ? i[2] : o === i[2] ? i[0] : null;
  }, t.prototype.neighborCW = function(o) {
    return o === this.points_[0] ? this.neighbors_[1] : o === this.points_[1] ? this.neighbors_[2] : this.neighbors_[0];
  }, t.prototype.neighborCCW = function(o) {
    return o === this.points_[0] ? this.neighbors_[2] : o === this.points_[1] ? this.neighbors_[0] : this.neighbors_[1];
  }, t.prototype.getConstrainedEdgeCW = function(o) {
    return o === this.points_[0] ? this.constrained_edge[1] : o === this.points_[1] ? this.constrained_edge[2] : this.constrained_edge[0];
  }, t.prototype.getConstrainedEdgeCCW = function(o) {
    return o === this.points_[0] ? this.constrained_edge[2] : o === this.points_[1] ? this.constrained_edge[0] : this.constrained_edge[1];
  }, t.prototype.getConstrainedEdgeAcross = function(o) {
    return o === this.points_[0] ? this.constrained_edge[0] : o === this.points_[1] ? this.constrained_edge[1] : this.constrained_edge[2];
  }, t.prototype.setConstrainedEdgeCW = function(o, i) {
    o === this.points_[0] ? this.constrained_edge[1] = i : o === this.points_[1] ? this.constrained_edge[2] = i : this.constrained_edge[0] = i;
  }, t.prototype.setConstrainedEdgeCCW = function(o, i) {
    o === this.points_[0] ? this.constrained_edge[2] = i : o === this.points_[1] ? this.constrained_edge[0] = i : this.constrained_edge[1] = i;
  }, t.prototype.getDelaunayEdgeCW = function(o) {
    return o === this.points_[0] ? this.delaunay_edge[1] : o === this.points_[1] ? this.delaunay_edge[2] : this.delaunay_edge[0];
  }, t.prototype.getDelaunayEdgeCCW = function(o) {
    return o === this.points_[0] ? this.delaunay_edge[2] : o === this.points_[1] ? this.delaunay_edge[0] : this.delaunay_edge[1];
  }, t.prototype.setDelaunayEdgeCW = function(o, i) {
    o === this.points_[0] ? this.delaunay_edge[1] = i : o === this.points_[1] ? this.delaunay_edge[2] = i : this.delaunay_edge[0] = i;
  }, t.prototype.setDelaunayEdgeCCW = function(o, i) {
    o === this.points_[0] ? this.delaunay_edge[2] = i : o === this.points_[1] ? this.delaunay_edge[0] = i : this.delaunay_edge[1] = i;
  }, t.prototype.neighborAcross = function(o) {
    return o === this.points_[0] ? this.neighbors_[0] : o === this.points_[1] ? this.neighbors_[1] : this.neighbors_[2];
  }, t.prototype.oppositePoint = function(o, i) {
    var s = o.pointCW(i);
    return this.pointCW(s);
  }, t.prototype.legalize = function(o, i) {
    var s = this.points_;
    if (o === s[0])
      s[1] = s[0], s[0] = s[2], s[2] = i;
    else if (o === s[1])
      s[2] = s[1], s[1] = s[0], s[0] = i;
    else if (o === s[2])
      s[0] = s[2], s[2] = s[1], s[1] = i;
    else
      throw new Error("poly2tri Invalid Triangle.legalize() call");
  }, t.prototype.index = function(o) {
    var i = this.points_;
    if (o === i[0])
      return 0;
    if (o === i[1])
      return 1;
    if (o === i[2])
      return 2;
    throw new Error("poly2tri Invalid Triangle.index() call");
  }, t.prototype.edgeIndex = function(o, i) {
    var s = this.points_;
    if (o === s[0]) {
      if (i === s[1])
        return 2;
      if (i === s[2])
        return 1;
    } else if (o === s[1]) {
      if (i === s[2])
        return 0;
      if (i === s[0])
        return 2;
    } else if (o === s[2]) {
      if (i === s[0])
        return 1;
      if (i === s[1])
        return 0;
    }
    return -1;
  }, t.prototype.markConstrainedEdgeByIndex = function(o) {
    this.constrained_edge[o] = !0;
  }, t.prototype.markConstrainedEdgeByEdge = function(o) {
    this.markConstrainedEdgeByPoints(o.p, o.q);
  }, t.prototype.markConstrainedEdgeByPoints = function(o, i) {
    var s = this.points_;
    i === s[0] && o === s[1] || i === s[1] && o === s[0] ? this.constrained_edge[2] = !0 : i === s[0] && o === s[2] || i === s[2] && o === s[0] ? this.constrained_edge[1] = !0 : (i === s[1] && o === s[2] || i === s[2] && o === s[1]) && (this.constrained_edge[0] = !0);
  }, hn = t, hn;
}
var dn = {}, ln, Yo;
function al() {
  if (Yo) return ln;
  Yo = 1;
  function e(t, n) {
    if (!t)
      throw new Error(n || "Assert Failed");
  }
  return ln = e, ln;
}
var Nt = { exports: {} }, Xo;
function lr() {
  if (Xo) return Nt.exports;
  Xo = 1;
  var e = function(n, o) {
    this.point = n, this.triangle = o || null, this.next = null, this.prev = null, this.value = n.x;
  }, t = function(n, o) {
    this.head_ = n, this.tail_ = o, this.search_node_ = n;
  };
  return t.prototype.head = function() {
    return this.head_;
  }, t.prototype.setHead = function(n) {
    this.head_ = n;
  }, t.prototype.tail = function() {
    return this.tail_;
  }, t.prototype.setTail = function(n) {
    this.tail_ = n;
  }, t.prototype.search = function() {
    return this.search_node_;
  }, t.prototype.setSearch = function(n) {
    this.search_node_ = n;
  }, t.prototype.findSearchNode = function() {
    return this.search_node_;
  }, t.prototype.locateNode = function(n) {
    var o = this.search_node_;
    if (n < o.value) {
      for (; o = o.prev; )
        if (n >= o.value)
          return this.search_node_ = o, o;
    } else
      for (; o = o.next; )
        if (n < o.value)
          return this.search_node_ = o.prev, o.prev;
    return null;
  }, t.prototype.locatePoint = function(n) {
    var o = n.x, i = this.findSearchNode(o), s = i.point.x;
    if (o === s) {
      if (n !== i.point)
        if (n === i.prev.point)
          i = i.prev;
        else if (n === i.next.point)
          i = i.next;
        else
          throw new Error("poly2tri Invalid AdvancingFront.locatePoint() call");
    } else if (o < s)
      for (; (i = i.prev) && n !== i.point; )
        ;
    else
      for (; (i = i.next) && n !== i.point; )
        ;
    return i && (this.search_node_ = i), i;
  }, Nt.exports = t, Nt.exports.Node = e, Nt.exports;
}
var Ke = {}, Uo;
function cl() {
  if (Uo) return Ke;
  Uo = 1;
  var e = 1e-12;
  Ke.EPSILON = e;
  var t = {
    CW: 1,
    CCW: -1,
    COLLINEAR: 0
  };
  Ke.Orientation = t;
  function n(s, r, a) {
    var c = (s.x - a.x) * (r.y - a.y), u = (s.y - a.y) * (r.x - a.x), h = c - u;
    return h > -e && h < e ? t.COLLINEAR : h > 0 ? t.CCW : t.CW;
  }
  Ke.orient2d = n;
  function o(s, r, a, c) {
    var u = (s.x - r.x) * (c.y - r.y) - (c.x - r.x) * (s.y - r.y);
    if (u >= -e)
      return !1;
    var h = (s.x - a.x) * (c.y - a.y) - (c.x - a.x) * (s.y - a.y);
    return !(h <= e);
  }
  Ke.inScanArea = o;
  function i(s, r, a) {
    var c = r.x - s.x, u = r.y - s.y, h = a.x - s.x, d = a.y - s.y;
    return c * h + u * d < 0;
  }
  return Ke.isAngleObtuse = i, Ke;
}
var jo;
function fr() {
  if (jo) return dn;
  jo = 1;
  var e = al(), t = An(), n = Nn(), o = lr().Node, i = cl(), s = i.EPSILON, r = i.Orientation, a = i.orient2d, c = i.inScanArea, u = i.isAngleObtuse;
  function h(b) {
    b.initTriangulation(), b.createAdvancingFront(), d(b), f(b);
  }
  function d(b) {
    var v, y = b.pointCount();
    for (v = 1; v < y; ++v)
      for (var E = b.getPoint(v), N = m(b, E), Y = E._p2t_edge_list, W = 0; Y && W < Y.length; ++W)
        l(b, Y[W], N);
  }
  function f(b) {
    for (var v = b.front().head().next.triangle, y = b.front().head().next.point; !v.getConstrainedEdgeCW(y); )
      v = v.neighborCCW(y);
    b.meshClean(v);
  }
  function m(b, v) {
    var y = b.locateNode(v), E = k(b, v, y);
    return v.x <= y.point.x + s && w(b, y), P(b, E), E;
  }
  function l(b, v, y) {
    b.edge_event.constrained_edge = v, b.edge_event.right = v.p.x > v.q.x, !x(y.triangle, v.p, v.q) && (F(b, v, y), p(b, v.p, v.q, y.triangle, v.q));
  }
  function p(b, v, y, E, N) {
    if (!x(E, v, y)) {
      var Y = E.pointCCW(N), W = a(y, Y, v);
      if (W === r.COLLINEAR)
        throw new t("poly2tri EdgeEvent: Collinear not supported!", [y, Y, v]);
      var q = E.pointCW(N), te = a(y, q, v);
      if (te === r.COLLINEAR)
        throw new t("poly2tri EdgeEvent: Collinear not supported!", [y, q, v]);
      W === te ? (W === r.CW ? E = E.neighborCCW(N) : E = E.neighborCW(N), p(b, v, y, E, N)) : be(b, v, y, E, N);
    }
  }
  function x(b, v, y) {
    var E = b.edgeIndex(v, y);
    if (E !== -1) {
      b.markConstrainedEdgeByIndex(E);
      var N = b.getNeighbor(E);
      return N && N.markConstrainedEdgeByPoints(v, y), !0;
    }
    return !1;
  }
  function k(b, v, y) {
    var E = new n(v, y.point, y.next.point);
    E.markNeighbor(y.triangle), b.addToMap(E);
    var N = new o(v);
    return N.next = y.next, N.prev = y, y.next.prev = N, y.next = N, _(b, E) || b.mapTriangleToNodes(E), N;
  }
  function w(b, v) {
    var y = new n(v.prev.point, v.point, v.next.point);
    y.markNeighbor(v.prev.triangle), y.markNeighbor(v.triangle), b.addToMap(y), v.prev.next = v.next, v.next.prev = v.prev, _(b, y) || b.mapTriangleToNodes(y);
  }
  function P(b, v) {
    for (var y = v.next; y.next && !u(y.point, y.next.point, y.prev.point); )
      w(b, y), y = y.next;
    for (y = v.prev; y.prev && !u(y.point, y.next.point, y.prev.point); )
      w(b, y), y = y.prev;
    v.next && v.next.next && M(v) && z(b, v);
  }
  function M(b) {
    var v = b.point.x - b.next.next.point.x, y = b.point.y - b.next.next.point.y;
    return e(y >= 0, "unordered y"), v >= 0 || Math.abs(v) < y;
  }
  function _(b, v) {
    for (var y = 0; y < 3; ++y)
      if (!v.delaunay_edge[y]) {
        var E = v.getNeighbor(y);
        if (E) {
          var N = v.getPoint(y), Y = E.oppositePoint(v, N), W = E.index(Y);
          if (E.constrained_edge[W] || E.delaunay_edge[W]) {
            v.constrained_edge[y] = E.constrained_edge[W];
            continue;
          }
          var q = B(N, v.pointCCW(N), v.pointCW(N), Y);
          if (q) {
            v.delaunay_edge[y] = !0, E.delaunay_edge[W] = !0, $(v, N, E, Y);
            var te = !_(b, v);
            return te && b.mapTriangleToNodes(v), te = !_(b, E), te && b.mapTriangleToNodes(E), v.delaunay_edge[y] = !1, E.delaunay_edge[W] = !1, !0;
          }
        }
      }
    return !1;
  }
  function B(b, v, y, E) {
    var N = b.x - E.x, Y = b.y - E.y, W = v.x - E.x, q = v.y - E.y, te = N * q, _e = W * Y, Ge = te - _e;
    if (Ge <= 0)
      return !1;
    var Ee = y.x - E.x, ze = y.y - E.y, Ce = Ee * Y, Te = N * ze, Le = Ce - Te;
    if (Le <= 0)
      return !1;
    var ft = W * ze, zt = Ee * q, pt = N * N + Y * Y, mt = W * W + q * q, Ct = Ee * Ee + ze * ze, gt = pt * (ft - zt) + mt * Le + Ct * Ge;
    return gt > 0;
  }
  function $(b, v, y, E) {
    var N, Y, W, q;
    N = b.neighborCCW(v), Y = b.neighborCW(v), W = y.neighborCCW(E), q = y.neighborCW(E);
    var te, _e, Ge, Ee;
    te = b.getConstrainedEdgeCCW(v), _e = b.getConstrainedEdgeCW(v), Ge = y.getConstrainedEdgeCCW(E), Ee = y.getConstrainedEdgeCW(E);
    var ze, Ce, Te, Le;
    ze = b.getDelaunayEdgeCCW(v), Ce = b.getDelaunayEdgeCW(v), Te = y.getDelaunayEdgeCCW(E), Le = y.getDelaunayEdgeCW(E), b.legalize(v, E), y.legalize(E, v), y.setDelaunayEdgeCCW(v, ze), b.setDelaunayEdgeCW(v, Ce), b.setDelaunayEdgeCCW(E, Te), y.setDelaunayEdgeCW(E, Le), y.setConstrainedEdgeCCW(v, te), b.setConstrainedEdgeCW(v, _e), b.setConstrainedEdgeCCW(E, Ge), y.setConstrainedEdgeCW(E, Ee), b.clearNeighbors(), y.clearNeighbors(), N && y.markNeighbor(N), Y && b.markNeighbor(Y), W && b.markNeighbor(W), q && y.markNeighbor(q), b.markNeighbor(y);
  }
  function z(b, v) {
    for (a(v.point, v.next.point, v.next.next.point) === r.CCW ? b.basin.left_node = v.next.next : b.basin.left_node = v.next, b.basin.bottom_node = b.basin.left_node; b.basin.bottom_node.next && b.basin.bottom_node.point.y >= b.basin.bottom_node.next.point.y; )
      b.basin.bottom_node = b.basin.bottom_node.next;
    if (b.basin.bottom_node !== b.basin.left_node) {
      for (b.basin.right_node = b.basin.bottom_node; b.basin.right_node.next && b.basin.right_node.point.y < b.basin.right_node.next.point.y; )
        b.basin.right_node = b.basin.right_node.next;
      b.basin.right_node !== b.basin.bottom_node && (b.basin.width = b.basin.right_node.point.x - b.basin.left_node.point.x, b.basin.left_highest = b.basin.left_node.point.y > b.basin.right_node.point.y, A(b, b.basin.bottom_node));
    }
  }
  function A(b, v) {
    if (!C(b, v)) {
      w(b, v);
      var y;
      if (!(v.prev === b.basin.left_node && v.next === b.basin.right_node)) {
        if (v.prev === b.basin.left_node) {
          if (y = a(v.point, v.next.point, v.next.next.point), y === r.CW)
            return;
          v = v.next;
        } else if (v.next === b.basin.right_node) {
          if (y = a(v.point, v.prev.point, v.prev.prev.point), y === r.CCW)
            return;
          v = v.prev;
        } else
          v.prev.point.y < v.next.point.y ? v = v.prev : v = v.next;
        A(b, v);
      }
    }
  }
  function C(b, v) {
    var y;
    return b.basin.left_highest ? y = b.basin.left_node.point.y - v.point.y : y = b.basin.right_node.point.y - v.point.y, b.basin.width > y;
  }
  function F(b, v, y) {
    b.edge_event.right ? X(b, v, y) : ie(b, v, y);
  }
  function X(b, v, y) {
    for (; y.next.point.x < v.p.x; )
      a(v.q, y.next.point, v.p) === r.CCW ? j(b, v, y) : y = y.next;
  }
  function j(b, v, y) {
    y.point.x < v.p.x && (a(y.point, y.next.point, y.next.next.point) === r.CCW ? V(b, v, y) : (se(b, v, y), j(b, v, y)));
  }
  function V(b, v, y) {
    w(b, y.next), y.next.point !== v.p && a(v.q, y.next.point, v.p) === r.CCW && a(y.point, y.next.point, y.next.next.point) === r.CCW && V(b, v, y);
  }
  function se(b, v, y) {
    a(y.next.point, y.next.next.point, y.next.next.next.point) === r.CCW ? V(b, v, y.next) : a(v.q, y.next.next.point, v.p) === r.CCW && se(b, v, y.next);
  }
  function ie(b, v, y) {
    for (; y.prev.point.x > v.p.x; )
      a(v.q, y.prev.point, v.p) === r.CW ? he(b, v, y) : y = y.prev;
  }
  function he(b, v, y) {
    y.point.x > v.p.x && (a(y.point, y.prev.point, y.prev.prev.point) === r.CW ? de(b, v, y) : (fe(b, v, y), he(b, v, y)));
  }
  function fe(b, v, y) {
    a(y.prev.point, y.prev.prev.point, y.prev.prev.prev.point) === r.CW ? de(b, v, y.prev) : a(v.q, y.prev.prev.point, v.p) === r.CW && fe(b, v, y.prev);
  }
  function de(b, v, y) {
    w(b, y.prev), y.prev.point !== v.p && a(v.q, y.prev.point, v.p) === r.CW && a(y.point, y.prev.point, y.prev.prev.point) === r.CW && de(b, v, y);
  }
  function be(b, v, y, E, N) {
    var Y = E.neighborAcross(N);
    e(Y, "FLIP failed due to missing triangle!");
    var W = Y.oppositePoint(E, N);
    if (E.getConstrainedEdgeAcross(N)) {
      var q = E.index(N);
      throw new t(
        "poly2tri Intersecting Constraints",
        [N, W, E.getPoint((q + 1) % 3), E.getPoint((q + 2) % 3)]
      );
    }
    if (c(N, E.pointCCW(N), E.pointCW(N), W))
      if ($(E, N, Y, W), b.mapTriangleToNodes(E), b.mapTriangleToNodes(Y), N === y && W === v)
        y === b.edge_event.constrained_edge.q && v === b.edge_event.constrained_edge.p && (E.markConstrainedEdgeByPoints(v, y), Y.markConstrainedEdgeByPoints(v, y), _(b, E), _(b, Y));
      else {
        var te = a(y, W, v);
        E = nt(b, te, E, Y, N, W), be(b, v, y, E, N);
      }
    else {
      var _e = Be(v, y, Y, W);
      ce(b, v, y, E, Y, _e), p(b, v, y, E, N);
    }
  }
  function nt(b, v, y, E, N, Y) {
    var W;
    return v === r.CCW ? (W = E.edgeIndex(N, Y), E.delaunay_edge[W] = !0, _(b, E), E.clearDelaunayEdges(), y) : (W = y.edgeIndex(N, Y), y.delaunay_edge[W] = !0, _(b, y), y.clearDelaunayEdges(), E);
  }
  function Be(b, v, y, E) {
    var N = a(v, E, b);
    if (N === r.CW)
      return y.pointCCW(E);
    if (N === r.CCW)
      return y.pointCW(E);
    throw new t("poly2tri [Unsupported] nextFlipPoint: opposing point on constrained edge!", [v, E, b]);
  }
  function ce(b, v, y, E, N, Y) {
    var W = N.neighborAcross(Y);
    e(W, "FLIP failed due to missing triangle");
    var q = W.oppositePoint(N, Y);
    if (c(y, E.pointCCW(y), E.pointCW(y), q))
      be(b, y, q, W, q);
    else {
      var te = Be(v, y, W, q);
      ce(b, v, y, E, W, te);
    }
  }
  return dn.triangulate = h, dn;
}
var fn, Vo;
function ul() {
  if (Vo) return fn;
  Vo = 1;
  var e = An(), t = dr(), n = Nn(), o = fr(), i = lr(), s = i.Node, r = 0.3, a = function(d, f) {
    if (this.p = d, this.q = f, d.y > f.y)
      this.q = d, this.p = f;
    else if (d.y === f.y) {
      if (d.x > f.x)
        this.q = d, this.p = f;
      else if (d.x === f.x)
        throw new e("poly2tri Invalid Edge constructor: repeated points!", [d]);
    }
    this.q._p2t_edge_list || (this.q._p2t_edge_list = []), this.q._p2t_edge_list.push(this);
  }, c = function() {
    this.left_node = null, this.bottom_node = null, this.right_node = null, this.width = 0, this.left_highest = !1;
  };
  c.prototype.clear = function() {
    this.left_node = null, this.bottom_node = null, this.right_node = null, this.width = 0, this.left_highest = !1;
  };
  var u = function() {
    this.constrained_edge = null, this.right = !1;
  }, h = function(d, f) {
    f = f || {}, this.triangles_ = [], this.map_ = [], this.points_ = f.cloneArrays ? d.slice(0) : d, this.edge_list = [], this.pmin_ = this.pmax_ = null, this.front_ = null, this.head_ = null, this.tail_ = null, this.af_head_ = null, this.af_middle_ = null, this.af_tail_ = null, this.basin = new c(), this.edge_event = new u(), this.initEdges(this.points_);
  };
  return h.prototype.addHole = function(d) {
    this.initEdges(d);
    var f, m = d.length;
    for (f = 0; f < m; f++)
      this.points_.push(d[f]);
    return this;
  }, h.prototype.AddHole = h.prototype.addHole, h.prototype.addHoles = function(d) {
    var f, m = d.length;
    for (f = 0; f < m; f++)
      this.initEdges(d[f]);
    return this.points_ = this.points_.concat.apply(this.points_, d), this;
  }, h.prototype.addPoint = function(d) {
    return this.points_.push(d), this;
  }, h.prototype.AddPoint = h.prototype.addPoint, h.prototype.addPoints = function(d) {
    return this.points_ = this.points_.concat(d), this;
  }, h.prototype.triangulate = function() {
    return o.triangulate(this), this;
  }, h.prototype.getBoundingBox = function() {
    return { min: this.pmin_, max: this.pmax_ };
  }, h.prototype.getTriangles = function() {
    return this.triangles_;
  }, h.prototype.GetTriangles = h.prototype.getTriangles, h.prototype.front = function() {
    return this.front_;
  }, h.prototype.pointCount = function() {
    return this.points_.length;
  }, h.prototype.head = function() {
    return this.head_;
  }, h.prototype.setHead = function(d) {
    this.head_ = d;
  }, h.prototype.tail = function() {
    return this.tail_;
  }, h.prototype.setTail = function(d) {
    this.tail_ = d;
  }, h.prototype.getMap = function() {
    return this.map_;
  }, h.prototype.initTriangulation = function() {
    var d = this.points_[0].x, f = this.points_[0].x, m = this.points_[0].y, l = this.points_[0].y, p, x = this.points_.length;
    for (p = 1; p < x; p++) {
      var k = this.points_[p];
      k.x > d && (d = k.x), k.x < f && (f = k.x), k.y > m && (m = k.y), k.y < l && (l = k.y);
    }
    this.pmin_ = new t(f, l), this.pmax_ = new t(d, m);
    var w = r * (d - f), P = r * (m - l);
    this.head_ = new t(d + w, l - P), this.tail_ = new t(f - w, l - P), this.points_.sort(t.compare);
  }, h.prototype.initEdges = function(d) {
    var f, m = d.length;
    for (f = 0; f < m; ++f)
      this.edge_list.push(new a(d[f], d[(f + 1) % m]));
  }, h.prototype.getPoint = function(d) {
    return this.points_[d];
  }, h.prototype.addToMap = function(d) {
    this.map_.push(d);
  }, h.prototype.locateNode = function(d) {
    return this.front_.locateNode(d.x);
  }, h.prototype.createAdvancingFront = function() {
    var d, f, m, l = new n(this.points_[0], this.tail_, this.head_);
    this.map_.push(l), d = new s(l.getPoint(1), l), f = new s(l.getPoint(0), l), m = new s(l.getPoint(2)), this.front_ = new i(d, m), d.next = f, f.next = m, f.prev = d, m.prev = f;
  }, h.prototype.removeNode = function(d) {
  }, h.prototype.mapTriangleToNodes = function(d) {
    for (var f = 0; f < 3; ++f)
      if (!d.getNeighbor(f)) {
        var m = this.front_.locatePoint(d.pointCW(d.getPoint(f)));
        m && (m.triangle = d);
      }
  }, h.prototype.removeFromMap = function(d) {
    var f, m = this.map_, l = m.length;
    for (f = 0; f < l; f++)
      if (m[f] === d) {
        m.splice(f, 1);
        break;
      }
  }, h.prototype.meshClean = function(d) {
    for (var f = [d], m, l; m = f.pop(); )
      if (!m.isInterior())
        for (m.setInterior(!0), this.triangles_.push(m), l = 0; l < 3; l++)
          m.constrained_edge[l] || f.push(m.getNeighbor(l));
  }, fn = h, fn;
}
var Go;
function hl() {
  return Go || (Go = 1, (function(e) {
    var t = Do.poly2tri;
    e.noConflict = function() {
      return Do.poly2tri = t, e;
    }, e.VERSION = sl.version, e.PointError = An(), e.Point = dr(), e.Triangle = Nn(), e.SweepContext = ul();
    var n = fr();
    e.triangulate = n.triangulate, e.sweep = { Triangulate: n.triangulate };
  })(sn)), sn;
}
hl();
const dl = {
  kind: "semantic-occlusion-v1",
  fadeStart: 0.58,
  farEyeOpacity: 1,
  farBrowOpacity: 1,
  farEarOpacity: 0.55,
  farSideHairOpacity: 0.72,
  sideHairDepthSwap: !0
};
function ll(e) {
  const t = Math.max(0, Math.min(1, e));
  return t * t * (3 - 2 * t);
}
function pr(e) {
  return e.side === "left" ? 1 : e.side === "right" ? -1 : 0;
}
function fl(e, t) {
  return Math.max(0, Math.max(-1, Math.min(1, t)) * pr(e));
}
function mr(e) {
  if (e.runtime.poseField)
    return e.runtime.poseOcclusion ?? dl;
}
function pl(e) {
  return e.role === "eyeWhite" || e.role === "iris" || e.role === "eyelash" || e.role === "eyeClosed" || e.role === "eyebrow";
}
function ml(e, t, n) {
  if (pl(t))
    return 1;
  const o = mr(e);
  if (!o || t.side === "center")
    return 1;
  const i = fl(t, n.headYaw), s = ll((i - o.fadeStart) / Math.max(1e-6, 1 - o.fadeStart));
  return s <= 0 ? 1 : 1 + ((t.role === "ear" ? o.farEarOpacity : t.role === "sideHair" ? o.farSideHairOpacity : 1) - 1) * s;
}
function gl(e, t, n, o, i) {
  if (!mr(e)?.sideHairDepthSwap || i === void 0 || t.side === "center" || t.role !== "sideHair" && t.role !== "ear")
    return o;
  const r = pr(t), a = Math.max(-1, Math.min(1, n.headYaw)), c = Math.max(0, -a * r), u = Math.max(0, a * r), h = Math.max(0.4, Math.min(1.6, e.runtime.poseField?.depthStrength ?? 1));
  return t.role === "ear" ? i - 0.35 - u * 0.4 * h + c * 0.08 * h : Math.max(c, u) < 0.08 ? o : i + c * 0.35 * h - u * 0.45 * h;
}
const kn = /* @__PURE__ */ new Set(["eyeWhite", "iris", "eyelash"]), Jo = /* @__PURE__ */ new WeakMap();
function yl(e) {
  const t = Jo.get(e);
  if (t)
    return t;
  const n = (i) => /* @__PURE__ */ new Set([`param-${i}`, ...(e.model?.parameters ?? []).filter((s) => s.semantic === i).map((s) => s.id)]), o = {
    blinkLeftParameterIds: n("blink-left"),
    blinkRightParameterIds: n("blink-right")
  };
  return Jo.set(e, o), o;
}
function vl(e, t) {
  const n = Cr(e, t), o = Yr(e, Mr(e, n)), i = (m, l) => {
    if (n[l] !== void 0)
      return !0;
    for (const w of m)
      if (n.parameters?.[w] !== void 0)
        return !0;
    const p = n.expressions ? new Set(Object.entries(n.expressions).filter(([, w]) => w !== 0).map(([w]) => w)) : void 0, x = e.model?.expressions ?? [];
    if (p && x.some((w) => p.has(w.id) && [...m].some((P) => w.parameters[P] !== void 0)))
      return !0;
    const k = e.model?.behaviors?.find((w) => w.id === n.behavior?.id);
    return k ? k.tracks.some((w) => w.target.kind === "parameter" ? m.has(w.target.id) : x.some((P) => P.id === w.target.id && [...m].some((M) => P.parameters[M] !== void 0))) : !1;
  }, { blinkLeftParameterIds: s, blinkRightParameterIds: r } = yl(e), a = i(s, "blinkLeft"), c = i(r, "blinkRight"), u = e.runtime.features.blink ? o.blink : 0, h = e.runtime.features.blink ? e.runtime.features.asymmetricBlink && a ? o.blinkLeft ?? u : u : 0, d = e.runtime.features.blink ? e.runtime.features.asymmetricBlink && c ? o.blinkRight ?? u : u : 0, f = { ...o.parameters ?? {} };
  for (const m of s)
    f[m] !== void 0 && (f[m] = h);
  for (const m of r)
    f[m] !== void 0 && (f[m] = d);
  return {
    ...o,
    parameters: f,
    blink: u,
    blinkLeft: h,
    blinkRight: d,
    mouthOpen: e.runtime.features.mouthMotion ? o.mouthOpen : 0,
    mouthA: e.runtime.features.visemes ? o.mouthA ?? 0 : 0,
    mouthI: e.runtime.features.visemes ? o.mouthI ?? 0 : 0,
    mouthU: e.runtime.features.visemes ? o.mouthU ?? 0 : 0,
    mouthE: e.runtime.features.visemes ? o.mouthE ?? 0 : 0,
    mouthO: e.runtime.features.visemes ? o.mouthO ?? 0 : 0
  };
}
function qo(e) {
  return e.role === "eyeWhite" ? 0 : e.role === "iris" ? 1 : 2;
}
function Zt(e) {
  const t = Math.max(0, Math.min(1, e));
  return t * t * (3 - 2 * t);
}
function xl(e, t) {
  if (e.blinkMode === "geometry" && (kn.has(e.role) || e.role === "eyeClosed"))
    return e.role === "eyeClosed" ? 0 : e.opacity;
  const n = e.side === "left" ? t.blinkLeft ?? t.blink : e.side === "right" ? t.blinkRight ?? t.blink : t.blink;
  if (e.role === "eyeClosed")
    return (e.opacity === 0 ? 1 : e.opacity) * n;
  if (e.role === "eyeWhite" || e.role === "iris" || e.role === "eyelash")
    return e.opacity * (1 - n);
  if (e.role !== "mouth")
    return e.opacity;
  const o = Math.max(0, Math.min(1, t.mouthOpen)), i = e.mouthVariant ?? "closed";
  return i === "a" ? e.opacity * Math.max(0, Math.min(1, t.mouthA ?? 0)) : i === "i" ? e.opacity * Math.max(0, Math.min(1, t.mouthI ?? 0)) : i === "u" ? e.opacity * Math.max(0, Math.min(1, t.mouthU ?? 0)) : i === "e" ? e.opacity * Math.max(0, Math.min(1, t.mouthE ?? 0)) : i === "o" ? e.opacity * Math.max(0, Math.min(1, t.mouthO ?? 0)) : i === "closed" ? e.opacity * (1 - Zt(o / 0.42)) : i === "slight" ? e.opacity * Zt(o / 0.42) * (1 - Zt((o - 0.5) / 0.38)) : e.opacity * Zt((o - 0.42) / 0.58);
}
function bl(e, t, n) {
  if (t.role === "mouth" && (t.mouthVariant === "closed" || t.mouthVariant === "open")) {
    const o = _l(e);
    if (o.has("closed") && o.has("open") && !o.has("slight")) {
      const i = n.mouthOpen >= 0.5;
      return t.opacity * (t.mouthVariant === "open" ? Number(i) : +!i);
    }
  }
  return xl(t, n);
}
const Ko = /* @__PURE__ */ new WeakMap();
function _l(e) {
  const t = Ko.get(e);
  if (t)
    return t;
  const n = new Set(e.layers.filter((o) => o.role === "mouth" && o.visible !== !1).map((o) => o.mouthVariant ?? "closed"));
  return Ko.set(e, n), n;
}
function kl(e, t, n) {
  const o = n?.inputState === t && n.project.model === e.model && n.project.runtime === e.runtime, i = n?.project === e, s = o ? n.frame.state : vl(e, t), r = i ? n.frame.authoringByLayerId : /* @__PURE__ */ new Map();
  for (const l of e.layers) {
    const p = i ? r.get(l.id) : void 0;
    r.set(l.id, Vr(e, l, s, p));
  }
  const a = (l) => l.order + (r.get(l.id)?.drawOrderOffset ?? 0), c = e.layers.find((l) => l.role === "face"), u = c ? a(c) : void 0, h = (l) => gl(e, l, s, a(l), u), d = e.layers.filter((l) => l.visible !== !1 && zr(e, l.id, s)).sort((l, p) => h(l) - h(p)), f = d.map((l, p) => kn.has(l.role) ? p : -1).filter((l) => l >= 0), m = d.filter((l) => kn.has(l.role)).sort((l, p) => qo(l) - qo(p) || l.side.localeCompare(p.side) || h(l) - h(p));
  return f.forEach((l, p) => {
    d[l] = m[p];
  }), {
    state: s,
    authoringByLayerId: r,
    layers: d.map((l) => {
      const p = r.get(l.id), x = Math.max(0, Math.min(1, bl(e, l, s) * p.opacityMultiplier * ml(e, l, s)));
      return { layer: l, authoring: p, opacity: x };
    })
  };
}
function wl(e) {
  const t = e.toLowerCase().replace(/[-_]/g, " ").trim();
  return t === "multiply" || t === "screen" || t === "darken" || t === "lighten" ? t : t === "linear dodge" || t === "add" || t === "lighter color" ? "add" : "normal";
}
const Qo = { sources: [], motion: {}, parameters: {}, expressions: {} }, Il = {}, Pl = /* @__PURE__ */ new Set([
  "blink",
  "mouthOpen",
  "blinkLeft",
  "blinkRight",
  "smile",
  "cheekPuff",
  "mouthA",
  "mouthI",
  "mouthU",
  "mouthE",
  "mouthO",
  "handLeftOpen",
  "handRightOpen"
]);
function at(e, t, n) {
  return e + (t - e) * Math.max(0, Math.min(1, n));
}
function Sl(e, t) {
  return e ? e.sources.filter((n) => n.expiresAtMs === void 0 || n.expiresAtMs > t).sort((n, o) => n.priority - o.priority || n.updatedAtMs - o.updatedAtMs || n.id.localeCompare(o.id)) : [];
}
function Ml(e, t = Date.now()) {
  if (!e || e.sources.length === 0)
    return Qo;
  const n = Sl(e, t);
  if (n.length === 0)
    return Qo;
  const o = {}, i = {}, s = {};
  let r, a;
  for (const c of n) {
    const u = Math.max(0, Math.min(1, c.blend));
    for (const [h, d] of Object.entries(c.motion ?? {}))
      o[h] = at(o[h] ?? 0, d, u);
    for (const [h, d] of Object.entries(c.parameters ?? {}))
      i[h] = at(i[h] ?? 0, d, u);
    for (const [h, d] of Object.entries(c.expressions ?? {}))
      s[h] = at(s[h] ?? 0, d, u);
    c.behavior && (r = { id: c.behavior.id, timeSeconds: Math.max(0, (t - c.behavior.startedAtMs) / 1e3), weight: u }), c.characterState && (a = structuredClone(c.characterState));
  }
  return { sources: n, motion: o, parameters: i, expressions: s, ...r ? { behavior: r } : {}, ...a ? { characterState: a } : {} };
}
function Rl(e, t, n) {
  let o = e;
  for (const s of n.sources) {
    const r = s.motion?.[t];
    r !== void 0 && (o = at(o, r, s.blend));
  }
  const i = Pl.has(t) ? 0 : -1;
  return Math.max(i, Math.min(1, o));
}
function El(e, t) {
  if (!e.model || t.sources.length === 0 && !t.behavior && !t.characterState)
    return Il;
  const n = {};
  for (const i of e.model.parameters) {
    let s = i.default, r = !1;
    for (const a of t.sources) {
      const c = a.parameters?.[i.id];
      c !== void 0 && (s = at(s, c, a.blend), r = !0);
    }
    r && (n[i.id] = Math.max(i.min, Math.min(i.max, s)));
  }
  const o = {};
  for (const i of e.model.expressions) {
    let s = 0, r = !1;
    for (const a of t.sources) {
      const c = a.expressions?.[i.id];
      c !== void 0 && (s = at(s, c, a.blend), r = !0);
    }
    r && (o[i.id] = Math.max(0, Math.min(1, s)));
  }
  return {
    ...Object.keys(n).length > 0 ? { parameters: n } : {},
    ...Object.keys(o).length > 0 ? { expressions: o } : {},
    ...t.behavior ? { behavior: t.behavior } : {},
    ...t.characterState ? { characterState: t.characterState } : {}
  };
}
function vt(e, t, n) {
  return Math.max(t, Math.min(n, e));
}
class we {
  particles;
  profile;
  constructor(t) {
    this.profile = t, this.particles = Array.from({ length: Math.max(2, t.segments) }, () => ({
      x: 0,
      y: 0,
      velocityX: 0,
      velocityY: 0
    }));
  }
  reset() {
    for (const t of this.particles)
      t.x = 0, t.y = 0, t.velocityX = 0, t.velocityY = 0;
  }
  advance(t, n, o, i = { response: 0.5, stability: 0.5 }) {
    const s = vt(o, 0.004166666666666667, 0.05), r = 0.65 + vt(i.response, 0, 1) * 0.7, a = 0.7 + vt(i.stability, 0, 1) * 0.6;
    for (let c = 0; c < this.particles.length; c += 1) {
      const u = this.particles[c], h = c === 0 ? void 0 : this.particles[c - 1], d = this.particles.length <= 1 ? 0 : c / (this.particles.length - 1), f = 0.22 + d * 0.08, m = 1 + d * 0.18, l = h ? h.x * this.profile.propagation * (1 - f) + t * m * f : t, p = h ? h.y * this.profile.propagation * (1 - f) + n * m * f : n, x = this.profile.stiffness * r * (1 - d * 0.32), k = this.profile.damping * a * (1 - d * 0.18);
      u.velocityX += ((l - u.x) * x - u.velocityX * k) * s, u.velocityY += ((p - u.y) * x - u.velocityY * k) * s, u.x = vt(u.x + u.velocityX * s, -this.profile.maxDisplacement, this.profile.maxDisplacement), u.y = vt(u.y + u.velocityY * s, -this.profile.maxDisplacement, this.profile.maxDisplacement);
    }
  }
  sample(t) {
    const n = t?.x.length === this.particles.length ? t.x : new Array(this.particles.length), o = t?.y.length === this.particles.length ? t.y : new Array(this.particles.length);
    for (let i = 0; i < this.particles.length; i += 1)
      n[i] = this.particles[i].x, o[i] = this.particles[i].y;
    return t && t.x === n && t.y === o ? t : { x: n, y: o };
  }
}
function Et(e) {
  let t = e >>> 0;
  return () => {
    t += 1831565813;
    let n = t;
    return n = Math.imul(n ^ n >>> 15, n | 1), n ^= n + Math.imul(n ^ n >>> 7, n | 61), ((n ^ n >>> 14) >>> 0) / 4294967296;
  };
}
function zl(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0) / 4294967296 * Math.PI * 2;
}
function Ae(e) {
  const t = Math.max(0, Math.min(1, e));
  return t * t * (3 - 2 * t);
}
function xt(e, t, n, o = 0) {
  const i = t - e.start + o, s = e[n];
  if (i <= 0)
    return 0;
  if (i < e.transition)
    return s * Ae(i / e.transition);
  const r = s * 0.955;
  if (i < e.transition + e.hold) {
    const c = (i - e.transition) / e.hold;
    return s + (r - s) * Ae(c);
  }
  const a = (i - e.transition - e.hold) / e.returnDuration;
  return a < 1 ? r * (1 - Ae(a)) : 0;
}
function Cl(e, t = 900) {
  const n = Et(e), o = [];
  let i = 1.15 + n() * 0.55, s = 0;
  const r = n() < 0.5 ? -1 : 1;
  for (; i < t; ) {
    const a = s % 4, c = a >= 2, u = a === 2, h = a === 0 ? r : a === 1 ? -r : n() < 0.5 ? -1 : 1, d = 0.62 + n() * 0.16;
    o.push({
      start: i,
      transition: 0.68 + n() * 0.22,
      hold: 0.55 + n() * 0.28,
      returnDuration: 0.88 + n() * 0.24,
      yaw: h * (c ? 0.08 + n() * 0.08 : d),
      pitch: c ? u ? -(0.26 + n() * 0.1) : 0.3 + n() * 0.12 : (n() - 0.5) * 0.1,
      roll: h * (c ? 0.03 + n() * 0.035 : 0.12 + n() * 0.08)
    }), i += 3.6 + n() * 0.65, s += 1;
  }
  return o;
}
function Me(e, t, n, o, i) {
  const s = 5 + Math.max(0, Math.min(1, o)) * 8, r = 0.72 + Math.max(0, Math.min(1, i)) * 0.38, a = (t - e.value) * s * s - 2 * r * s * e.velocity;
  e.velocity += a * n, e.value += e.velocity * n;
}
function Tl(e, t, n, o) {
  const i = Math.max(0, o) * n, s = Math.max(-i, Math.min(i, t - e.value));
  e.velocity = s / n, e.value += s;
}
function Ie(e, t, n, o, i) {
  e.advance(t * i.amplitude, n * i.amplitude, o, i);
}
function pn(e, t) {
  const n = Et(t ^ 2654435769);
  let o = 2 + n() * 2.5;
  for (; o < e + 0.25; ) {
    const i = 0.12 + n() * 0.06;
    if (e >= o && e <= o + i) {
      const s = (e - o) / i;
      return s < 0.45 ? Ae(s / 0.45) : 1 - Ae((s - 0.45) / 0.55);
    }
    o += 3.5 + n() * 3.5;
  }
  return 0;
}
function Ll(e, t) {
  const n = Et(t ^ 1779033703);
  let o = 1.45 + n() * 0.85;
  for (; o <= e + 0.4; ) {
    const i = 4 + Math.floor(n() * 4);
    let s = 0;
    for (let r = 0; r < i; r += 1) {
      const a = 0.3 + n() * 0.12, c = 0.045 + n() * 0.065, u = 0.48 + n() * 0.52, h = e - o - s;
      if (h >= 0 && h <= a) {
        const d = h / a;
        return u * (d < 0.46 ? Ae(d / 0.46) : 1 - Ae((d - 0.46) / 0.54));
      }
      s += a + c;
    }
    o += s + 3.2 + n() * 2.4;
  }
  return 0;
}
function gr(e) {
  return e <= 0 || e >= 1 ? 0 : e < 0.2 ? Ae(e / 0.2) : e < 0.38 ? 1 : 1 - Ae((e - 0.38) / 0.62);
}
function ei(e, t, n) {
  const o = Et(t ^ (n === "left" ? 1013904242 : 2773480762));
  let i = 1.35 + o() * 1.55;
  for (; i < e + 0.8; ) {
    const s = 0.22 + o() * 0.055, r = 3 + Math.floor(o() * 2), a = 0.016 + o() * 4e-3, c = (o() < 0.5 ? -1 : 1) * (14e-4 + o() * 11e-4), u = e - i, h = s * r;
    if (u >= 0 && u <= h) {
      const d = Math.min(r - 1, Math.floor(u / s)), f = (u - d * s) / s, m = gr(f) * (1 - d * 0.055);
      return { x: c * m, y: -a * m };
    }
    i += h + 4.8 + o() * 4.2;
  }
  return { x: 0, y: 0 };
}
function Ol(e, t) {
  const n = Et(t ^ 3144134277);
  let o = 2.2 + n() * 2.1;
  for (; o < e + 0.9; ) {
    const i = 0.46 + n() * 0.14, s = 0.95 + n() * 0.35, r = e - o;
    if (r >= 0 && r <= i)
      return gr(r / i) * s;
    o += 5.6 + n() * 4.6;
  }
  return 0;
}
class ti {
  project;
  events;
  lastTime = 0;
  previousHead = 0;
  previousPitch = 0;
  previousRoll = 0;
  previousBody = 0;
  trackedYaw = { value: 0, velocity: 0 };
  trackedPitch = { value: 0, velocity: 0 };
  trackedRoll = { value: 0, velocity: 0 };
  trackedBody = { value: 0, velocity: 0 };
  trackedBodyPitch = { value: 0, velocity: 0 };
  trackedBodyRoll = { value: 0, velocity: 0 };
  trackedLookX = { value: 0, velocity: 0 };
  trackedLookY = { value: 0, velocity: 0 };
  trackedLookStrength = { value: 0, velocity: 0 };
  trackedEarLeftX = { value: 0, velocity: 0 };
  trackedEarLeftY = { value: 0, velocity: 0 };
  trackedEarRightX = { value: 0, velocity: 0 };
  trackedEarRightY = { value: 0, velocity: 0 };
  frontHairLeft = new we({ segments: 4, stiffness: 31, damping: 10.2, propagation: 1.08, maxDisplacement: 0.075 });
  frontHairRight = new we({ segments: 4, stiffness: 28, damping: 9.4, propagation: 1.1, maxDisplacement: 0.075 });
  backHairLeft = new we({ segments: 5, stiffness: 23, damping: 7.4, propagation: 1.09, maxDisplacement: 0.105 });
  backHairRight = new we({ segments: 5, stiffness: 21, damping: 6.9, propagation: 1.11, maxDisplacement: 0.105 });
  ahoge = new we({ segments: 5, stiffness: 19, damping: 5.6, propagation: 1.12, maxDisplacement: 0.14 });
  headwear = new we({ segments: 3, stiffness: 36, damping: 11.4, propagation: 1.04, maxDisplacement: 0.045 });
  topCloth = new we({ segments: 3, stiffness: 24, damping: 8.6, propagation: 1.06, maxDisplacement: 0.055 });
  skirt = new we({ segments: 4, stiffness: 18, damping: 7, propagation: 1.09, maxDisplacement: 0.09 });
  tail = new we({ segments: 5, stiffness: 18, damping: 6.8, propagation: 1.09, maxDisplacement: 0.11 });
  accessory = new we({ segments: 4, stiffness: 11, damping: 4.9, propagation: 1.1, maxDisplacement: 0.09 });
  hairStrandChains = /* @__PURE__ */ new Map();
  modelPhysics;
  renderSecondary;
  secondaryTunings = /* @__PURE__ */ new Map();
  constructor(t) {
    this.project = t, this.events = Cl(t.runtime.seed), this.modelPhysics = new Hr(t);
    for (const n of ["frontHair", "backHair", "ahoge", "headwear", "ears", "topCloth", "skirt", "tail", "accessory"])
      this.secondaryTunings.set(n, { amplitude: 1, response: 0.5, stability: 0.5, ...t.runtime.secondaryMotionTuning?.[n] ?? {} });
    for (const n of t.layers ?? [])
      if (!(n.role !== "frontHair" && n.role !== "backHair" && n.role !== "sideHair"))
        for (const [o, i] of (n.hairStrands ?? []).entries())
          this.hairStrandChains.set(i.id, {
            chain: new we({
              segments: i.physics.segments,
              stiffness: i.physics.stiffness,
              damping: i.physics.damping,
              propagation: n.role === "frontHair" ? 1.08 : 1.11,
              maxDisplacement: i.physics.maxDisplacement
            }),
            role: n.role,
            phase: zl(i.id),
            order: o
          });
  }
  reset() {
    this.lastTime = 0, this.previousHead = 0, this.previousPitch = 0, this.previousRoll = 0, this.previousBody = 0;
    for (const t of [this.trackedYaw, this.trackedPitch, this.trackedRoll, this.trackedBody, this.trackedBodyPitch, this.trackedBodyRoll, this.trackedLookX, this.trackedLookY, this.trackedLookStrength, this.trackedEarLeftX, this.trackedEarLeftY, this.trackedEarRightX, this.trackedEarRightY])
      t.value = 0, t.velocity = 0;
    for (const t of [this.frontHairLeft, this.frontHairRight, this.backHairLeft, this.backHairRight, this.ahoge, this.headwear, this.topCloth, this.skirt, this.tail, this.accessory])
      t.reset();
    for (const t of this.hairStrandChains.values())
      t.chain.reset();
    this.modelPhysics.reset();
  }
  sample(t, n = {}) {
    return this.sampleInternal(t, n, !1);
  }
  /** Hot-path sample whose secondary-chain arrays are reused by the renderer after each frame has been consumed. */
  sampleForRender(t, n = {}) {
    return this.sampleInternal(t, n, !0);
  }
  sampleInternal(t, n, o) {
    const i = n.primaryMotion ?? !0, s = i ? this.events.find((U) => t >= U.start - 0.42 && t <= U.start + U.transition + U.hold + U.returnDuration) : void 0, r = this.project.runtime.seed % 97 / 97 * Math.PI * 2, a = i ? Math.sin(t * 0.55 + r) * 0.018 + Math.sin(t * 0.19 + r * 0.7) * 0.012 : 0, c = i ? Math.sin(t * 0.37 + r * 1.3) * 0.01 : 0, u = i ? Math.sin(t * 0.29 + r * 0.45) * 8e-3 : 0, h = this.project.runtime.motionTuning ?? { amplitude: 1, response: 0.72, stability: 0.42 }, d = this.lastTime === 0 ? 1 / 60 : Math.max(1 / 240, Math.min(0.05, t - this.lastTime));
    this.lastTime = t;
    const f = Ml(n.runtimeControl, n.nowMs ?? Date.now()), m = (U, ge) => Rl(ge, U, f), l = f.motion.lookTargetStrength === void 0 ? void 0 : {
      x: f.motion.lookTargetX ?? 0,
      y: f.motion.lookTargetY ?? 0,
      strength: f.motion.lookTargetStrength
    }, p = i ? l ?? n.lookTarget : void 0;
    Me(this.trackedLookX, Math.max(-1, Math.min(1, p?.x ?? 0)), d, 1, 0.72), Me(this.trackedLookY, Math.max(-1, Math.min(1, p?.y ?? 0)), d, 1, 0.72), Me(this.trackedLookStrength, Math.max(0, Math.min(1, p?.strength ?? 0)), d, 1, 0.82);
    const x = Math.max(0, Math.min(1, this.trackedLookStrength.value)), k = ((s ? xt(s, t, "yaw") : 0) + a) * h.amplitude, w = ((s ? xt(s, t, "pitch") : 0) + c) * h.amplitude, P = ((s ? xt(s, t, "roll") : 0) + u) * h.amplitude, M = this.trackedLookX.value * 0.92 * h.amplitude, _ = this.trackedLookY.value * 0.96 * h.amplitude, B = this.trackedLookX.value * 0.1 * h.amplitude, $ = Math.max(Math.abs(this.trackedLookX.value), Math.abs(this.trackedLookY.value)), z = 1 - x * Math.min(1, 0.65 + $ * 0.35), A = m("headYaw", Math.max(-1, Math.min(1, k * z + (M + a * 0.2) * x))), C = m("headPitch", Math.max(-1, Math.min(1, w * z + (_ + c * 0.18) * x))), F = m("headRoll", Math.max(-1, Math.min(1, P * z + (B + u * 0.24) * x))), X = ((s ? xt(s, t, "yaw", 0.38) * 1.25 : 0) + a * 0.7) * h.amplitude, j = ((s ? xt(s, t, "pitch", 0.32) * 1.05 : 0) + c * 0.55) * h.amplitude, V = m("gazeX", X * z + this.trackedLookX.value * 1.1 * h.amplitude * x), se = m("gazeY", j * z + this.trackedLookY.value * 0.92 * h.amplitude * x);
    Me(this.trackedYaw, A, d, h.response, h.stability), Me(this.trackedPitch, C, d, h.response, h.stability), Me(this.trackedRoll, F, d, h.response, h.stability);
    const ie = Math.max(-1, Math.min(1, this.trackedYaw.value)), he = Math.max(-1, Math.min(1, this.trackedPitch.value)), fe = Math.max(-1, Math.min(1, this.trackedRoll.value)), de = V - ie * 0.55, be = se - he * 0.42;
    for (const [U, ge] of [
      [this.trackedBody, m("bodySway", ie * 0.62)],
      [this.trackedBodyPitch, m("bodyPitch", he * 0.46)],
      [this.trackedBodyRoll, m("bodyRoll", fe * 0.52 + ie * 0.16)]
    ])
      Tl(U, ge, d, 3);
    const nt = Math.max(-2.5, Math.min(2.5, (ie - this.previousHead) / d)), Be = Math.max(-2.5, Math.min(2.5, (he - this.previousPitch) / d)), ce = Math.max(-2.5, Math.min(2.5, (fe - this.previousRoll) / d)), b = Math.max(-1.2, Math.min(1.2, (this.trackedBody.value - this.previousBody) / d));
    this.previousHead = ie, this.previousPitch = he, this.previousRoll = fe, this.previousBody = this.trackedBody.value;
    const v = Math.sin(t * 0.81 + r * 0.63) * 0.34 + Math.sin(t * 0.29 + r * 1.17) * 0.14, y = Math.sin(t * 0.67 + r * 1.46) * 0.15 + Math.sin(t * 0.24 + r * 0.55) * 0.06, E = Math.sin(t * 1.07 + r * 1.41) * 0.34 + Math.sin(t * 0.43 + r * 0.38) * 0.16, N = Ol(t, this.project.runtime.seed), Y = Math.sin(t * 0.68 + r * 0.92 + Math.PI) * 0.29 + Math.sin(t * 0.33 + r * 1.58) * 0.13, W = Math.sin(t * 0.56 + r * 0.27) * 0.11, q = Math.sin(t * 0.39 + r * 1.73) * 0.055, te = ei(t, this.project.runtime.seed, "left"), _e = ei(t, this.project.runtime.seed, "right"), Ge = Math.sin(t * 0.36 + r * 1.09) * 0.19, Ee = Math.sin(t * 0.27 + r * 1.87) * 0.14 + Math.sin(t * 0.62 + r * 0.52) * 0.05, ze = Math.sin(t * 0.66 + r * 1.31) * 0.09, Ce = nt * 0.6 + b * 0.4 + ce * 0.18, Te = Be * 0.6 + this.trackedBodyPitch.velocity * 0.4, Le = -Ce * 0.02 + v * 0.027, ft = -Te * 0.013 + y * 0.019, zt = this.secondaryTunings.get("frontHair");
    Ie(this.frontHairLeft, Le + Math.sin(t * 0.53 + r * 1.91) * 45e-4, ft, d, zt), Ie(this.frontHairRight, Le + Math.sin(t * 0.61 + r * 0.31) * 4e-3, ft * 0.92, d, zt);
    const pt = -Ce * 0.034 + Y * 0.072, mt = -Te * 0.023 + W * 0.028, Ct = this.secondaryTunings.get("backHair");
    Ie(this.backHairLeft, pt + Math.sin(t * 0.33 + r * 0.43) * 8e-3, mt, d, Ct), Ie(this.backHairRight, pt + Math.sin(t * 0.41 + r * 1.37) * 75e-4, mt * 1.06, d, Ct);
    for (const U of this.hairStrandChains.values()) {
      const ge = U.role === "frontHair", _r = ge ? Le : pt, kr = ge ? ft : mt, wr = Math.sin(t * (0.47 + U.order * 0.037) + U.phase) * (ge ? 6e-3 : 0.012) + Math.sin(t * 0.23 + U.phase * 0.61) * (ge ? 25e-4 : 5e-3), Ir = Math.sin(t * (0.39 + U.order * 0.021) + U.phase * 1.19) * (ge ? 24e-4 : 45e-4);
      Ie(U.chain, _r + wr, kr + Ir, d, this.secondaryTunings.get(ge ? "frontHair" : "backHair"));
    }
    Ie(this.ahoge, -Ce * 0.026 + E * 0.1, -Te * 0.011 + y * 0.016 - N * 0.08, d, this.secondaryTunings.get("ahoge")), Ie(this.headwear, -(nt * 0.4 + b * 0.6 + ce * 0.28) * 0.01 + q * 0.24, -(Be * 0.4 + this.trackedBodyPitch.velocity * 0.6) * 6e-3, d, this.secondaryTunings.get("headwear"));
    const gt = b + this.trackedBodyRoll.velocity * 0.35, Jt = this.trackedBodyPitch.velocity;
    Ie(this.topCloth, -gt * 0.021 + Ge * 0.032, -Jt * 8e-3, d, this.secondaryTunings.get("topCloth"));
    const yr = Math.sin(t * 0.95 + r * 0.74) * 0.25 + Math.sin(t * 1.65 + r * 0.29) * 0.07, qt = this.project.layers?.some((U) => U.role === "bottomWear" && U.garmentStructure === "supported") ?? !1;
    Ie(this.skirt, -gt * (qt ? 0.032 : 0.031) + yr * (qt ? 0.085 : 0.115), -Jt * (qt ? 7e-3 : 8e-3), d, this.secondaryTunings.get("skirt"));
    const vr = Math.sin(t * 1.35 + r * 0.76) * 0.056 + Math.sin(t * 0.63 + r * 1.32) * 0.012;
    if (Ie(this.tail, -gt * 0.01 + Ee * 8e-3, -Jt * 0.015 + vr, d, this.secondaryTunings.get("tail")), Ie(this.accessory, -Ce * 0.023 + ze * 0.028, -Te * 0.014 + Math.sin(t * 0.51 + r * 1.61) * 8e-3, d, this.secondaryTunings.get("accessory")), this.project.runtime.secondaryMotionTuning?.ears) {
      const U = this.secondaryTunings.get("ears");
      Me(this.trackedEarLeftX, te.x * U.amplitude, d, U.response, U.stability), Me(this.trackedEarLeftY, te.y * U.amplitude, d, U.response, U.stability), Me(this.trackedEarRightX, _e.x * U.amplitude, d, U.response, U.stability), Me(this.trackedEarRightY, _e.y * U.amplitude, d, U.response, U.stability);
    } else
      this.trackedEarLeftX.value = te.x, this.trackedEarLeftY.value = te.y, this.trackedEarRightX.value = _e.x, this.trackedEarRightY.value = _e.y, this.trackedEarLeftX.velocity = this.trackedEarLeftY.velocity = 0, this.trackedEarRightX.velocity = this.trackedEarRightY.velocity = 0;
    const ke = o ? this.renderSecondary : void 0, Kt = ke?.hairStrands ?? {};
    for (const [U, ge] of this.hairStrandChains)
      Kt[U] = ge.chain.sample(o ? Kt[U] : void 0);
    const ue = {
      frontHairLeft: this.frontHairLeft.sample(ke?.frontHairLeft),
      frontHairRight: this.frontHairRight.sample(ke?.frontHairRight),
      backHairLeft: this.backHairLeft.sample(ke?.backHairLeft),
      backHairRight: this.backHairRight.sample(ke?.backHairRight),
      ahoge: this.ahoge.sample(ke?.ahoge),
      headwear: this.headwear.sample(ke?.headwear),
      topCloth: this.topCloth.sample(ke?.topCloth),
      skirt: this.skirt.sample(ke?.skirt),
      tail: this.tail.sample(ke?.tail),
      accessory: this.accessory.sample(ke?.accessory),
      hairStrands: Kt
    };
    o && (this.renderSecondary = ue);
    const ve = (U) => U.at(-1) ?? 0, Tt = (U, ge) => (ve(U) + ve(ge)) * 0.5, xr = 5.1 + this.project.runtime.seed % 17 / 17 * 0.7, Fn = t / xr * Math.PI * 2 - Math.PI * 0.5, br = m("breath", Math.max(-1, Math.min(1, Math.sin(Fn) * 0.86 + Math.sin(Fn * 0.5 + r) * 0.1)));
    return this.modelPhysics.sample({
      headYaw: ie,
      headPitch: he,
      headRoll: fe,
      bodySway: this.trackedBody.value,
      bodyPitch: this.trackedBodyPitch.value,
      bodyRoll: this.trackedBodyRoll.value,
      gazeX: this.project.runtime.features.gaze ? de : 0,
      gazeY: this.project.runtime.features.gaze ? be : 0,
      breath: br,
      hairX: this.project.runtime.features.hairPhysics ? Tt(ue.frontHairLeft.x, ue.frontHairRight.x) : 0,
      hairY: this.project.runtime.features.hairPhysics ? Tt(ue.frontHairLeft.y, ue.frontHairRight.y) : 0,
      ahogeX: this.project.runtime.features.hairPhysics ? ve(ue.ahoge.x) : 0,
      ahogeY: this.project.runtime.features.hairPhysics ? ve(ue.ahoge.y) : 0,
      backHairX: this.project.runtime.features.hairPhysics ? Tt(ue.backHairLeft.x, ue.backHairRight.x) : 0,
      backHairY: this.project.runtime.features.hairPhysics ? Tt(ue.backHairLeft.y, ue.backHairRight.y) : 0,
      headwearX: this.project.runtime.features.hairPhysics ? ve(ue.headwear.x) : 0,
      headwearY: this.project.runtime.features.hairPhysics ? ve(ue.headwear.y) : 0,
      earX: this.project.runtime.features.hairPhysics ? (this.trackedEarLeftX.value + this.trackedEarRightX.value) * 0.5 : 0,
      earY: this.project.runtime.features.hairPhysics ? (this.trackedEarLeftY.value + this.trackedEarRightY.value) * 0.5 : 0,
      earLeftX: this.project.runtime.features.hairPhysics ? this.trackedEarLeftX.value : 0,
      earLeftY: this.project.runtime.features.hairPhysics ? this.trackedEarLeftY.value : 0,
      earRightX: this.project.runtime.features.hairPhysics ? this.trackedEarRightX.value : 0,
      earRightY: this.project.runtime.features.hairPhysics ? this.trackedEarRightY.value : 0,
      clothX: this.project.runtime.features.hairPhysics ? ve(ue.skirt.x) : 0,
      clothY: this.project.runtime.features.hairPhysics ? ve(ue.skirt.y) : 0,
      tailX: this.project.runtime.features.hairPhysics ? ve(ue.tail.x) : 0,
      tailY: this.project.runtime.features.hairPhysics ? ve(ue.tail.y) : 0,
      accessoryX: this.project.runtime.features.hairPhysics ? ve(ue.accessory.x) : 0,
      accessoryY: this.project.runtime.features.hairPhysics ? ve(ue.accessory.y) : 0,
      ...this.project.runtime.features.hairPhysics ? { secondary: ue } : {},
      blink: m("blink", this.project.runtime.features.blink ? pn(t, this.project.runtime.seed) : 0),
      blinkLeft: m("blinkLeft", this.project.runtime.features.blink ? pn(t, this.project.runtime.seed) : 0),
      blinkRight: m("blinkRight", this.project.runtime.features.blink ? pn(t, this.project.runtime.seed) : 0),
      browLeft: m("browLeft", 0),
      browRight: m("browRight", 0),
      smile: m("smile", 0),
      cheekPuff: m("cheekPuff", 0),
      mouthOpen: m("mouthOpen", this.project.runtime.features.mouthMotion ? Ll(t, this.project.runtime.seed) : 0),
      mouthA: m("mouthA", 0),
      mouthI: m("mouthI", 0),
      mouthU: m("mouthU", 0),
      mouthE: m("mouthE", 0),
      mouthO: m("mouthO", 0),
      armLeft: m("armLeft", 0),
      armRight: m("armRight", 0),
      handLeftX: m("handLeftX", 0),
      handLeftY: m("handLeftY", 0),
      handRightX: m("handRightX", 0),
      handRightY: m("handRightY", 0),
      handLeftOpen: m("handLeftOpen", 0),
      handRightOpen: m("handRightOpen", 0),
      ...El(this.project, f),
      timeSeconds: t
    }, t);
  }
}
const $l = `#version 300 es
precision highp float;
in vec2 a_position;
in vec2 a_uv;
uniform vec2 u_aspectScale;
out vec2 v_uv;
out vec2 v_position;
void main() {
  v_uv = a_uv;
  v_position = a_position;
  vec2 clipPosition = vec2(a_position.x * 2.0 - 1.0, 1.0 - a_position.y * 2.0);
  gl_Position = vec4(clipPosition * u_aspectScale, 0.0, 1.0);
}`, Al = `#version 300 es
precision highp float;
uniform sampler2D u_texture;
uniform float u_opacity;
uniform float u_alphaThreshold;
in vec2 v_uv;
out vec4 outColor;
void main() {
  vec4 color = texture(u_texture, v_uv);
  if (color.a <= u_alphaThreshold) discard;
  // Textures are premultiplied before filtering; apply opacity once to RGBA.
  color *= u_opacity;
  outColor = color;
}`;
function ni(e, t, n) {
  const o = e.createShader(t);
  if (!o)
    throw new Error("无法创建 WebGL 着色器。");
  if (e.shaderSource(o, n), e.compileShader(o), !e.getShaderParameter(o, e.COMPILE_STATUS)) {
    const i = e.getShaderInfoLog(o) || "未知着色器错误";
    throw e.deleteShader(o), new Error(i);
  }
  return o;
}
function Nl(e) {
  const t = e.createProgram();
  if (!t)
    throw new Error("无法创建 WebGL 程序。");
  const n = ni(e, e.VERTEX_SHADER, $l), o = ni(e, e.FRAGMENT_SHADER, Al);
  if (e.attachShader(t, n), e.attachShader(t, o), e.linkProgram(t), e.deleteShader(n), e.deleteShader(o), !e.getProgramParameter(t, e.LINK_STATUS))
    throw new Error(e.getProgramInfoLog(t) || "WebGL 程序链接失败。");
  return t;
}
async function Zl(e) {
  return createImageBitmap(e, { premultiplyAlpha: "premultiply", colorSpaceConversion: "none" });
}
const oi = 2048 * 2048, wt = 4096;
function Dl(e, t, n, o = oi, i = wt) {
  const s = Number.isFinite(e) && e > 0 ? e : 1, r = Number.isFinite(t) && t > 0 ? t : 1, a = Number.isFinite(n) && n > 0 ? n : 1, c = Math.max(1, Math.round(s * a)), u = Math.max(1, Math.round(r * a)), h = Number.isFinite(o) && o > 0 ? o : oi, d = Number.isFinite(i) && i > 0 ? i : wt, f = Math.min(1, Math.sqrt(h / (c * u)), d / c, d / u), m = (l) => f < 1 ? Math.floor(l * f) : l;
  return {
    width: Math.max(1, m(c)),
    height: Math.max(1, m(u))
  };
}
function Fl(e, t, n, o) {
  const i = n + (o === void 0 ? 0 : Math.max(0, t - o));
  return Math.max(0, t - e - i) / 1e3;
}
function Wl(e, t, n, o) {
  if (![e, t, n, o].every((r) => Number.isFinite(r) && r > 0))
    return { x: 1, y: 1 };
  const i = e / t, s = n / o;
  return s > i ? { x: i / s, y: 1 } : s < i ? { x: 1, y: s / i } : { x: 1, y: 1 };
}
function Bl(e, t) {
  const n = wl(t);
  if (e.blendEquationSeparate(e.FUNC_ADD, e.FUNC_ADD), n === "multiply") {
    e.blendFuncSeparate(e.DST_COLOR, e.ONE_MINUS_SRC_ALPHA, e.ONE, e.ONE_MINUS_SRC_ALPHA);
    return;
  }
  if (n === "screen") {
    e.blendFuncSeparate(e.ONE, e.ONE_MINUS_SRC_COLOR, e.ONE, e.ONE_MINUS_SRC_ALPHA);
    return;
  }
  if (n === "add") {
    e.blendFuncSeparate(e.ONE, e.ONE, e.ONE, e.ONE_MINUS_SRC_ALPHA);
    return;
  }
  if (n === "darken") {
    e.blendEquationSeparate(e.MIN, e.FUNC_ADD), e.blendFuncSeparate(e.ONE, e.ONE, e.ONE, e.ONE_MINUS_SRC_ALPHA);
    return;
  }
  if (n === "lighten") {
    e.blendEquationSeparate(e.MAX, e.FUNC_ADD), e.blendFuncSeparate(e.ONE, e.ONE, e.ONE, e.ONE_MINUS_SRC_ALPHA);
    return;
  }
  e.blendFuncSeparate(e.ONE, e.ONE_MINUS_SRC_ALPHA, e.ONE, e.ONE_MINUS_SRC_ALPHA);
}
class Zn {
  canvas;
  gl;
  currentProject;
  controller;
  program;
  locations;
  resources = /* @__PURE__ */ new Map();
  animationFrame = 0;
  startedAt = 0;
  paused = !1;
  pausedAt;
  pausedDuration = 0;
  lastState;
  lookTarget = { x: 0, y: 0, strength: 0 };
  runtimeControl;
  outputOverride;
  authoredFrameReuse;
  deformedByLayerId = /* @__PURE__ */ new Map();
  deformationBuffersByLayerId = /* @__PURE__ */ new Map();
  uploadedLayerIds = /* @__PURE__ */ new Set();
  deformedPointCache = /* @__PURE__ */ new Map();
  constructor(t, n) {
    this.canvas = t, this.currentProject = n;
    const o = t.getContext("webgl2", {
      alpha: !0,
      antialias: !1,
      stencil: !0,
      premultipliedAlpha: !0,
      preserveDrawingBuffer: !1,
      powerPreference: "high-performance"
    });
    if (!o)
      throw new Error("当前环境不支持 WebGL2。");
    this.gl = o, this.program = Nl(o);
    const i = o.getUniformLocation(this.program, "u_opacity"), s = o.getUniformLocation(this.program, "u_alphaThreshold"), r = o.getUniformLocation(this.program, "u_aspectScale");
    if (!i || !s || !r)
      throw new Error("WebGL 程序缺少必要的 uniform。");
    this.locations = {
      position: o.getAttribLocation(this.program, "a_position"),
      uv: o.getAttribLocation(this.program, "a_uv"),
      opacity: i,
      alphaThreshold: s,
      aspectScale: r
    }, o.clearStencil(0), this.controller = new ti(n);
  }
  get project() {
    return this.currentProject;
  }
  /** The exact state used by the most recently rendered frame. */
  get motionState() {
    return this.lastState;
  }
  static async create(t, n, o) {
    const i = new Zn(t, n);
    try {
      return await i.loadTextures(o), i.resize(), i;
    } catch (s) {
      throw i.dispose(), s;
    }
  }
  async loadTextures(t) {
    let n = 0;
    const o = async () => {
      for (; n < this.project.layers.length; ) {
        const s = this.project.layers[n++], r = await Zl(await t(s));
        try {
          this.createLayerResources(s, r);
        } finally {
          r.close();
        }
      }
    }, i = Math.min(4, this.project.layers.length);
    await Promise.all(Array.from({ length: i }, () => o()));
  }
  createLayerResources(t, n) {
    const o = this.gl, i = o.createTexture(), s = [];
    for (let d = 0; d < 3; d += 1) {
      const f = o.createBuffer();
      f && s.push(f);
    }
    const r = o.createBuffer(), a = o.createBuffer();
    if (!i || s.length !== 3 || !r || !a) {
      i && o.deleteTexture(i);
      for (const d of s)
        o.deleteBuffer(d);
      throw r && o.deleteBuffer(r), a && o.deleteBuffer(a), new Error(`无法为 ${t.sourceName} 创建 GPU 资源。`);
    }
    o.bindTexture(o.TEXTURE_2D, i), o.pixelStorei(o.UNPACK_PREMULTIPLY_ALPHA_WEBGL, 0), o.texImage2D(o.TEXTURE_2D, 0, o.RGBA, o.RGBA, o.UNSIGNED_BYTE, n), o.texParameteri(o.TEXTURE_2D, o.TEXTURE_MIN_FILTER, o.LINEAR), o.texParameteri(o.TEXTURE_2D, o.TEXTURE_MAG_FILTER, o.LINEAR), o.texParameteri(o.TEXTURE_2D, o.TEXTURE_WRAP_S, o.CLAMP_TO_EDGE), o.texParameteri(o.TEXTURE_2D, o.TEXTURE_WRAP_T, o.CLAMP_TO_EDGE);
    const c = new Float32Array(t.mesh.points.length * 2);
    for (const d of s)
      o.bindBuffer(o.ARRAY_BUFFER, d), o.bufferData(o.ARRAY_BUFFER, c.byteLength, o.DYNAMIC_DRAW);
    const u = this.uvArray(t);
    o.bindBuffer(o.ARRAY_BUFFER, r), o.bufferData(o.ARRAY_BUFFER, u, o.STATIC_DRAW);
    const h = new Uint16Array(t.mesh.triangles);
    o.bindBuffer(o.ELEMENT_ARRAY_BUFFER, a), o.bufferData(o.ELEMENT_ARRAY_BUFFER, h, o.STATIC_DRAW), this.resources.set(t.id, {
      texture: i,
      positionBuffers: s,
      positionBufferIndex: 0,
      uvBuffer: r,
      indexBuffer: a,
      indexCount: h.length,
      indices: h,
      positions: c,
      uvs: u,
      uploadedPoints: void 0
    });
  }
  uvArray(t) {
    const n = new Float32Array(t.mesh.uvs.length * 2);
    for (let o = 0; o < t.mesh.uvs.length; o += 1) {
      const i = t.mesh.uvs[o];
      n[o * 2] = i.x, n[o * 2 + 1] = i.y;
    }
    return n;
  }
  drawingBufferTarget() {
    return this.outputOverride ? { width: this.outputOverride.width, height: this.outputOverride.height } : Dl(this.canvas.clientWidth, this.canvas.clientHeight, window.devicePixelRatio);
  }
  setOutputOverride(t) {
    if (t && (!Number.isInteger(t.width) || t.width < 1 || t.width > wt || !Number.isInteger(t.height) || t.height < 1 || t.height > wt))
      throw new Error(`输出画布尺寸必须在 1 到 ${wt} 之间。`);
    if (t?.background.mode === "solid" && !/^#[0-9a-f]{6}$/i.test(t.background.color))
      throw new Error("输出背景颜色必须是 #RRGGBB。");
    this.outputOverride = t ? { ...t, background: { ...t.background } } : void 0, this.render(this.lastState ?? this.controller.sample(0, { lookTarget: this.lookTarget }));
  }
  resize() {
    const { width: t, height: n } = this.drawingBufferTarget();
    (this.canvas.width !== t || this.canvas.height !== n) && (this.canvas.width = t, this.canvas.height = n), this.gl.viewport(0, 0, this.canvas.width, this.canvas.height);
  }
  render(t) {
    this.lastState = t;
    const n = this.gl;
    this.uploadedLayerIds.clear(), this.resize();
    const o = this.outputOverride?.background;
    if (o?.mode === "solid") {
      const x = Number.parseInt(o.color.slice(1), 16);
      n.clearColor((x >> 16 & 255) / 255, (x >> 8 & 255) / 255, (x & 255) / 255, 1);
    } else
      n.clearColor(0, 0, 0, 0);
    n.clear(n.COLOR_BUFFER_BIT), n.enable(n.BLEND), n.blendFuncSeparate(n.ONE, n.ONE_MINUS_SRC_ALPHA, n.ONE, n.ONE_MINUS_SRC_ALPHA), n.useProgram(this.program);
    const { position: i, uv: s, opacity: r, alphaThreshold: a, aspectScale: c } = this.locations, u = Wl(this.project.canvas.width, this.project.canvas.height, this.canvas.width, this.canvas.height);
    n.uniform2f(c, u.x, u.y), n.enableVertexAttribArray(i), n.enableVertexAttribArray(s);
    const h = (x, k, w, P = 0) => {
      const M = this.resources.get(x.id);
      if (!M)
        return;
      if (M.positions.length !== k.length * 2) {
        M.positions = new Float32Array(k.length * 2);
        for (const $ of M.positionBuffers)
          n.bindBuffer(n.ARRAY_BUFFER, $), n.bufferData(n.ARRAY_BUFFER, M.positions.byteLength, n.DYNAMIC_DRAW);
        M.uploadedPoints = void 0;
      }
      const _ = M.uploadedPoints !== k, B = !this.paused && !this.uploadedLayerIds.has(x.id);
      if (_ || B) {
        const $ = M.positions;
        for (let z = 0; z < k.length; z += 1) {
          const A = k[z];
          $[z * 2] = A.x, $[z * 2 + 1] = A.y;
        }
        M.positionBufferIndex = (M.positionBufferIndex + 1) % M.positionBuffers.length, n.bindBuffer(n.ARRAY_BUFFER, M.positionBuffers[M.positionBufferIndex]), n.bufferSubData(n.ARRAY_BUFFER, 0, $), M.uploadedPoints = k;
      }
      this.uploadedLayerIds.add(x.id), n.bindBuffer(n.ARRAY_BUFFER, M.positionBuffers[M.positionBufferIndex]), n.vertexAttribPointer(i, 2, n.FLOAT, !1, 0, 0), n.bindBuffer(n.ARRAY_BUFFER, M.uvBuffer), n.vertexAttribPointer(s, 2, n.FLOAT, !1, 0, 0), n.activeTexture(n.TEXTURE0), n.bindTexture(n.TEXTURE_2D, M.texture), n.uniform1f(r, w), n.uniform1f(a, P), n.bindBuffer(n.ELEMENT_ARRAY_BUFFER, M.indexBuffer), n.drawElements(n.TRIANGLES, M.indexCount, n.UNSIGNED_SHORT, 0);
    }, d = kl(this.project, t, this.authoredFrameReuse);
    this.authoredFrameReuse = { project: this.project, inputState: t, frame: d };
    const f = Sn(this.project, d.state), m = this.deformedByLayerId;
    m.clear();
    const l = this.project.layers.some((x) => x.visible !== !1 && x.role === "ear"), p = (x) => {
      const k = m.get(x.id);
      if (k)
        return k;
      const w = this.deformedPointCache.get(x.id);
      if (w?.layer === x && w.inputState === t && w.model === this.project.model && w.runtime === this.project.runtime && w.anchors === this.project.anchors && w.hasSeparateEarLayers === l)
        return m.set(x.id, w.points), w.points;
      const P = d.authoringByLayerId.get(x.id), M = P ? this.paused ? Vs(this.project, x, P.points, d.state, f) : js(this.project, x, P.points, d.state, f, this.deformationBuffersByLayerId.get(x.id)) : x.mesh.points;
      return !this.paused && P && this.deformationBuffersByLayerId.set(x.id, M), m.set(x.id, M), this.deformedPointCache.set(x.id, {
        layer: x,
        inputState: t,
        model: this.project.model,
        runtime: this.project.runtime,
        anchors: this.project.anchors,
        hasSeparateEarLayers: l,
        points: M
      }), M;
    };
    for (const x of d.layers) {
      const { layer: k } = x, w = p(k), P = k.clipLayerId ? this.project.layers.find((M) => M.id === k.clipLayerId) : void 0;
      P && (n.enable(n.STENCIL_TEST), n.stencilMask(255), n.clear(n.STENCIL_BUFFER_BIT), n.stencilFunc(n.ALWAYS, 1, 255), n.stencilOp(n.KEEP, n.KEEP, n.REPLACE), n.colorMask(!1, !1, !1, !1), n.disable(n.BLEND), h(P, p(P), 1, 0.01), n.colorMask(!0, !0, !0, !0), n.stencilMask(0), n.stencilFunc(n.EQUAL, 1, 255), n.stencilOp(n.KEEP, n.KEEP, n.KEEP)), n.enable(n.BLEND), Bl(n, k.blendMode), h(k, w, x.opacity), P && (n.disable(n.STENCIL_TEST), n.stencilMask(255));
    }
  }
  start() {
    if (this.animationFrame)
      return;
    this.startedAt = performance.now(), this.pausedDuration = 0, this.pausedAt = void 0;
    const t = (n) => {
      if (!this.paused)
        this.render(this.controller.sampleForRender(Fl(this.startedAt, n, this.pausedDuration, this.pausedAt), { lookTarget: this.lookTarget, ...this.runtimeControl ? { runtimeControl: this.runtimeControl } : {}, nowMs: Date.now() }));
      else {
        const { width: o, height: i } = this.drawingBufferTarget();
        (this.canvas.width !== o || this.canvas.height !== i) && this.render(this.lastState ?? this.controller.sample(0, { lookTarget: this.lookTarget }));
      }
      this.animationFrame = requestAnimationFrame(t);
    };
    this.animationFrame = requestAnimationFrame(t);
  }
  setPaused(t) {
    if (t === this.paused)
      return;
    const n = performance.now();
    t ? this.pausedAt = n : this.pausedAt !== void 0 && (this.pausedDuration += Math.max(0, n - this.pausedAt), this.pausedAt = void 0), this.paused = t;
  }
  setLookTarget(t) {
    this.lookTarget = {
      x: Math.max(-1, Math.min(1, t.x)),
      y: Math.max(-1, Math.min(1, t.y)),
      strength: Math.max(0, Math.min(1, t.strength))
    };
  }
  setRuntimeControl(t) {
    this.runtimeControl = t;
  }
  restartMotion() {
    const t = performance.now();
    this.controller.reset(), this.startedAt = t, this.pausedDuration = 0, this.pausedAt = this.paused ? t : void 0;
    const n = this.controller.sampleForRender(0, {
      lookTarget: this.lookTarget,
      ...this.runtimeControl ? { runtimeControl: this.runtimeControl } : {},
      nowMs: Date.now()
    });
    this.render(n);
  }
  updateProject(t) {
    const n = this.currentProject, o = new Set(n.layers.map((r) => r.id));
    if (t.layers.length !== o.size || t.layers.some((r) => !o.has(r.id)))
      throw new Error("编辑期间不能增加或移除纹理图层，请重新打开项目。");
    const i = new Map(n.layers.map((r) => [r.id, r]));
    for (const r of t.layers) {
      const a = i.get(r.id);
      if (a === r)
        continue;
      const c = this.resources.get(r.id);
      if (!c)
        continue;
      if (c.positions.length !== r.mesh.points.length * 2) {
        c.positions = new Float32Array(r.mesh.points.length * 2);
        for (const d of c.positionBuffers)
          this.gl.bindBuffer(this.gl.ARRAY_BUFFER, d), this.gl.bufferData(this.gl.ARRAY_BUFFER, c.positions.byteLength, this.gl.DYNAMIC_DRAW);
        c.uploadedPoints = void 0;
      }
      if (a?.mesh.uvs !== r.mesh.uvs) {
        const d = this.uvArray(r);
        (c.uvs.length !== d.length || c.uvs.some((m, l) => m !== d[l])) && (c.uvs = d, this.gl.bindBuffer(this.gl.ARRAY_BUFFER, c.uvBuffer), this.gl.bufferData(this.gl.ARRAY_BUFFER, c.uvs, this.gl.STATIC_DRAW));
      }
      const u = r.mesh.triangles;
      a?.mesh.triangles !== u && (c.indices.length !== u.length || c.indices.some((d, f) => d !== u[f])) && (c.indices = new Uint16Array(u), this.gl.bindBuffer(this.gl.ELEMENT_ARRAY_BUFFER, c.indexBuffer), this.gl.bufferData(this.gl.ELEMENT_ARRAY_BUFFER, c.indices, this.gl.STATIC_DRAW), c.indexCount = c.indices.length);
    }
    this.currentProject = t, (n.model !== t.model || n.runtime !== t.runtime || t.layers.some((r) => {
      const a = i.get(r.id);
      return !a || a.role !== r.role || a.garmentStructure !== r.garmentStructure || a.hairStrands !== r.hairStrands;
    })) && (this.controller = new ti(t));
  }
  dispose() {
    this.animationFrame && cancelAnimationFrame(this.animationFrame);
    for (const t of this.resources.values()) {
      this.gl.deleteTexture(t.texture);
      for (const n of t.positionBuffers)
        this.gl.deleteBuffer(n);
      this.gl.deleteBuffer(t.uvBuffer), this.gl.deleteBuffer(t.indexBuffer);
    }
    this.gl.deleteProgram(this.program), this.resources.clear(), this.deformedByLayerId.clear(), this.deformationBuffersByLayerId.clear(), this.uploadedLayerIds.clear(), this.deformedPointCache.clear(), this.authoredFrameReuse = void 0;
  }
}
class Dn {
  constructor(t, n, o) {
    this.canvas = o, this.project = t, this.renderer = n;
  }
  canvas;
  project;
  renderer;
  motion = {};
  characterState;
  sourceId = "web-sdk";
  pointerHandler;
  static async create(t) {
    const n = new URL(t.projectUrl, document.baseURI), o = await fetch(n);
    if (!o.ok) throw new Error(`无法读取 PuppetLoom 项目：HTTP ${o.status}`);
    const i = await o.json();
    if (i.version !== 4 || !Array.isArray(i.layers)) throw new Error("网页播放器需要 PuppetLoom v4 项目。");
    const s = await Zn.create(t.canvas, i, async (a) => {
      const c = await fetch(new URL(a.texture.replace(/\\/g, "/"), n));
      if (!c.ok) throw new Error(`无法读取纹理 ${a.texture}：HTTP ${c.status}`);
      return c.blob();
    }), r = new Dn(i, s, t.canvas);
    return t.pointerLook !== !1 && r.enablePointerLook(t.canvas), t.autoplay !== !1 ? s.start() : s.setPaused(!0), r;
  }
  setMotion(t) {
    this.motion = { ...t }, this.publish();
  }
  setCharacterState(t) {
    this.characterState = t ? structuredClone(t) : void 0, this.publish();
  }
  pause() {
    this.renderer.setPaused(!0);
  }
  play() {
    this.renderer.setPaused(!1), this.renderer.start();
  }
  dispose() {
    this.pointerHandler && this.canvas.removeEventListener("pointermove", this.pointerHandler), this.renderer.dispose();
  }
  publish() {
    this.renderer.setRuntimeControl({
      version: 1,
      viewerId: 1,
      capturedAtMs: Date.now(),
      sources: [{ id: this.sourceId, priority: 50, blend: 1, updatedAtMs: Date.now(), ...Object.keys(this.motion).length ? { motion: this.motion } : {}, ...this.characterState ? { characterState: this.characterState } : {} }]
    });
  }
  enablePointerLook(t) {
    this.pointerHandler = (n) => {
      const o = t.getBoundingClientRect();
      this.renderer.setLookTarget({ x: (n.clientX - o.left) / Math.max(1, o.width) * 2 - 1, y: (n.clientY - o.top) / Math.max(1, o.height) * 2 - 1, strength: 1 });
    }, t.addEventListener("pointermove", this.pointerHandler);
  }
}
class Hl extends HTMLElement {
  player;
  canvas = document.createElement("canvas");
  connectedCallback() {
    this.canvas.isConnected || (this.canvas.style.cssText = "display:block;width:100%;height:100%;", this.append(this.canvas));
    const t = this.getAttribute("src");
    t && Dn.create({ projectUrl: t, canvas: this.canvas, autoplay: !this.hasAttribute("paused"), pointerLook: !this.hasAttribute("no-pointer-look") }).then((n) => {
      this.player = n, this.dispatchEvent(new CustomEvent("puppetloom-ready"));
    }).catch((n) => this.dispatchEvent(new CustomEvent("puppetloom-error", { detail: n })));
  }
  disconnectedCallback() {
    this.player?.dispose(), this.player = void 0;
  }
}
customElements.get("puppetloom-player") || customElements.define("puppetloom-player", Hl);
export {
  Hl as PuppetLoomPlayerElement,
  Dn as PuppetLoomWebPlayer
};
//# sourceMappingURL=puppetloom-web.js.map
