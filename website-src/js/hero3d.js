/* Hero 3D: a tide of points with a faceted "resonance core" floating above it.
   three.js is self-hosted (vendor/three.module.min.js, MIT). The canvas fades in only
   after the first frame renders; if WebGL is missing the CSS gradient stays. Rendering
   pauses when the hero is off screen or the tab is hidden; reduced motion gets one still frame. */
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

  /* ---- resonance core: faceted crystal + two thin orbit rings ---- */
  const core = new THREE.Group();
  core.position.set(0, 0.55, 0);
  const crystal = new THREE.Mesh(
    new THREE.IcosahedronGeometry(1.05, 0),
    new THREE.MeshStandardMaterial({ color: 0xdfe9fb, metalness: 0.15, roughness: 0.18, flatShading: true, transparent: true, opacity: 0.94 })
  );
  const edges = new THREE.LineSegments(
    new THREE.EdgesGeometry(crystal.geometry),
    new THREE.LineBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.85 })
  );
  crystal.add(edges);
  core.add(crystal);
  const ringMat = new THREE.MeshBasicMaterial({ color: 0x5a8dee, transparent: true, opacity: 0.55 });
  const ringA = new THREE.Mesh(new THREE.TorusGeometry(1.75, 0.012, 8, 160), ringMat);
  const ringB = new THREE.Mesh(new THREE.TorusGeometry(2.15, 0.008, 8, 160), ringMat.clone());
  ringB.material.opacity = 0.32;
  ringA.rotation.x = Math.PI * 0.42;
  ringB.rotation.x = Math.PI * 0.58;
  ringB.rotation.y = 0.35;
  core.add(ringA, ringB);
  scene.add(core);

  scene.add(new THREE.HemisphereLight(0xffffff, 0x9fb6dd, 1.6));
  const key = new THREE.DirectionalLight(0xffffff, 2.2);
  key.position.set(3, 5, 4);
  scene.add(key);
  const rim = new THREE.DirectionalLight(0x7fb0ff, 1.4);
  rim.position.set(-4, -1, -3);
  scene.add(rim);

  /* ---- layout: core sits in the empty band above the headline on wide screens ---- */
  function resize() {
    const w = hero.clientWidth, h = hero.clientHeight;
    renderer.setSize(w, h, false);
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
    // Narrow screens: the headline + composer fill the whole band the core would float in,
    // so any part of it that shows reads as a glitch peeking out behind the card. Hide it.
    core.visible = w >= 700;
    core.scale.setScalar(w < 1100 ? 0.8 : 1);
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
    crystal.rotation.y = t * 0.25 + tilt.x * 0.5;
    crystal.rotation.x = 0.35 + tilt.y * 0.3;
    core.position.y = 0.55 + Math.sin(t * 0.9) * 0.08;
    ringA.rotation.z = t * 0.18;
    ringB.rotation.z = -t * 0.12;
    camera.position.x = tilt.x * 0.45;
    camera.lookAt(0, 0.6, 0);
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
