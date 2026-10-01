/* Hero 3D: a tide of points with the 心月狐 figure standing over it.
   three.js and GLTFLoader are self-hosted (vendor/, MIT). The canvas fades in only
   after the first frame renders; the model loads separately, so a slow or missing
   asset still leaves a working hero. If WebGL is missing the CSS gradient stays.
   Rendering pauses when the hero is off screen or the tab is hidden; reduced motion
   gets a single still frame. */
import * as THREE from "./vendor/three.module.min.js";
import { GLTFLoader } from "./vendor/jsm/loaders/GLTFLoader.js";

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

  /* ---- 心月狐 model ----
     Loaded lazily, after the tide is already on screen, so the 2.8 MB asset never
     delays first paint. Normalised to height 1 here; resize() then scales it in
     proportion to the viewport, which keeps its on-screen size stable. */
  const figure = new THREE.Group();
  figure.visible = false;
  scene.add(figure);

  let figureReady = false;
  let figBaseY = 0;

  new GLTFLoader().load("./models/xin-yuehu.glb", (gltf) => {
    const src = gltf.scene;
    src.updateWorldMatrix(true, true);
    const box = new THREE.Box3().setFromObject(src);
    const size = box.getSize(new THREE.Vector3());
    const h = size.y || 1;
    src.scale.setScalar(1 / h);
    src.updateWorldMatrix(true, true);
    // origin at the feet, centred on x/z, so placement maths is simple
    const b2 = new THREE.Box3().setFromObject(src);
    src.position.set(-(b2.min.x + b2.max.x) / 2, -b2.min.y, -(b2.min.z + b2.max.z) / 2);

    // The GLB carries KHR_materials_unlit (GLTFLoader gives MeshBasicMaterial), which is
    // what these toon-shaded characters want: the atlas already has baked shading, and
    // pushing it through PBR washed the whites out. Only the colour space needs stating.
    src.traverse((n) => {
      if (!n.isMesh) return;
      (Array.isArray(n.material) ? n.material : [n.material]).forEach((m) => {
        if (m.map && m.map.colorSpace !== THREE.SRGBColorSpace) {
          m.map.colorSpace = THREE.SRGBColorSpace;
          m.needsUpdate = true;
        }
      });
    });

    figure.add(src);
    figure.visible = true;
    figureReady = true;
    resize();
    // Under reduced motion only the single setup frame was drawn, which happened
    // before the model arrived; draw once more so the figure actually appears.
    if (reduce) { frame(); }
  }, undefined, () => { /* keep the tide-only hero if the model cannot load */ });

  scene.add(new THREE.HemisphereLight(0xffffff, 0x9fb6dd, 1.6));
  const key = new THREE.DirectionalLight(0xffffff, 2.2);
  key.position.set(3, 5, 4);
  scene.add(key);
  const rim = new THREE.DirectionalLight(0x7fb0ff, 1.4);
  rim.position.set(-4, -1, -3);
  scene.add(rim);

  /* ---- layout: stand the figure in the free margin beside the centred copy ---- */
  function resize() {
    const w = hero.clientWidth, h = hero.clientHeight;
    renderer.setSize(w, h, false);
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
    if (!figureReady) { return; }
    // The copy is centred and its widest line is a fixed pixel width, so the free
    // margin shrinks in world units as the viewport narrows. Below this width the
    // figure would sit behind the headline, so it is hidden instead.
    const wide = w >= 1280;
    figure.visible = wide;
    if (!wide) { return; }
    const visH = 2 * Math.tan((camera.fov * Math.PI) / 360) * camera.position.z;
    const visW = visH * camera.aspect;
    // Measured: at this size the figure spans ~0.83 of NDC width and its flowing
    // cloth bleeds a little past the right edge, which reads as intentional framing.
    // Sizing it to fit entirely would need ~0.32 and makes her noticeably smaller.
    const fh = visW * 0.40;
    figure.scale.setScalar(fh);
    figBaseY = -fh * 0.5 + visH * 0.03;
    figure.position.set(visW * 0.345, figBaseY, 0);
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
    if (figureReady) {
      // shallow turntable: enough to read as 3D, never a distracting spin
      figure.rotation.y = Math.sin(t * 0.28) * 0.24 + tilt.x * 0.09;
      figure.position.y = figBaseY + Math.sin(t * 0.7) * 0.04;
    }
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
