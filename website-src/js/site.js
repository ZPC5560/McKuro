/* McKuro site v2: progressive enhancement only.
   With JS off every panel is still readable (no-JS shows the first panel and all tab labels).
   Motion is capped by prefers-reduced-motion. */
(function () {
  "use strict";

  var reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  var $ = function (s, r) { return (r || document).querySelector(s); };
  var $$ = function (s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); };

  /* ---------- 1. nav: stuck state + mobile drawer ---------- */
  var nav = $("#nav"), burger = $("#burger"), drawer = $("#drawer");
  if (nav) {
    var onScroll = function () { nav.classList.toggle("is-stuck", window.scrollY > 8); };
    onScroll();
    window.addEventListener("scroll", onScroll, { passive: true });
  }
  if (burger && drawer) {
    var setDrawer = function (open) {
      burger.setAttribute("aria-expanded", String(open));
      burger.setAttribute("aria-label", open ? "关闭菜单" : "打开菜单");
      if (open) { drawer.removeAttribute("hidden"); } else { drawer.setAttribute("hidden", ""); }
    };
    burger.addEventListener("click", function () { setDrawer(burger.getAttribute("aria-expanded") !== "true"); });
    drawer.addEventListener("click", function (e) { if (e.target.tagName === "A") { setDrawer(false); } });
    window.addEventListener("keydown", function (e) {
      if (e.key === "Escape" && burger.getAttribute("aria-expanded") === "true") { setDrawer(false); burger.focus(); }
    });
    window.matchMedia("(min-width: 941px)").addEventListener("change", function (e) { if (e.matches) { setDrawer(false); } });
  }

  /* ---------- 2. reveal on scroll ---------- */
  if (!reduce && "IntersectionObserver" in window) {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) {
        if (!en.isIntersecting) { return; }
        en.target.classList.add("is-in");
        io.unobserve(en.target);
      });
    }, { rootMargin: "0px 0px -8% 0px", threshold: 0.08 });
    [".mods", ".looks", ".steps", ".facts", ".faq"].forEach(function (sel) {
      var g = $(sel);
      if (!g) { return; }
      Array.prototype.forEach.call(g.children, function (c, i) { c.style.setProperty("--d", Math.min(i, 8) * 55 + "ms"); });
    });
    $$(".sec__head, .stage, .mod, .looks li, .steps li, .plat > *, .facts, .faq details, .dl__card, .wall").forEach(function (el) {
      el.classList.add("reveal");
      io.observe(el);
    });
  }

  /* ---------- 3. sign-in calendar: mark done days from --done ---------- */
  $$(".cal").forEach(function (cal) {
    var done = parseInt(getComputedStyle(cal).getPropertyValue("--done"), 10) || 0;
    $$("span", cal).forEach(function (d, i) {
      if (i < done - 1) { d.classList.add("done"); }
      else if (i === done - 1) { d.classList.add("today"); }
    });
  });

  /* ---------- 3b. INTAKE: scrolling folds the data cards into the window ----------
     Progress is read from the sticky stage's own position (no scroll listener maths
     beyond a rAF throttle). Each card flies to its slot, then the card content fades
     as the slot lights up, so the window appears to swallow the data. */
  var intake = $("[data-intake]");
  if (intake) {
    // progress must be measured on the tall SECTION, not the sticky child:
    // a pinned element keeps a constant top and would never advance.
    var intakeSec = intake.closest(".intake") || intake.parentElement;
    var cards = $$(".dcard", intake);
    var slotBox = $("[data-slots]", intake);
    var win = $(".intake__win", intake);

    // Desks come from the markup, but the landing slots are generated: the count must
    // match the number of cards, and markup would silently drift out of sync.
    if (slotBox && slotBox.children.length !== cards.length) {
      slotBox.textContent = "";
      cards.forEach(function () { slotBox.appendChild(document.createElement("i")); });
    }
    var slots = slotBox ? $$("i", slotBox) : [];

    // give each card a target: centre of its slot inside the window
    function layout() {
      if (!win || !slots.length) { return; }
      slots.forEach(function (s, i) {
        var card = cards[i];
        if (!card) { return; }
        var sr = s.getBoundingClientRect();
        var cr = card.getBoundingClientRect();
        if (!cr.width || !sr.width) { return; }
        card.style.setProperty("--tx", (sr.left + sr.width / 2 - (cr.left + cr.width / 2)).toFixed(1) + "px");
        card.style.setProperty("--ty", (sr.top + sr.height / 2 - (cr.top + cr.height / 2)).toFixed(1) + "px");
        card.style.setProperty("--sc", (sr.width / cr.width).toFixed(3));
      });
    }

    var ticking = false;
    function paint() {
      ticking = false;
      var r = intakeSec.getBoundingClientRect();
      // 0 when the section top reaches the sticky offset, 1 at the end of the travel
      var pinned = parseFloat(getComputedStyle(intake).top) || 96;
      var travel = Math.max(r.height - intake.offsetHeight, 1);
      var p = Math.min(Math.max((pinned - r.top) / travel, 0), 1);
      p = p < 0.5 ? 2 * p * p : 1 - Math.pow(-2 * p + 2, 2) / 2; // easeInOutQuad

      if (win) {
        win.style.opacity = String(Math.min(p / 0.22, 1));
        win.style.transform = "translate(-50%, -50%) scale(" + (0.9 + 0.1 * Math.min(p / 0.22, 1)).toFixed(4) + ")";
      }
      cards.forEach(function (c, i) {
        var start = i * 0.06;
        var cp = Math.min(Math.max((p - start) / (1 - start), 0), 1);
        var e = cp < 0.5 ? 2 * cp * cp : 1 - Math.pow(-2 * cp + 2, 2) / 2;
        c.style.transform =
          "translate(calc(var(--tx) * " + e.toFixed(4) + "), calc(var(--ty) * " + e.toFixed(4) + ")) " +
          "scale(" + (1 + (parseFloat(c.style.getPropertyValue("--sc") || 1) - 1) * e).toFixed(4) + ")";
        // the cards stay visible: they become the window's "今日数据" grid
        c.style.opacity = "1";
        var s = slots[i];
        if (s) { s.classList.toggle("is-lit", e > 0.88); }
      });
      if (p > 0.98) { intake.setAttribute("data-done", ""); } else { intake.removeAttribute("data-done"); }
    }
    function onScroll() { if (!ticking) { ticking = true; requestAnimationFrame(paint); } }

    if (reduce) {
      // no staging: light every slot and park the cards in place
      cards.forEach(function (c, i) { c.style.display = "none"; if (slots[i]) { slots[i].classList.add("is-lit"); } });
      if (win) { win.style.opacity = "1"; win.style.transform = "translate(-50%, -50%)"; }
    } else {
      layout();
      window.addEventListener("resize", function () { layout(); paint(); });
      window.addEventListener("scroll", onScroll, { passive: true });
      paint();
    }
  }

  /* ---------- 4. SIGNATURE demo: tabs drive a scripted "sync" ----------
     Order of a switch: status flips to syncing -> old panel leaves -> new panel lands
     in three beats (header, body, footer) -> status settles. Same cadence as the reference. */
  var app = $("[data-demo]");
  var tabs = $$(".tab");
  var status = $("[data-demo-status]");
  var pulse = $("[data-demo-status-pulse]");
  var current = 0, timer = null, pending = null;

  function setStatus(syncing) {
    if (!app) { return; }
    app.classList.toggle("is-syncing", syncing);
    if (status) { status.textContent = syncing ? "同步中…" : "同步"; }
    if (pulse) { pulse.hidden = !syncing; }
  }

  function select(idx, focus) {
    if (!tabs.length) { return; }
    idx = (idx + tabs.length) % tabs.length;
    var tab = tabs[idx];
    var key = tab.getAttribute("data-tab");
    tabs.forEach(function (t, i) {
      var on = i === idx;
      t.classList.toggle("is-on", on);
      t.setAttribute("aria-selected", String(on));
      t.tabIndex = on ? 0 : -1;
    });
    if (focus) { tab.focus(); }
    $$(".app__rail li[data-rail-for]").forEach(function (li) {
      li.classList.toggle("is-on", li.getAttribute("data-rail-for") === key);
    });
    if (idx === current && !$("#p-" + key).hidden) { return; }
    current = idx;
    var show = function () {
      $$(".panel").forEach(function (p) {
        var on = p.id === "p-" + key;
        p.hidden = !on;
        p.classList.toggle("is-on", on);
        p.classList.remove("is-enter");
        if (on && !reduce) { void p.offsetWidth; p.classList.add("is-enter"); }
      });
      setStatus(false);
    };
    clearTimeout(pending);
    if (reduce) { show(); return; }
    setStatus(true);
    pending = setTimeout(show, 420);
  }

  if (tabs.length) {
    tabs.forEach(function (t, i) {
      t.addEventListener("click", function () { stopAuto(); select(i); });
      t.addEventListener("keydown", function (e) {
        var k = e.key, n = null;
        if (k === "ArrowRight" || k === "ArrowDown") { n = current + 1; }
        else if (k === "ArrowLeft" || k === "ArrowUp") { n = current - 1; }
        else if (k === "Home") { n = 0; }
        else if (k === "End") { n = tabs.length - 1; }
        if (n === null) { return; }
        e.preventDefault(); stopAuto(); select(n, true);
      });
    });
    // hero shortcuts jump straight to a panel
    $$("[data-goto]").forEach(function (a) {
      a.addEventListener("click", function () {
        var key = a.getAttribute("data-goto");
        var i = tabs.findIndex(function (t) { return t.getAttribute("data-tab") === key; });
        if (i >= 0) { stopAuto(); select(i); }
      });
    });
    select(0);
  }

  // gentle auto-advance only while the stage is on screen; any interaction stops it for good
  var userTook = false;
  function startAuto() {
    if (reduce || timer || userTook || !tabs.length) { return; }
    timer = setInterval(function () { select(current + 1); }, 5200);
  }
  function stopAuto() { userTook = true; if (timer) { clearInterval(timer); timer = null; } }
  function pauseAuto() { if (timer) { clearInterval(timer); timer = null; } }
  var stage = $(".stage");
  if (stage && "IntersectionObserver" in window) {
    new IntersectionObserver(function (en) { if (en[0].isIntersecting) { startAuto(); } else { pauseAuto(); } }, { threshold: 0.35 }).observe(stage);
    stage.addEventListener("mouseenter", pauseAuto);
    stage.addEventListener("mouseleave", startAuto);
    stage.addEventListener("focusin", pauseAuto);
  }
  document.addEventListener("visibilitychange", function () { if (document.hidden) { pauseAuto(); } });

  /* ---------- 5. curved wall: scroll turns the ring a few degrees ---------- */
  var ring = $("[data-wall]");
  if (ring && !reduce) {
    var ticking = false;
    var turn = function () {
      ticking = false;
      var r = ring.getBoundingClientRect();
      var p = (r.top + r.height / 2 - window.innerHeight / 2) / window.innerHeight; // -1..1
      p = Math.max(-1, Math.min(1, p));
      ring.style.transform = "rotateY(" + (p * -9).toFixed(2) + "deg)";
    };
    window.addEventListener("scroll", function () { if (!ticking) { ticking = true; requestAnimationFrame(turn); } }, { passive: true });
    turn();
  }
  /* ---------- 6. version rail types itself in, then stays live from the GitHub API ---------- */
  var state = { version: "", date: "", typed: false };
  var railV = $('[data-rail="version"]'), railD = $('[data-rail="date"]');
  if (railV) { state.version = railV.textContent.trim(); }
  if (railD) { state.date = railD.textContent.trim(); }

  function setVersionText(ver, date) {
    if (ver) { state.version = ver; }
    if (date) { state.date = date; }
    $$('[data-rail="version"]').forEach(function (el) { el.removeAttribute("data-typing"); el.textContent = state.version; });
    $$('[data-rail="date"]').forEach(function (el) { el.removeAttribute("data-typing"); el.textContent = state.date; });
  }
  function typeInto(el, text, done) {
    var i = 0;
    el.textContent = "";
    el.setAttribute("data-typing", "");
    var tick = function () {
      if (!el.hasAttribute("data-typing")) { return; } // a live value replaced it mid-way
      i += 1;
      el.textContent = text.slice(0, i);
      if (i < text.length) { setTimeout(tick, 34); }
      else { el.removeAttribute("data-typing"); if (done) { done(); } }
    };
    setTimeout(tick, 600);
  }
  if (!reduce && railV && railD) {
    typeInto(railV, state.version, function () { typeInto(railD, state.date, null); });
  }

  var REPO = "ZPC5560/McKuro";
  var PICK = { win: /^McKuro-setup-.*\.exe$/i, mac: /^McKuro-osx-arm64-.*\.dmg$/i, linux: /^mckuro_.*_amd64\.deb$/i };
  function platformOf() {
    var ua = navigator.userAgent;
    if (/Mac|iPhone|iPad|iPod/i.test(ua)) { return "mac"; }
    if (/Linux|X11|CrOS|Android/i.test(ua)) { return "linux"; }
    return "win";
  }
  function applyRelease(rel) {
    if (!rel || !rel.tag_name) { return; }
    var tag = rel.tag_name;
    setVersionText(tag, (rel.published_at || "").slice(0, 10));
    var want = PICK[platformOf()], asset = null;
    (rel.assets || []).some(function (a) { if (want.test(a.name)) { asset = a; return true; } return false; });
    var href = asset ? asset.browser_download_url : "https://github.com/" + REPO + "/releases/latest";
    $$("[data-dl-primary]").forEach(function (a) {
      a.href = href;
      if (asset) { a.setAttribute("aria-label", "下载 " + tag + " " + asset.name); }
    });
    var hint = $("[data-dl-hint]");
    if (hint && asset) { hint.textContent = "已为你的系统选中 " + asset.name + "，其他平台的安装包在 Releases 页面。"; }
  }
  if (window.fetch) {
    fetch("https://api.github.com/repos/" + REPO + "/releases/latest", { headers: { Accept: "application/vnd.github+json" } })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(applyRelease)
      .catch(function () { /* offline or rate limited: keep baked-in values */ });
  }

  /* ---------- 7. count-up numbers (real values only) ---------- */
  var counters = $$("[data-count]");
  if (!reduce && counters.length && "IntersectionObserver" in window) {
    var cio = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) {
        if (!en.isIntersecting) { return; }
        var el = en.target, target = parseInt(el.getAttribute("data-count"), 10), t0 = performance.now();
        cio.unobserve(el);
        var step = function (now) {
          var t = Math.min((now - t0) / 900, 1);
          el.textContent = String(Math.round(target * (1 - Math.pow(1 - t, 3))));
          if (t < 1) { requestAnimationFrame(step); }
        };
        requestAnimationFrame(step);
      });
    }, { threshold: 0.5 });
    counters.forEach(function (el) { cio.observe(el); });
  }
})();
