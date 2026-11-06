/* =============================================================================
   app.js — the movement, the crown logic and the instrument panel
   ============================================================================= */
(function () {
  "use strict";

  let booted = false;

  const W = window.__watch;
  if (!W) {
    document.getElementById("watch").addEventListener("watch:ready", boot, { once: true });
    return;
  }
  boot();

  function boot() {
    if (booted) return;
    booted = true;
    const $ = (id) => document.getElementById(id);

    const svg = $("watch");
    const host = $("watchHost");
    const tilt = $("watchTilt");
    const glareEl = $("watchGlare");

    /* ------------------------------------------------------------- state -- */
    const state = {
      crown: 0,               // 0 = verschraubt, 1 = herausgezogen (Zeiger stellen)
      hackedAt: null,         // Zeitstempel beim Anhalten des Sekundenzeigers
      offset: 0,              // ms, die auf die Systemzeit addiert werden
      dayOffset: 0,
      light: 80,
      night: false,
      audio: false,
      dragging: false
    };

    /* frozen time already includes the offset, so continuity is preserved */
    const nowMs = () => (state.hackedAt !== null ? state.hackedAt : Date.now() + state.offset);
    const dateFor = (ms) => new Date(ms + state.dayOffset * 864e5);

    /* --------------------------------------------------------- hand loop -- */
    let lastSec = -1;
    let lastDay = -1;

    function frame() {
      const t = new Date(nowMs());
      const ms = t.getMilliseconds();
      const s = t.getSeconds() + ms / 1000;
      const m = t.getMinutes() + s / 60;
      const h = (t.getHours() % 12) + m / 60;

      W.secHand.setAttribute("transform", `rotate(${(s * 6).toFixed(3)} ${W.CX} ${W.CY})`);
      W.minHand.setAttribute("transform", `rotate(${(m * 6).toFixed(3)} ${W.CX} ${W.CY})`);
      W.hourHand.setAttribute("transform", `rotate(${(h * 30).toFixed(3)} ${W.CX} ${W.CY})`);

      const day = dateFor(nowMs()).getDate();
      if (day !== lastDay) {
        const first = lastDay === -1;
        lastDay = day;
        setDate(day, !first);
      }

      const secInt = t.getSeconds();
      if (secInt !== lastSec) {
        lastSec = secInt;
        tick();
      }

      digital(t);
      requestAnimationFrame(frame);
    }

    /* ------------------------------------------------------------- date -- */
    let stripRaf = 0;
    const stripState = { pos: 0, target: 0, lens: 0, targetLens: 0 };

    function setDate(day, animate) {
      stripState.target = -(day - 1) * W.CELL;
      stripState.targetLens = -(day - 1) * W.CELL_LENS;
      $("datestep").textContent = String(day);
      if (!animate) {
        stripState.pos = stripState.target;
        stripState.lens = stripState.targetLens;
        applyStrip();
      }
      if (!stripRaf) stripRaf = requestAnimationFrame(animateStrip);
    }

    function applyStrip() {
      W.stripMain.setAttribute("transform", `translate(0 ${stripState.pos.toFixed(2)})`);
      if (W.stripLens) W.stripLens.setAttribute("transform", `translate(0 ${stripState.lens.toFixed(2)})`);
    }

    function animateStrip() {
      stripRaf = 0;
      let moving = false;
      [["pos", "target"], ["lens", "targetLens"]].forEach(([k, t]) => {
        const d = stripState[t] - stripState[k];
        if (Math.abs(d) > 0.2) { stripState[k] += d * 0.26; moving = true; }
        else stripState[k] = stripState[t];
      });
      applyStrip();
      if (moving) stripRaf = requestAnimationFrame(animateStrip);
    }

    function bumpDate(delta) {
      state.dayOffset += delta;
      lastDay = -1;
      frameOnce();
      clickSound();
    }

    let rafPending = false;
    function frameOnce() {
      if (rafPending) return;
      rafPending = true;
      requestAnimationFrame(() => { rafPending = false; });
    }

    /* ---------------------------------------------------------- digital -- */
    function digital(t) {
      const p = (n) => String(n).padStart(2, "0");
      $("digital").textContent = `${p(t.getHours())}:${p(t.getMinutes())}:${p(t.getSeconds())}`;
    }

    /* ------------------------------------------------------------ crown -- */
    const crownCap = $("crownCap");
    const crownBtn = $("crownBtn");
    const stateText = $("stateText");

    function setCrown(pos, silent) {
      state.crown = pos;
      document.body.classList.toggle("is-set", pos === 1);
      crownBtn.setAttribute("aria-pressed", pos === 1 ? "true" : "false");
      crownCap.textContent = pos === 1 ? "Gezogen" : "Verschraubt";
      stateText.textContent = pos === 1 ? "Zeiger stellen" : "Gangwerk läuft";
      if (pos === 1) {
        state.hackedAt = Date.now() + state.offset;   /* absolute, frozen */
        W.crownCapG.setAttribute("transform", "translate(9 0)");
        tilt.style.cursor = "ns-resize";
      } else {
        if (state.hackedAt !== null) {
          state.offset = state.hackedAt - Date.now();   /* keep the set time */
          state.hackedAt = null;
        }
        W.crownCapG.setAttribute("transform", "translate(0 0)");
        tilt.style.cursor = "";
      }
      if (!silent) clickSound(true);
    }

    crownBtn.addEventListener("click", () => setCrown(state.crown === 0 ? 1 : 0));

    /* drum the crown: drag up/down to set the time */
    let drag = null;

    function beginDrag(e) {
      state.dragging = true;
      drag = { y: e.clientY, x: e.clientX, ms: 0 };
      host.setPointerCapture && host.setPointerCapture(e.pointerId);
      e.preventDefault();
    }

    function moveDrag(e) {
      if (!drag) return;
      const dy = drag.y - e.clientY;
      if (Math.abs(dy) > 1) {
        /* 1 px ≈ 1.2 minutes */
        const deltaMs = dy * 1.2 * 60000;
        if (state.hackedAt !== null) state.hackedAt += deltaMs;
        else state.offset += deltaMs;
        drag.y = e.clientY;
      }
      e.preventDefault();
    }

    function endDrag() {
      if (drag) clickSound();
      drag = null;
      state.dragging = false;
    }

    host.addEventListener("pointerdown", (e) => {
      if (state.crown === 0) return;
      beginDrag(e);
    });
    window.addEventListener("pointermove", moveDrag, { passive: false });
    window.addEventListener("pointerup", endDrag);
    window.addEventListener("pointercancel", endDrag);

    host.addEventListener("wheel", (e) => {
      if (state.crown === 0) return;
      e.preventDefault();
      const step = (e.deltaY > 0 ? 1 : -1) * 60000;
      if (state.hackedAt !== null) state.hackedAt += step;
      else state.offset += step;
    }, { passive: false });

    /* keyboard: arrow keys act on the crown when it is pulled */
    document.addEventListener("keydown", (e) => {
      if (state.crown === 0) return;
      const step = e.shiftKey ? 60000 : 10000;
      const s = e.key === "ArrowDown" || e.key === "ArrowLeft" ? -step : step;
      if (["ArrowUp", "ArrowRight", "ArrowDown", "ArrowLeft"].includes(e.key)) {
        if (state.hackedAt !== null) state.hackedAt += s; else state.offset += s;
        e.preventDefault();
      }
      if (e.key === "Escape") setCrown(0);
    });

    /* --------------------------------------------------------- date UI ---- */
    $("dateUp").addEventListener("click", () => bumpDate(1));
    $("dateDown").addEventListener("click", () => bumpDate(-1));
    $("dateToday").addEventListener("click", () => {
      state.dayOffset = 0;
      lastDay = -1;
      clickSound();
    });

    $("syncBtn").addEventListener("click", () => {
      state.offset = 0;
      state.hackedAt = null;
      state.dayOffset = 0;
      lastDay = -1;
      setCrown(0, true);
      stateText.textContent = "Auf Systemzeit gestellt";
      setTimeout(() => { stateText.textContent = "Gangwerk läuft"; }, 2200);
      clickSound();
    });

    /* ------------------------------------------------------------ light --- */
    const knob = $("lightKnob");
    const knobFace = $("knobFace");
    const lightVal = $("lightVal");

    function applyLight(v) {
      state.light = Math.max(20, Math.min(100, Math.round(v)));
      const k = (state.light - 20) / 80;          // 0 … 1
      const rot = -135 + k * 270;
      knobFace.style.setProperty("--knob-rot", rot + "deg");
      knob.querySelector(".knob__glow").style.setProperty("--knob-glow", (0.25 + k * 0.75).toFixed(2));
      lightVal.textContent = String(state.light);
      knob.setAttribute("aria-valuenow", String(state.light));

      document.documentElement.style.setProperty("--light", String(state.light));
      document.documentElement.style.setProperty("--light-b", (0.72 + k * 0.36).toFixed(3));
      document.documentElement.style.setProperty("--glow-o", (0.35 + k * 0.65).toFixed(3));
      W.glare.style.opacity = String(0.32 + k * 0.55);
    }

    let kd = null;
    knob.addEventListener("pointerdown", (e) => {
      kd = { y: e.clientY, v: state.light };
      knob.setPointerCapture && knob.setPointerCapture(e.pointerId);
      e.preventDefault();
    });
    window.addEventListener("pointermove", (e) => {
      if (!kd) return;
      applyLight(kd.v + (kd.y - e.clientY) * 0.9);
    });
    window.addEventListener("pointerup", () => { kd = null; });
    knob.addEventListener("wheel", (e) => {
      e.preventDefault();
      applyLight(state.light + (e.deltaY > 0 ? -4 : 4));
    }, { passive: false });
    knob.addEventListener("keydown", (e) => {
      const step = e.shiftKey ? 10 : 4;
      if (e.key === "ArrowUp" || e.key === "ArrowRight") { applyLight(state.light + step); e.preventDefault(); }
      if (e.key === "ArrowDown" || e.key === "ArrowLeft") { applyLight(state.light - step); e.preventDefault(); }
    });
    applyLight(80);

    /* ------------------------------------------------------------ night --- */
    const lumeBtn = $("lumeBtn");

    /* the lume lives in the indices and the hands themselves: CSS lights it */
    let lumeTimer = null;

    function setNight(on) {
      state.night = on;
      lumeBtn.setAttribute("aria-checked", on ? "true" : "false");
      document.body.classList.toggle("night", on);

      W.dialFace.style.filter = on ? "brightness(.26) saturate(.5)" : "";
      W.bezel.style.filter = on ? "brightness(.6) saturate(.75)" : "";
      W.caseG.style.filter = on ? "brightness(.58) saturate(.8)" : "";
      W.braceletG.style.filter = on ? "brightness(.46) saturate(.75)" : "";
      W.crystal.style.filter = on ? "brightness(.5)" : "";
      W.glare.style.opacity = on ? "0.1" : String(0.32 + (state.light - 20) / 80 * 0.55);

      /* Super-LumiNova charges and then fades */
      clearTimeout(lumeTimer);
      document.documentElement.style.setProperty("--lume", on ? "1" : "0.35");
      if (on) {
        lumeTimer = setTimeout(() => {
          document.documentElement.style.setProperty("--lume", "0.42");
        }, 12000);
      }
      if (on) clickSound();
    }

    lumeBtn.addEventListener("click", () => setNight(!state.night));

    /* ------------------------------------------------------------ sound --- */
    const soundBtn = $("soundBtn");
    let ac = null;

    function audio() {
      if (!ac) {
        const AC = window.AudioContext || window.webkitAudioContext;
        if (!AC) return null;
        ac = new AC();
      }
      if (ac.state === "suspended") ac.resume();
      return ac;
    }

    function clickSound(low) {
      if (!state.audio) return;
      const ctx = audio();
      if (!ctx) return;
      const t0 = ctx.currentTime;
      const len = 0.05;
      const buf = ctx.createBuffer(1, Math.ceil(ctx.sampleRate * len), ctx.sampleRate);
      const ch = buf.getChannelData(0);
      for (let i = 0; i < ch.length; i++) {
        const p = i / ch.length;
        ch[i] = (Math.random() * 2 - 1) * Math.pow(1 - p, 6);
      }
      const src = ctx.createBufferSource();
      src.buffer = buf;
      const bp = ctx.createBiquadFilter();
      bp.type = "bandpass";
      bp.frequency.value = low ? 1500 : 2900;
      bp.Q.value = low ? 2.4 : 4;
      const g = ctx.createGain();
      g.gain.value = low ? 0.16 : 0.1;
      src.connect(bp).connect(g).connect(ctx.destination);
      src.start(t0);
    }

    /* mechanical tick, aligned to the second */
    function tick() {
      if (!state.audio || state.crown === 1) return;
      const ctx = audio();
      if (!ctx) return;
      const t0 = ctx.currentTime;
      const len = 0.045;
      const buf = ctx.createBuffer(1, Math.ceil(ctx.sampleRate * len), ctx.sampleRate);
      const ch = buf.getChannelData(0);
      for (let i = 0; i < ch.length; i++) {
        const p = i / ch.length;
        ch[i] = ((Math.random() * 2 - 1) * 0.7 + Math.sin(p * 90) * 0.3) * Math.pow(1 - p, 9);
      }
      const src = ctx.createBufferSource();
      src.buffer = buf;
      const bp = ctx.createBiquadFilter();
      bp.type = "bandpass";
      bp.frequency.value = 3400;
      bp.Q.value = 3.2;
      const g = ctx.createGain();
      g.gain.value = 0.085;
      src.connect(bp).connect(g).connect(ctx.destination);
      src.start(t0);

      const osc = ctx.createOscillator();
      const og = ctx.createGain();
      osc.frequency.setValueAtTime(780, t0);
      osc.frequency.exponentialRampToValueAtTime(320, t0 + 0.035);
      og.gain.setValueAtTime(0.05, t0);
      og.gain.exponentialRampToValueAtTime(0.0001, t0 + 0.04);
      osc.connect(og).connect(ctx.destination);
      osc.start(t0);
      osc.stop(t0 + 0.05);
    }

    soundBtn.addEventListener("click", () => {
      state.audio = !state.audio;
      soundBtn.setAttribute("aria-checked", state.audio ? "true" : "false");
      if (state.audio) { audio(); tick(); } else if (ac) { ac.suspend(); }
    });

    /* -------------------------------------------------------- tilt/glare -- */
    let tiltRaf = 0;
    let target = { rx: 0, ry: 0, gx: 50, gy: 30 };

    host.addEventListener("pointermove", (e) => {
      const r = host.getBoundingClientRect();
      const nx = (e.clientX - r.left) / r.width - 0.5;
      const ny = (e.clientY - r.top) / r.height - 0.5;
      target.rx = (-ny * 7).toFixed(2);
      target.ry = (nx * 9).toFixed(2);
      target.gx = (30 + nx * 60).toFixed(1);
      target.gy = (18 + ny * 46).toFixed(1);
      if (!tiltRaf) tiltRaf = requestAnimationFrame(applyTilt);
    });

    host.addEventListener("pointerleave", () => {
      target = { rx: 0, ry: 0, gx: 50, gy: 30 };
      if (!tiltRaf) tiltRaf = requestAnimationFrame(applyTilt);
    });

    function applyTilt() {
      tiltRaf = 0;
      tilt.style.transform = `rotateX(${target.rx}deg) rotateY(${target.ry}deg)`;
      glareEl.style.setProperty("--gx", target.gx + "%");
      glareEl.style.setProperty("--gy", target.gy + "%");
    }

    /* ------------------------------------------------------------- start -- */
    setCrown(0, true);
    requestAnimationFrame(frame);

    /* expose a tiny API for debugging / screenshots */
    window.__watchApp = {
      setCrown,
      setNight,
      setLight: applyLight,
      setTimeTo(h, m) {
        const d = new Date();
        d.setHours(h, m, 0, 0);
        state.hackedAt = d.getTime();
        state.offset = 0;
      },
      resume() { state.hackedAt = null; },
      state
    };
  }
})();
