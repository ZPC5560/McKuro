/* Hero backdrop: a tide of points behind the copy, drawn with three.js.
 *
 * The character standing at the right of the hero is NOT here any more — it is the
 * Live2D 心 figure from hero-figure.js (PuppetLoom runtime), which is what the project
 * actually ships as the character showcase. This module owns only the tide field.
 *
 * three.js is self-hosted (vendor/, MIT). The canvas fades in only after the first
 * frame renders, so a slow or missing asset still leaves a working hero: the CSS
 * gradient in .hero__fallback stays underneath either way. Rendering pauses when the
 * hero is off screen or the tab is hidden; reduced motion gets a single still frame.
 */
import * as THREE from "./vendor/three.module.min.js";

const canvas = document.getElementById("hero-gl");
const hero = canvas && canvas.closest(".hero");
const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

function supportsGL() {
  try {
    const c = document.createElement("canvas");
    return !!(c.getContext("webgl2") || c.getContext("webgl"));
  } catch (e) { return false; }
}

if (canvas && hero && supportsGL()) {
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, powerPreference: "low-power" });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.75));
  renderer.setClearColor(0x000000, 0);

  const scene = new THREE.Scene();
  scene.fog = new THREE.Fog(0xe8edf4, 9, 26);
  const camera = new THREE.PerspectiveCamera(42, 1, 0.1, 60);
  camera.position.set(0, 2.6, 9.5);
  camera.lookAt(0, 0.6, 0);

  /* ---- tide: a grid of points displaced in the vertex shader ---- */
  const COLS = 180, ROWS = 90, W = 30, D = 15;
  const pos = new Float32Array(COLS * ROWS * 3);
  let k = 0;
  for (let z = 0; z < ROWS; z++) {
    for (let x = 0; x < COLS; x++) {
      pos[k++] = (x / (COLS - 1) - 0.5) * W;
      pos[k++] = 0;
      pos[k++] = (z / (ROWS - 1) - 0.5) * D - 3;
    }
  }
  const tideGeo = new THREE.BufferGeometry();
  tideGeo.setAttribute("position", new THREE.BufferAttribute(pos, 3));
  const tideMat = new THREE.ShaderMaterial({
    transparent: true,
    depthWrite: false,
    uniforms: {
      uTime: { value: 0 },
      uDpr: { value: renderer.getPixelRatio() },
      uDeep: { value: new THREE.Color(0x2a5bc0) },
      uCrest: { value: new THREE.Color(0x9dbeff) },
    },
    vertexShader: `
      uniform float uTime; uniform float uDpr;
      varying float vH; varying float vFade;
      void main() {
        vec3 p = position;
        float t = uTime * 0.55;
        float h = sin(p.x * 0.42 + t) * 0.32
                + sin(p.z * 0.65 - t * 1.3) * 0.22
                + sin((p.x + p.z) * 0.9 + t * 0.7) * 0.10;
        p.y = h - 1.25;
        vH = h;
        vec4 mv = modelViewMatrix * vec4(p, 1.0);
        vFade = smoothstep(26.0, 6.0, -mv.z) * smoothstep(15.5, 9.0, abs(p.x));
        gl_PointSize = (2.4 + h * 1.6) * uDpr * (8.0 / -mv.z);
        gl_Position = projectionMatrix * mv;
      }`,
    fragmentShader: `
      uniform vec3 uDeep; uniform vec3 uCrest;
      varying float vH; varying float vFade;
      void main() {
        vec2 c = gl_PointCoord - 0.5;
        float d = dot(c, c);
        if (d > 0.25) discard;
        vec3 col = mix(uDeep, uCrest, smoothstep(-0.5, 0.6, vH));
        gl_FragColor = vec4(col, (1.0 - d * 4.0) * vFade * 0.85);
      }`,
  });
  scene.add(new THREE.Points(tideGeo, tideMat));

  function resize() {
    const w = hero.clientWidth, h = hero.clientHeight;
    renderer.setSize(w, h, false);
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
  }
  resize();
  window.addEventListener("resize", resize);

  /* ---- pointer tilt (eased) ---- */
  const target = { x: 0, y: 0 }, tilt = { x: 0, y: 0 };
  if (!reduce) {
    hero.addEventListener("pointermove", (e) => {
      const r = hero.getBoundingClientRect();
      target.x = ((e.clientX - r.left) / r.width - 0.5) * 2;
      target.y = ((e.clientY - r.top) / r.height - 0.5) * 2;
    }, { passive: true });
  }

  const clock = new THREE.Clock();
  let t = 0, running = false, raf = 0;
  function frame() {
    const dt = Math.min(clock.getDelta(), 0.05);
    t += dt;
    tideMat.uniforms.uTime.value = t;
    tilt.x += (target.x - tilt.x) * 0.05;
    tilt.y += (target.y - tilt.y) * 0.05;
    camera.position.x = tilt.x * 0.4;
    camera.lookAt(0, 0.1, 0);
    renderer.render(scene, camera);
  }
  function loop() { if (!running) { return; } frame(); raf = requestAnimationFrame(loop); }
  function start() { if (running || reduce) { return; } running = true; clock.getDelta(); loop(); }
  function stop() { running = false; cancelAnimationFrame(raf); }

  t = 2.2;
  frame();
  canvas.classList.add("is-ready");
  if (!reduce) {
    new IntersectionObserver((en) => { if (en[0].isIntersecting && !document.hidden) { start(); } else { stop(); } }).observe(hero);
    document.addEventListener("visibilitychange", () => { if (document.hidden) { stop(); } else { start(); } });
  }
}
