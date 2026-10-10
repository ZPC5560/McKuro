/* Hero character: 心 in two forms, drawn by the PuppetLoom web runtime.
 *
 * Replaces the previous three.js dance figure on the hero's right margin. The tide
 * field behind the copy is unchanged and still drawn by hero3d.js; this module only
 * owns the character.
 *
 * The two forms (红黑 / 白金) alternate on their own, on an un-scheduled rhythm
 * ("呼吸式"): whichever form is up holds for a randomised dwell, then the other one
 * cross-fades in. Nothing is on a metronome, so the hero never reads as a slideshow.
 *
 * Interruption rules — deliberately narrow:
 *   - pointing AT HER holds the current form (the whole hero is 100vh tall, so binding
 *     hover to the section would freeze the rotation for anyone who ever moves a mouse);
 *   - clicking a form button shows it at once and restarts the dwell;
 *   - scrolling her off screen or hiding the tab stops the timer entirely (and resumes
 *     on return), because she is the most expensive thing on the page.
 *
 * Loading is staged so the hero paints fast:
 *   1. the first form's project + textures (~1.1 MB gzipped) -> first paint
 *   2. the other form, once the page is idle, so the first switch is already warm
 * If anything fails (no WebGL, asset missing, runtime error) the hero keeps its CSS
 * gradient and the toggle stays hidden — the section never depends on this module.
 *
 * Reduced motion: one static frame, no rotation and no timers at all.
 *
 * The runtime is imported DYNAMICALLY, not with a top-level `import`: a static import
 * is fetched as soon as this module evaluates, which would download the 72 KB runtime
 * even on the narrow viewports where the figure is hidden by CSS.
 */
const RUNTIME_URL = "./live2d/puppetloom-web.js";

const host = document.getElementById("hero-figure");
const hero = host && host.closest(".hero");
const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

const FORMS = [
  { key: "form1", label: "红黑形态" },
  { key: "form2", label: "白金形态" },
];

/* "呼吸式" dwell: a random hold between these bounds, re-rolled after every switch.
   Long enough to read as "she's just standing there", short enough to notice both. */
const DWELL_MIN = 11000;
const DWELL_MAX = 26000;

let PlayerClass = null;
async function playerClass() {
  if (!PlayerClass) {
    ({ PuppetLoomWebPlayer: PlayerClass } = await import(RUNTIME_URL));
  }
  return PlayerClass;
}

function supportsGL() {
  try {
    const c = document.createElement("canvas");
    return !!(c.getContext("webgl2") || c.getContext("webgl"));
  } catch (e) { return false; }
}

/* The figure is hidden by CSS below ~1100px (the copy fills the width and there is no
   free right margin). Skip loading her there entirely: the two projects are ~1.2 MB
   gzipped each, which is not worth spending on a character nobody can see. Re-checked
   on resize, so a window widened later still gets her. */
function hasRoom() {
  return host.clientWidth > 0 && getComputedStyle(host).display !== "none";
}

if (host && hero && supportsGL()) {
  /* One canvas per form, stacked; a switch is a class flip, so there is no reload,
     no flash, and no re-layout. */
  const canvases = FORMS.map(() => {
    const c = document.createElement("canvas");
    c.className = "hero__l2d";
    c.setAttribute("aria-hidden", "true");
    host.appendChild(c);
    return c;
  });

  const state = {
    ready: FORMS.map(() => false),
    failed: FORMS.map(() => false),
    players: FORMS.map(() => null),
    current: 0,
    timer: 0,
  };
  /* Held back while the pointer is on her or a control has focus. */
  let holding = false;
  /* Set by the IntersectionObserver; starts true so a hero already in view can rotate
     before the observer's first callback lands. */
  let onScreen = true;

  const toggle = document.getElementById("hero-form-toggle");
  const buttons = [];

  function publish() {
    host.dataset.form = FORMS[state.current].key;
    canvases.forEach((c, i) => {
      const on = i === state.current;
      c.classList.toggle("is-on", on);
      /* Only the visible canvas keeps a compositing layer once the other faded out. */
      c.style.willChange = on ? "opacity" : "";
    });
    buttons.forEach((b, i) => {
      b.setAttribute("aria-pressed", String(i === state.current));
      /* On a slow connection the second form can take tens of seconds to arrive. A
         button that looks enabled but does nothing is worse than one that says so, so
         the pending form is disabled until it is actually playable. */
      b.disabled = !state.ready[i] || state.failed[i];
      b.title = b.disabled ? `${FORMS[i].label}载入中…` : "";
    });
    if (toggle) { toggle.hidden = !state.ready.some(Boolean); }
  }

  function play(i) {
    const p = state.players[i];
    if (!p) { return; }
    if (reduce || i !== state.current) { p.pause(); } else { p.play(); }
  }

  /* Show form `i`. If it is still loading, the request is REMEMBERED and honoured when
     it arrives — the button is disabled while pending, but a keyboard/screen-reader
     activation or a click racing the load must not be silently dropped. */
  let wanted = null;

  function show(i) {
    if (state.failed[i]) { return; }
    if (!state.ready[i]) { wanted = i; return; }
    state.current = i;
    state.players.forEach((p, k) => { if (p) { play(k); } });
    publish();
    schedule();
  }

  /* The single place that decides whether the rotation runs, so every caller — a form
     finishing its load, a click, the pointer leaving, the tab becoming visible — gets
     the same answer. */
  function schedule() {
    clearTimeout(state.timer);
    if (reduce || holding || !onScreen || document.hidden) { return; }
    const others = state.ready
      .map((ok, i) => (ok && !state.failed[i] && i !== state.current ? i : -1))
      .filter((i) => i >= 0);
    if (others.length === 0) { return; }
    const wait = DWELL_MIN + Math.random() * (DWELL_MAX - DWELL_MIN);
    state.timer = setTimeout(() => show(others[(Math.random() * others.length) | 0]), wait);
  }

  async function load(i) {
    try {
      const Player = await playerClass();
      const player = await Player.create({
        projectUrl: `./live2d/${FORMS[i].key}/project.json`,
        canvas: canvases[i],
        autoplay: false,
        pointerLook: false,
      });
      state.players[i] = player;
      state.ready[i] = true;
      /* Draw one frame now: a canvas that is not current (or is paused under reduced
         motion) would otherwise stay blank until its first play(). */
      player.renderer.restartMotion();
      publish();
      /* Honour a switch requested while this form was still loading. */
      if (wanted === i) { wanted = null; show(i); return; }
      if (i === state.current) { play(i); }
      /* Re-arm here: this is the only moment the rotation becomes possible, and the
         observer's initial callback already fired before anything was ready. */
      schedule();
    } catch (err) {
      state.failed[i] = true;
      publish();
      console.warn(`[hero] ${FORMS[i].label} 载入失败`, err);
    }
  }

  function buildToggle() {
    if (!toggle || buttons.length) { return; }
    FORMS.forEach((f, i) => {
      const b = document.createElement("button");
      b.type = "button";
      b.className = "hero__form";
      b.textContent = f.label;
      b.setAttribute("aria-pressed", String(i === state.current));
      /* Start disabled: nothing is loaded yet, and a button that does nothing must not
         look available. publish() enables each one as its form becomes playable. */
      b.disabled = true;
      /* Show this one now; the dwell restarts, so the rhythm stays un-scheduled. */
      b.addEventListener("click", () => show(i));
      toggle.appendChild(b);
      buttons.push(b);
    });
  }

  buildToggle();
  /* Reflect the (nothing ready yet) state immediately, so there is no window in which
     the buttons exist but still look clickable. */
  publish();

  /* ---- hold while the pointer is actually on her ----
     The figure layer ignores pointer events (it sits behind the copy), so hit-testing
     is done against the visible canvas rectangle instead of via hover on the section —
     the section is 100vh tall and would hold the rotation for anyone who ever moves a
     mouse anywhere on the hero. */
  let pointerOnFigure = false;
  let toggleFocused = false;
  const activeRect = () => canvases[state.current].getBoundingClientRect();
  const setHold = (on) => { if (on !== holding) { holding = on; schedule(); } };
  const refreshHold = () => setHold(pointerOnFigure || toggleFocused);

  hero.addEventListener("pointermove", (e) => {
    const r = activeRect();
    const inside = r.width > 0 && e.clientX >= r.left && e.clientX <= r.right
      && e.clientY >= r.top && e.clientY <= r.bottom;
    if (inside !== pointerOnFigure) { pointerOnFigure = inside; refreshHold(); }
  }, { passive: true });
  hero.addEventListener("pointerleave", () => { pointerOnFigure = false; refreshHold(); });
  /* Keyboard users get the same courtesy while the toggle has focus. */
  toggle?.addEventListener("focusin", () => { toggleFocused = true; refreshHold(); });
  toggle?.addEventListener("focusout", () => { toggleFocused = false; refreshHold(); });

  /* ---- stop drawing while she is off screen or the tab is hidden ---- */
  if (!reduce) {
    new IntersectionObserver((en) => { onScreen = en[0].isIntersecting; schedule(); }).observe(hero);
    document.addEventListener("visibilitychange", schedule);
  }
  window.addEventListener("pagehide", () => {
    clearTimeout(state.timer);
    state.players.forEach((p) => { try { p?.dispose(); } catch (e) { /* ignore */ } });
  });

  /* ---- staged load: first form for an immediate figure, second after that ----
     The second load needs an explicit deadline: `requestIdleCallback` without a timeout
     is allowed to defer forever, and this page never stops animating (the tide plus the
     character's own render loop), so the idle period can simply never arrive. Without
     the timeout the second form silently never loads.

     Both forms are fetched even under reduced motion: that preference is about
     autonomous animation, not bandwidth, and switching forms is a user action that
     should still work (the rotation itself stays off — schedule() returns early). */
  async function start() {
    if (state.ready[0] || state.failed[0]) { return; }
    await load(0);
    const later = () => load(1);
    if (window.requestIdleCallback) {
      window.requestIdleCallback(later, { timeout: 2500 });
    } else {
      setTimeout(later, 2500);
    }
  }

  if (hasRoom()) { start(); }

  /* Widening the window past the breakpoint brings her in, so the cost is only paid
     when she can actually be seen. */
  let resizeTimer = 0;
  window.addEventListener("resize", () => {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => { if (hasRoom()) { start(); } }, 400);
  }, { passive: true });
}
