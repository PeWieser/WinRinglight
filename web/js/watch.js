/* =============================================================================
   watch.js — generative, fully vectorial Datejust-style timepiece
   Proportions traced from the reference photo:
     case Ø 344 · dial Ø 254 · fluted bezel 132→164 · flange 127→131
     bracelet 250 wide at the case · cyclops Ø 132 · crown 46 × 20
   Everything is generated at runtime and animates independently.
   ============================================================================= */
(function () {
  "use strict";

  const NS = "http://www.w3.org/2000/svg";
  const svg = document.getElementById("watch");
  svg.setAttribute("viewBox", "0 0 720 1002");

  const CX = 360;
  const CY = 500;

  /* geometry (1 unit = 0.5 px of the reference photo) */
  const R = {
    dial: 129.5,
    flange: 131.5,
    bezelIn: 132.5,
    bezelOut: 166,
    ring: 167.5,
    case: 168.5,
    lug: 177
  };

  /* In the photo the bubble is Ø 105 units and the magnified card inside it is
     ~64 x 45.  The bare aperture therefore is only ~30 x 24 and hides behind the
     bubble, exactly like on the real watch. */
  const DATE = { w: 30, h: 24, x: CX + 96, y: CY - 4 };
  /* cyclops: Ø 105 units, centred on the aperture */
  const LENS = { x: CX + 96, y: CY - 4, r: 47.5 };

  const TOP_END = 26;
  const BOT_END = 976;
  const BR_HALF = 125.5;
  const BR_TIP = 112;

  const LIGHT = { x: -0.62, y: -0.78 };

  const rad = (deg) => (deg - 90) * Math.PI / 180;
  const polar = (r, deg) => [CX + r * Math.cos(rad(deg)), CY + r * Math.sin(rad(deg))];
  const P = (n) => Math.round(n * 100) / 100;
  const clamp = (n, a, b) => Math.max(a, Math.min(b, n));
  const steel = (v, warm) => {
    const c = clamp(Math.round(v), 0, 255);
    const w = warm || 0;
    return `rgb(${Math.min(255, c + w)},${Math.min(255, c + 2)},${Math.min(255, c + 6)})`;
  };

  function E(tag, attrs, parent) {
    const n = document.createElementNS(NS, tag);
    if (attrs) for (const k in attrs) {
      const v = attrs[k];
      if (v !== undefined && v !== null) n.setAttribute(k, v);
    }
    if (parent) parent.appendChild(n);
    return n;
  }

  const defs = E("defs", {}, svg);
  function grad(id, type, stops, extra) {
    const g = E(type, Object.assign({ id }, extra || {}), defs);
    stops.forEach((s) => E("stop", { offset: s[0], "stop-color": s[1], "stop-opacity": s.length > 2 ? s[2] : 1 }, g));
    return g;
  }
  function filter(id, children, attrs) {
    const f = E("filter", Object.assign({ id }, attrs || {}), defs);
    (children || []).forEach((c) => f.appendChild(c));
    return f;
  }
  function clip(id, child) {
    const c = E("clipPath", { id }, defs);
    c.appendChild(child);
    return c;
  }

  /* ============================================================== GRADIENTS */
  grad("gHandFrame", "linearGradient", [
    [0, "#6c747c"], [0.2, "#dfe5ea"], [0.46, "#ffffff"], [0.72, "#e8edf0"], [1, "#878f97"]
  ], { x1: 0, y1: 0, x2: 1, y2: 0.12 });

  grad("gHandDark", "linearGradient", [
    [0, "#cfd6dc"], [0.3, "#a8b0b7"], [0.66, "#8b939b"], [1, "#6b737b"]
  ], { x1: 0, y1: 0, x2: 1, y2: 0.2 });

  grad("gHandSteel", "linearGradient", [
    [0, "#1b2429"], [0.08, "#8f979e"], [0.22, "#ffffff"], [0.38, "#f1f5f7"],
    [0.52, "#a2aab1"], [0.66, "#e6ebef"], [0.82, "#ffffff"], [0.93, "#7c848c"], [1, "#141b20"]
  ], { x1: 0, y1: 0, x2: 1, y2: 0 });

  grad("gChromeH", "linearGradient", [
    [0, "#828990"], [0.06, "#cfd5db"], [0.17, "#ffffff"], [0.31, "#f4f7fa"],
    [0.45, "#9aa1a9"], [0.57, "#ffffff"], [0.71, "#f9fbfd"], [0.86, "#cbd1d7"], [1, "#818890"]
  ], { x1: 0, y1: 0, x2: 1, y2: 0 });

  grad("gChromeV", "linearGradient", [
    [0, "#868d95"], [0.08, "#eaeeF1"], [0.2, "#ffffff"], [0.4, "#d0d6dc"],
    [0.55, "#8b929a"], [0.68, "#f4f7fa"], [0.85, "#ffffff"], [1, "#99a0a8"]
  ], { x1: 0, y1: 0, x2: 0, y2: 1 });

  grad("gChromeD", "linearGradient", [
    [0, "#90979f"], [0.13, "#ffffff"], [0.33, "#e8ecf0"], [0.51, "#92999f"],
    [0.69, "#f6f9fb"], [0.86, "#ffffff"], [1, "#8e959d"]
  ], { x1: 0, y1: 0, x2: 0.55, y2: 1 });

  /* the case flank: a wheel rim lit from the upper left */
  grad("gCase", "radialGradient", [
    [0.62, "#c2c8cf", 0], [0.72, "#f2f5f8", 0.75], [0.8, "#ffffff", 0.95],
    [0.89, "#f0f4f7", 0.95], [0.955, "#bcc3ca", 0.95], [0.99, "#8b939b", 0.95], [1, "#6c737b", 1]
  ], { cx: "30%", cy: "20%", r: "98%" });

  grad("gLugTop", "linearGradient", [
    [0, "#ffffff"], [0.3, "#f8fbfd"], [0.66, "#c4cad1"], [1, "#8e959d"]
  ], { x1: 0.15, y1: 0, x2: 0.85, y2: 1 });

  grad("gSatin", "linearGradient", [
    [0, "#c3cad1"], [0.18, "#f6f9fb"], [0.42, "#ffffff"], [0.64, "#f2f6f9"], [1, "#bcc3ca"]
  ], { x1: 0, y1: 0, x2: 1, y2: 0 });

  grad("gRing", "linearGradient", [
    [0, "#ffffff", 1], [0.16, "#ffffff", 1], [0.3, "#f7fafc", 0.9], [0.52, "#dfe5ea", 0.8],
    [0.72, "#c2c9d0", 0.8], [0.88, "#eef2f5", 0.9], [1, "#ffffff", 1]
  ], { x1: 0.06, y1: 0, x2: 0.94, y2: 1 });

  /* dial */
  grad("gDial", "radialGradient", [
    [0, "#3f9e70"], [0.18, "#328f60"], [0.4, "#277c53"], [0.6, "#1d6a46"],
    [0.78, "#15583a"], [0.92, "#10482f"], [1, "#0d3f28"]
  ], { cx: "42%", cy: "36%", r: "76%" });

  grad("gDialSheen", "radialGradient", [
    [0, "#d6f9e8", 0.36], [0.18, "#a6e6c6", 0.26], [0.4, "#54a87c", 0.12],
    [0.66, "#15603f", 0.07], [0.86, "#062a1a", 0.16], [1, "#03150e", 0.4]
  ], { cx: "30%", cy: "22%", r: "88%" });

  grad("gDialRim", "radialGradient", [
    [0, "#02110a", 0], [0.74, "#02110a", 0], [0.88, "#02100a", 0.26], [0.96, "#010c07", 0.5], [1, "#010a06", 0.78]
  ], { cx: "50%", cy: "50%", r: "50%" });

  grad("gDialCore", "radialGradient", [
    [0, "#37885f", 0.98], [0.55, "#2d7c54", 0.85], [1, "#24704b", 0]
  ], { cx: "44%", cy: "42%", r: "50%" });

  grad("gLinkBody", "linearGradient", [
    [0, "#8e969e"], [0.14, "#c9d0d6"], [0.34, "#e9eef2"], [0.56, "#f2f5f8"],
    [0.76, "#d6dce2"], [1, "#a2aab2"]
  ], { x1: 0, y1: 0, x2: 0.15, y2: 1 });

  grad("gBeadBody", "linearGradient", [
    [0, "#c8cfd6"], [0.12, "#eef2f5"], [0.3, "#fbfcfd"], [0.55, "#f2f5f7"],
    [0.8, "#dde3e8"], [0.93, "#c2c9d0"], [1, "#aab2ba"]
  ], { x1: 0, y1: 0, x2: 0.08, y2: 1 });

  grad("gEdgeBody", "linearGradient", [
    [0, "#6b747d"], [0.3, "#a8b0b8"], [0.6, "#cdd4da"], [1, "#8b939b"]
  ], { x1: 0, y1: 0, x2: 1, y2: 0.2 });

  grad("gApertureBevel", "linearGradient", [
    [0, "#ffffff", 0.85], [0.3, "#cfd6da", 0.5], [0.55, "#7d878d", 0.55],
    [0.78, "#c6ced3", 0.4], [1, "#ffffff", 0.6]
  ], { x1: 0, y1: 0, x2: 1, y2: 1 });

  grad("gApertureShade", "linearGradient", [
    [0, "#000000", 0.16], [1, "#000000", 0]
  ], { x1: 0, y1: 0, x2: 0, y2: 1 });

  grad("gPrint", "linearGradient", [
    [0, "#ffffff"], [0.4, "#ffffff"], [1, "#eaf7f0"]
  ], { x1: 0, y1: 0, x2: 0.25, y2: 1 });

  grad("gLume", "linearGradient", [
    [0, "#f2f7f4"], [0.22, "#fdfdfb"], [0.62, "#f1f7f3"], [1, "#dcebe2"]
  ], { x1: 0.1, y1: 0, x2: 0.4, y2: 1 });

  /* glass */
  grad("gGlare", "radialGradient", [
    [0, "#ffffff", 0.5], [0.36, "#ffffff", 0.18], [0.7, "#ffffff", 0.03], [1, "#ffffff", 0]
  ], { cx: "50%", cy: "50%", r: "50%" });
  grad("gGlareSoft", "radialGradient", [
    [0, "#ffffff", 0.24], [0.55, "#ffffff", 0.06], [1, "#ffffff", 0]
  ], { cx: "50%", cy: "50%", r: "50%" });

  grad("gLensTint", "radialGradient", [
    [0, "#ffffff", 0.0], [0.6, "#ffffff", 0.0], [0.86, "#dff3ea", 0.05], [0.96, "#8fc2ac", 0.1], [1, "#4f8a72", 0.22]
  ], { cx: "34%", cy: "28%", r: "80%" });

  grad("gLensRim", "linearGradient", [
    [0, "#ffffff", 0.9], [0.22, "#f2fbf6", 0.42], [0.46, "#8fb5a5", 0.16],
    [0.68, "#ffffff", 0.42], [0.86, "#c6ddd2", 0.2], [1, "#ffffff", 0.45]
  ], { x1: 0.12, y1: 0, x2: 0.88, y2: 1 });

  grad("gDisc", "linearGradient", [
    [0, "#ffffff"], [0.3, "#fefefb"], [0.72, "#f6f7f2"], [1, "#e8ebe3"]
  ], { x1: 0, y1: 0, x2: 0.24, y2: 1 });

  grad("gRowShade", "linearGradient", [
    [0, "#ffffff", 0.5], [0.08, "#ffffff", 0.24], [0.26, "#ffffff", 0.02],
    [0.56, "#000000", 0.04], [0.84, "#000000", 0.26], [1, "#000000", 0.08]
  ], { x1: 0, y1: 0, x2: 0, y2: 1 });

  grad("gEdgeShade", "linearGradient", [
    [0, "#8a929a", 0.55], [0.35, "#b9c0c7", 0.22], [0.62, "#ffffff", 0.3], [1, "#7f878f", 0.5]
  ], { x1: 0, y1: 0, x2: 1, y2: 0 });

  grad("gBead", "linearGradient", [
    [0, "#000000", 0.34], [0.12, "#000000", 0.06], [0.3, "#ffffff", 0.34], [0.46, "#ffffff", 0.5],
    [0.62, "#ffffff", 0.3], [0.86, "#000000", 0.1], [1, "#000000", 0.36]
  ], { x1: 0, y1: 0, x2: 1, y2: 0 });

  grad("gCenterLink", "linearGradient", [
    [0, "#6f767e"], [0.1, "#eef2f5"], [0.26, "#ffffff"], [0.44, "#f4f7fa"],
    [0.56, "#a4abb3"], [0.66, "#ffffff"], [0.82, "#eef2f5"], [1, "#767d85"]
  ], { x1: 0, y1: 0, x2: 1, y2: 0 });

  grad("gCrownCap", "linearGradient", [
    [0, "#939ba3"], [0.15, "#f9fcfd"], [0.36, "#ffffff"], [0.58, "#ccd2d9"], [0.8, "#f5f8fa"], [1, "#9aa2aa"]
  ], { x1: 0, y1: 0, x2: 1, y2: 0.2 });

  filter("fHandShadow", [
    E("feDropShadow", { dx: 2, dy: 3, stdDeviation: 1.8, "flood-color": "#03150e", "flood-opacity": 0.55 })
  ], { x: "-80%", y: "-80%", width: "280%", height: "280%" });
  filter("fBlur1", [E("feGaussianBlur", { stdDeviation: 1 })]);
  filter("fBlur2", [E("feGaussianBlur", { stdDeviation: 2 })]);
  filter("fBlur3", [E("feGaussianBlur", { stdDeviation: 3.4 })]);
  filter("fBlur6", [E("feGaussianBlur", { stdDeviation: 6 })]);
  filter("fBlur16", [E("feGaussianBlur", { stdDeviation: 16 })]);

  const brushPat = E("pattern", { id: "pBrush", width: 110, height: 300, patternUnits: "userSpaceOnUse" }, defs);
  E("image", { href: "assets/brush.png", x: 0, y: 0, width: 110, height: 300, preserveAspectRatio: "none" }, brushPat);

  const grainPat = E("pattern", { id: "pGrain", width: 160, height: 160, patternUnits: "userSpaceOnUse" }, defs);
  E("image", { href: "assets/grain.png", x: 0, y: 0, width: 160, height: 160, preserveAspectRatio: "none" }, grainPat);

  /* a soft shadow pooled under the watch head: it has to sit below the
     bracelet, otherwise it reads as a brown smudge on the links */
  const shadowG = E("g", { id: "castShadow" }, svg);
  E("ellipse", {
    cx: CX, cy: CY + R.case * 0.86, rx: R.case * 1.02, ry: R.case * 0.34,
    fill: "#57524a", opacity: 0.13, filter: "url(#fBlur16)"
  }, shadowG);

  /* =============================================================== BRACELET */
  const braceletG = E("g", { id: "bracelet" }, svg);

  /* Jubilee weave, seen from the front:
       · three polished round "beads" in the centre, staggered half a pitch
       · wide satin outer links on both sides
       · a narrow polished chamfer at the outer edge
     Each row overlaps the next so the metal reads as a continuous ribbon. */
  const COLS = [
    { a: -1.000, b: -0.400, kind: "outer", side: -1 },
    { a: -0.390, b: -0.205, kind: "bead" },
    { a: -0.185, b: 0.185, kind: "bead" },
    { a: 0.205, b: 0.390, kind: "bead" },
    { a: 0.400, b: 1.000, kind: "outer", side: 1 }
  ];

  const hwAt = (y) => {
    const d = Math.abs(y - CY);
    const span = TOP_END < CY ? CY - TOP_END : BOT_END - CY;
    /* it leaves the case at 125.5 and narrows to ~92 at the clasp end */
    return BR_HALF - (BR_HALF - BR_TIP) * Math.pow(clamp((d - R.case) / span, 0, 1), 1.7);
  };

  function silhouette(top) {
    const yNear = top ? CY - R.case + 10 : CY + R.case - 10;
    const yFar = top ? TOP_END : BOT_END;
    const cap = 13;
    const hwN = hwAt(yNear), hwF = hwAt(yFar);
    return top
      ? `M ${CX - hwN} ${yNear} L ${CX - hwF} ${yFar + cap} A ${hwF} ${cap} 0 0 1 ${CX + hwF} ${yFar + cap} L ${CX + hwN} ${yNear} Z`
      : `M ${CX - hwN} ${yNear} L ${CX - hwF} ${yFar - cap} A ${hwF} ${cap} 0 0 0 ${CX + hwF} ${yFar - cap} L ${CX + hwN} ${yNear} Z`;
  }

  /* One link.  In the reference the Jubilee centre is a run of flat polished
     bricks separated by near-black joints, while the outer links are satin
     with a strong vertical grain.  Everything overlaps by a hair so no
     background shows through. */
  function link(x1, x2, y, h, kind) {
    const w = x2 - x1;
    if (w < 0.8 || h < 1) return;
    const g = E("g", {}, this.parent);
    const bead = kind === "bead";
    const rx = bead ? 1.5 : 1.1;
    const inset = bead ? 0.7 : 0.45;

    /* body */
    const body = bead ? "url(#gBeadBody)" : kind === "edge" ? "url(#gEdgeBody)" : "url(#gLinkBody)";
    E("rect", {
      x: P(x1 + inset), y: P(y + inset),
      width: Math.max(0, P(w - inset * 2)), height: Math.max(0, P(h - inset * 1.2)),
      rx, fill: body
    }, g);
    E("rect", {
      x: P(x1 + inset), y: P(y + inset),
      width: Math.max(0, P(w - inset * 2)), height: Math.max(0, P(h - inset * 1.2)),
      rx, fill: "url(#pBrush)", opacity: bead ? 0.06 : 0.36, style: "mix-blend-mode:overlay"
    }, g);

    if (bead) {
      /* a flat polished brick: bright crest, hairline bevel all round */
      E("rect", {
        x: P(x1 + inset), y: P(y + inset), width: Math.max(0, P(w - inset * 2)),
        height: 0.9, fill: "#ffffff", opacity: 0.9
      }, g);
      E("rect", {
        x: P(x1 + inset), y: P(y + inset), width: Math.max(0, P(w - inset * 2)),
        height: Math.max(0, P(h - inset * 1.2)), rx,
        fill: "none", stroke: "#4c565f", "stroke-opacity": 0.4, "stroke-width": 0.5
      }, g);
      /* the left end catches the light, the right end turns away */
      E("rect", {
        x: P(x1 + inset), y: P(y + inset), width: Math.max(0.6, P(w * 0.09)),
        height: Math.max(0, P(h - inset * 1.2)), rx, fill: "#ffffff", opacity: 0.5
      }, g);
      E("rect", {
        x: P(x2 - inset - Math.max(0.6, P(w * 0.09))), y: P(y + inset),
        width: Math.max(0.6, P(w * 0.09)), height: Math.max(0, P(h - inset * 1.2)),
        rx, fill: "#7d8791", opacity: 0.45
      }, g);
    } else {
      /* one wide satin link: a bright crest just below its top edge, polish
         along the outer rim, and a shadow where it rolls away at the bottom */
      E("rect", {
        x: P(x1 + inset + 0.4), y: P(y + inset + 0.3),
        width: Math.max(0, P(w - inset * 2 - 0.8)), height: 1.6,
        fill: "#ffffff", opacity: 0.62
      }, g);
      E("rect", {
        x: P(x1 + inset + 0.4), y: P(y + h * 0.62),
        width: Math.max(0, P(w - inset * 2 - 0.8)), height: Math.max(0, P(h * 0.26)),
        fill: "#7d868f", opacity: 0.3
      }, g);
      /* polished chamfer along the outer rim */
      const cw = Math.max(1.6, w * 0.07);
      E("rect", {
        x: this.side < 0 ? P(x1) : P(x2) - cw, y: P(y + 0.4), width: cw,
        height: Math.max(0, P(h - 0.8)), fill: "url(#gEdgeBody)"
      }, g);
      E("rect", {
        x: this.side < 0 ? P(x1) : P(x2) - cw, y: P(y + 0.4), width: cw,
        height: Math.max(0, P(h - 0.8)), fill: "#20282e", opacity: 0.22
      }, g);
    }

    /* the joint: a deep shadow that separates the bricks */
    E("rect", { x: P(x1), y: P(y + h), width: P(w), height: 1.5, fill: "#0a0f13", opacity: 0.82 }, g);
    E("rect", { x: P(x1), y: P(y + h + 1.5), width: P(w), height: 0.6, fill: "#ffffff", opacity: 0.32 }, g);
  }

  ["top", "bottom"].forEach((which) => {
    const top = which === "top";
    const cid = "clipBr" + which;
    clip(cid, E("path", { d: silhouette(top) }));

    const g = E("g", { "clip-path": `url(#${cid})` }, braceletG);
    const lit = E("g", { class: "lit" }, g);

    /* the metal behind the joints */
    E("path", { d: silhouette(top), fill: "#9aa2aa" }, lit);

    const rows = [];
    {
      const yNear = top ? CY - R.case + 6 : CY + R.case - 6;
      let y = top ? TOP_END : BOT_END - 26;
      let pitch = 33;
      let guard = 0;
      while ((top ? y < yNear : y > yNear - 26) && guard++ < 60) {
        rows.push({ y, p: pitch });
        y += top ? pitch : -pitch;
        pitch *= 1.004;
      }
    }

    rows.forEach((row) => {
      COLS.forEach((c) => {
        const stagger = c.stagger ? (top ? row.p / 2 : -row.p / 2) : 0;
        const y = row.y + stagger;
        const hw = hwAt(row.y);
        const x1 = CX + c.a * hw, x2 = CX + c.b * hw;
        /* rows overlap by 0.6 so no hairline background shows through */
        link.call({ parent: lit, side: c.side || 0 }, x1, x2, y, row.p - 0.4, c.kind);
      });
    });

    /* cylindrical shading: the band is brighter in the middle */
    const y0 = top ? TOP_END : CY - 130;
    const y1 = top ? CY + 130 : BOT_END;
    grad("gCyl" + which, "linearGradient", [
      [0, "#0d1512", 0.34], [0.05, "#0d1512", 0.1], [0.16, "#ffffff", 0.06], [0.32, "#ffffff", 0.16],
      [0.5, "#ffffff", 0.22], [0.68, "#ffffff", 0.16], [0.84, "#ffffff", 0.06], [0.95, "#0d1512", 0.1], [1, "#0d1512", 0.34]
    ], { x1: 0, y1: 0, x2: 1, y2: 0 });
    E("rect", { x: CX - BR_HALF, y: y0, width: BR_HALF * 2, height: y1 - y0,
                fill: `url(#gCyl${which})`, style: "mix-blend-mode:soft-light" }, lit);

    /* ambient occlusion where the band dives under the case */
    grad("gAO" + which, "linearGradient", top
      ? [[0, "#04100b", 0], [0.62, "#04100b", 0], [1, "#04100b", 0.38]]
      : [[0, "#04100b", 0.38], [0.38, "#04100b", 0], [1, "#04100b", 0]],
      { x1: 0, y1: 0, x2: 0, y2: 1 });
    E("rect", { x: CX - BR_HALF, y: y0, width: BR_HALF * 2, height: y1 - y0, fill: `url(#gAO${which})` }, lit);
  });

  /* ================================================================== CASE */
  const caseG = E("g", { id: "case" }, svg);

  /* soft bumpy silhouette: round drum + four lugs that barely peep out */
  function caseOutline(scale) {
    const pts = [];
    const N = 300;
    for (let i = 0; i <= N; i++) {
      const a = (i / N) * 360;
      let rr = R.case;
      [30, 150, 210, 330].forEach((c) => {
        const d = Math.min(Math.abs(a - c), 360 - Math.abs(a - c));
        rr += (R.lug - R.case) * Math.exp(-Math.pow(d / 15, 2));
      });
      rr *= (scale || 1);
      pts.push(polar(rr, a));
    }
    return "M" + pts.map((p) => P(p[0]) + " " + P(p[1])).join(" L ") + " Z";
  }

  

  const caseLit = E("g", { class: "lit" }, caseG);

  /* contact shadow of the case onto the bracelet */
  E("circle", {
    cx: CX, cy: CY + 3, r: R.case + 1.5, fill: "none",
    stroke: "#0b1613", "stroke-width": 4, "stroke-opacity": 0.16, filter: "url(#fBlur2)"
  }, caseLit);

  /* drum */
  E("circle", { cx: CX, cy: CY, r: R.case, fill: "url(#gCase)" }, caseLit);
  E("circle", { cx: CX, cy: CY, r: R.case, fill: "url(#pBrush)", opacity: 0.22, style: "mix-blend-mode:overlay" }, caseLit);
  E("circle", { cx: CX, cy: CY, r: R.case, fill: "none", stroke: "#6c737b", "stroke-opacity": 0.55, "stroke-width": 1 }, caseLit);
  E("circle", { cx: CX, cy: CY, r: R.case - 1.4, fill: "none", stroke: "#ffffff", "stroke-opacity": 0.4, "stroke-width": 1.2 }, caseLit);

  /* four lugs: bright wedges that emerge from the case at 1:30 / 4:30 / … */
  const lugG = E("g", { id: "lugs" }, caseLit);
  [30, 150, 210, 330].forEach((ang) => {
    const g = E("g", { transform: `rotate(${ang} ${CX} ${CY})` }, lugG);
    /* a tapered horn, rounded at the tip, sunk into the case */
    E("path", {
      d: `M ${CX - 23} ${CY - R.case + 24}
          Q ${CX - 21} ${CY - R.case + 1} ${CX - 10} ${CY - R.case - 3}
          Q ${CX} ${CY - R.case - 4.5} ${CX + 10} ${CY - R.case - 3}
          Q ${CX + 21} ${CY - R.case + 1} ${CX + 23} ${CY - R.case + 24} Z`,
      fill: "url(#gLugTop)"
    }, g);
    E("path", {
      d: `M ${CX - 23} ${CY - R.case + 24}
          Q ${CX - 21} ${CY - R.case + 1} ${CX - 10} ${CY - R.case - 3}
          Q ${CX} ${CY - R.case - 4.5} ${CX + 10} ${CY - R.case - 3}
          Q ${CX + 21} ${CY - R.case + 1} ${CX + 23} ${CY - R.case + 24} Z`,
      fill: "none", stroke: "#616870", "stroke-opacity": 0.45, "stroke-width": 0.8
    }, g);
    /* the lit leading edge */
    E("path", {
      d: `M ${CX - 22} ${CY - R.case + 20}
          Q ${CX - 20} ${CY - R.case + 2} ${CX - 10} ${CY - R.case - 2.5}
          Q ${CX - 15} ${CY - R.case + 6} ${CX - 16} ${CY - R.case + 22} Z`,
      fill: "#ffffff", opacity: 0.7
    }, g);
    /* the shaded trailing edge */
    E("path", {
      d: `M ${CX + 22} ${CY - R.case + 20}
          Q ${CX + 20} ${CY - R.case + 2} ${CX + 10} ${CY - R.case - 2.5}
          Q ${CX + 15} ${CY - R.case + 6} ${CX + 16} ${CY - R.case + 22} Z`,
      fill: "#7d848c", opacity: 0.4
    }, g);
    /* a soft shadow the horn drops on the bracelet */
    E("path", {
      d: `M ${CX - 24} ${CY - R.case + 28} L ${CX + 24} ${CY - R.case + 28} L ${CX + 22} ${CY - R.case + 42} L ${CX - 22} ${CY - R.case + 42} Z`,
      fill: "#0d1713", opacity: 0.2, filter: "url(#fBlur3)"
    }, g);
  });

  /* polished ring between the flutes and the case edge */
  const ringPath = `M ${CX - R.ring} ${CY} A ${R.ring} ${R.ring} 0 1 0 ${CX + R.ring} ${CY} A ${R.ring} ${R.ring} 0 1 0 ${CX - R.ring} ${CY} Z
    M ${CX - R.bezelOut - 0.4} ${CY} A ${R.bezelOut + 0.4} ${R.bezelOut + 0.4} 0 1 1 ${CX + R.bezelOut + 0.4} ${CY} A ${R.bezelOut + 0.4} ${R.bezelOut + 0.4} 0 1 1 ${CX - R.bezelOut - 0.4} ${CY} Z`;
  E("path", { d: ringPath, fill: "#dfe4e9", "fill-rule": "evenodd" }, caseLit);
  E("path", { d: ringPath, fill: "url(#gRing)", "fill-rule": "evenodd" }, caseLit);
  E("path", { d: ringPath, fill: "url(#pBrush)", "fill-rule": "evenodd", opacity: 0.26, style: "mix-blend-mode:overlay" }, caseLit);
  E("path", { d: ringPath, fill: "none", stroke: "#585f67", "stroke-opacity": 0.5, "stroke-width": 0.7 }, caseLit);

  /* ================================================================= BEZEL */
  const bezel = E("g", { id: "bezel" }, caseLit);
  E("circle", { cx: CX, cy: CY, r: R.bezelOut + 0.3, fill: "#394047" }, bezel);

  const FL = 60;
  const step = 360 / FL;
  const midR = (R.bezelIn + R.bezelOut) / 2;

  for (let i = 0; i < FL; i++) {
    const aL = i * step;
    const aM = aL + step / 2;
    const aR = aL + step;

    const nLx = Math.cos(rad(aL + step * 0.24)), nLy = Math.sin(rad(aL + step * 0.24));
    const nRx = Math.cos(rad(aR - step * 0.24)), nRy = Math.sin(rad(aR - step * 0.24));
    const lamL = nLx * LIGHT.x + nLy * LIGHT.y;
    const lamR = nRx * LIGHT.x + nRy * LIGHT.y;

    /* soft specular response of a polished bevel: always bright, the seams
       and the rims provide the contrast */
    const f = (l) => 192 + 63 * Math.pow(clamp((l + 1) / 2, 0, 1), 1.15);
    const tL = f(lamL);
    const tR = f(lamR);

    const ridgeOut = R.bezelOut - 0.2;
    const ridgeIn = R.bezelIn + 0.2;

    const [lx0, ly0] = polar(ridgeIn, aL);
    const [lx1, ly1] = polar(ridgeOut, aL);
    const [mx0, my0] = polar(ridgeOut, aM);
    const [mx1, my1] = polar(ridgeIn, aM);
    const [rx0, ry0] = polar(ridgeIn, aR);
    const [rx1, ry1] = polar(ridgeOut, aR);

    /* radial falloff: the flute is a curved prism, darker at both rims */
    const gid = "fl" + i, gidR = "flr" + i;
    const fall = (t) => [
      [0, steel(t * 0.66)], [0.1, steel(t * 0.84)], [0.22, steel(t * 1.0)],
      [0.46, steel(Math.min(255, t * 1.1))], [0.62, steel(Math.min(255, t * 1.04))],
      [0.84, steel(t * 0.9)], [1, steel(t * 0.7)]
    ];
    grad(gid, "radialGradient", fall(tL), { gradientUnits: "userSpaceOnUse", cx: P(CX), cy: P(CY), r: P(R.bezelOut) });
    grad(gidR, "radialGradient", fall(tR), { gradientUnits: "userSpaceOnUse", cx: P(CX), cy: P(CY), r: P(R.bezelOut) });

    const gid2 = "flh" + i;
    /* each flute is a pair of facets meeting in a bright crest; the crest sits
       slightly outboard so the bevel catches the light like the real one */
    grad(gid2, "radialGradient", [
      [0, steel(tL * 0.5)], [0.12, steel(tL * 0.74)], [0.34, steel(tL * 0.96)],
      [0.56, steel(Math.min(255, tL * 1.1))], [0.72, steel(tL * 0.98)],
      [0.9, steel(tL * 0.76)], [1, steel(tL * 0.52)]
    ], { gradientUnits: "userSpaceOnUse", cx: P(CX), cy: P(CY), r: P(R.bezelOut) });
    E("path", {
      d: `M ${P(lx0)} ${P(ly0)} L ${P(lx1)} ${P(ly1)} L ${P(mx0)} ${P(my0)} L ${P(mx1)} ${P(my1)} Z`,
      fill: `url(#${gid2})`
    }, bezel);
    E("path", {
      d: `M ${P(mx1)} ${P(my1)} L ${P(mx0)} ${P(my0)} L ${P(rx1)} ${P(ry1)} L ${P(rx0)} ${P(ry0)} Z`,
      fill: `url(#${gidR})`
    }, bezel);

    E("line", {
      x1: P(mx1), y1: P(my1), x2: P(mx0), y2: P(my0),
      stroke: "#ffffff", "stroke-opacity": 0.42 + 0.4 * clamp((lamL + 1) / 2, 0, 1), "stroke-width": 0.7
    }, bezel);
    E("line", {
      x1: P(lx0), y1: P(ly0), x2: P(lx1), y2: P(ly1),
      stroke: "#12181d", "stroke-opacity": 0.44, "stroke-width": 0.75
    }, bezel);
  }

  /* outer and inner polished rims */
  E("circle", { cx: CX, cy: CY, r: R.bezelOut, fill: "none", stroke: "url(#gChromeH)", "stroke-width": 2.6 }, bezel);
  E("circle", { cx: CX, cy: CY, r: R.bezelOut - 1.2, fill: "none", stroke: "#0f151a", "stroke-opacity": 0.35, "stroke-width": 0.9 }, bezel);
  E("circle", { cx: CX, cy: CY, r: R.bezelIn, fill: "none", stroke: "url(#gChromeH)", "stroke-width": 1.8 }, bezel);
  E("circle", { cx: CX, cy: CY, r: R.bezelIn + 1, fill: "none", stroke: "#082018", "stroke-opacity": 0.45, "stroke-width": 1 }, bezel);

  /* broad specular sweep across the flutes */
  grad("gBezelSheen", "linearGradient", [
    [0, "#ffffff", 0], [0.24, "#ffffff", 0.08], [0.42, "#ffffff", 0.2], [0.58, "#ffffff", 0.08], [0.8, "#ffffff", 0.01], [1, "#ffffff", 0]
  ], { x1: 0.06, y1: 0, x2: 0.94, y2: 1 });
  E("circle", { cx: CX, cy: CY, r: R.bezelOut, fill: "url(#gBezelSheen)", style: "mix-blend-mode:screen" }, bezel);
  /* a whisper of grain so it doesn't look like vector art */
  E("circle", { cx: CX, cy: CY, r: R.bezelOut, fill: "url(#pGrain)", opacity: 0.18, style: "mix-blend-mode:overlay" }, bezel);

  /* ================================================================ REHAUT */
  const rehaut = E("g", { id: "rehaut", class: "lit" }, E("g", {}, svg));
  grad("gFlange", "radialGradient", [
    [0, "#93a89d", 0], [0.78, "#7e978b", 0], [0.86, "#5d786a", 0.4], [0.9, "#8ba498", 0.45],
    [0.94, "#d4e1d8", 0.5], [0.97, "#f0f6f2", 0.8], [1, "#ffffff", 0.95]
  ], { cx: "42%", cy: "34%", r: "58%" });
  grad("gFlangeSheen", "linearGradient", [
    [0, "#ffffff", 0.45], [0.28, "#ffffff", 0.05], [0.54, "#000000", 0.14], [0.8, "#ffffff", 0.08], [1, "#000000", 0.28]
  ], { x1: 0.1, y1: 0, x2: 0.9, y2: 1 });
  E("circle", { cx: CX, cy: CY, r: R.flange, fill: "url(#gFlange)" }, rehaut);
  E("circle", { cx: CX, cy: CY, r: R.flange, fill: "url(#gFlangeSheen)", style: "mix-blend-mode:overlay" }, rehaut);
  E("circle", { cx: CX, cy: CY, r: R.flange - 0.4, fill: "none", stroke: "#ffffff", "stroke-opacity": 0.55, "stroke-width": 0.8 }, rehaut);
  E("circle", { cx: CX, cy: CY, r: R.dial + 0.6, fill: "none", stroke: "#04170f", "stroke-opacity": 0.45, "stroke-width": 1.2 }, rehaut);

  E("path", {
    id: "engrPath",
    d: `M ${CX - R.dial - 4.2} ${CY} A ${R.dial + 4.2} ${R.dial + 4.2} 0 1 1 ${CX + R.dial + 4.2} ${CY} A ${R.dial + 4.2} ${R.dial + 4.2} 0 1 1 ${CX - R.dial - 4.2} ${CY} Z`,
    fill: "none"
  }, rehaut);
  const engr = E("text", {
    "font-family": "Manrope, sans-serif", "font-size": 3.4, "font-weight": 700,
    "letter-spacing": 1.5, fill: "#ffffff", "fill-opacity": 0.1, class: "rehaut-engr"
  }, rehaut);
  E("textPath", { href: "#engrPath", startOffset: "0.2%" }, engr).textContent = "ROLEX  ".repeat(18);

  /* ================================================================== DIAL */
  const dialG = E("g", { id: "dial" }, svg);
  const dialLit = E("g", { class: "lit" }, dialG);

  /* the face is its own group: it is the part that darkens at night */
  const dialFace = E("g", { id: "dialFace" }, dialLit);
  E("circle", { cx: CX, cy: CY, r: R.dial, fill: "url(#gDial)" }, dialFace);
  clip("clipDial", E("circle", { cx: CX, cy: CY, r: R.dial }));
  const rays = E("g", { "clip-path": "url(#clipDial)" }, dialFace);

  const RAYS = 360;
  for (let i = 0; i < RAYS; i++) {
    const a0 = (i * 360) / RAYS;
    const a1 = a0 + 360 / RAYS + 0.6;
    const [x1, y1] = polar(R.dial + 3, a0);
    const [x2, y2] = polar(R.dial + 3, a1);
    const nx = Math.cos(rad(a0));
    const ny = Math.sin(rad(a0));
    const lam = nx * LIGHT.x + ny * LIGHT.y;
    const j = ((i * 2654435761) % 1000) / 1000;
    const o = clamp(0.01 + 0.052 * Math.pow(clamp((lam + 1) / 2, 0, 1), 2.6) + 0.014 * j, 0, 0.09);
    E("path", {
      d: `M ${CX} ${CY} L ${P(x1)} ${P(y1)} L ${P(x2)} ${P(y2)} Z`,
      fill: j > 0.5 ? "#ffffff" : "#01100a",
      opacity: P(o)
    }, rays);
  }

  E("circle", { cx: CX, cy: CY, r: 48, fill: "url(#gDialCore)" }, dialFace);
  E("circle", { cx: CX, cy: CY, r: R.dial, fill: "url(#gDialSheen)", style: "mix-blend-mode:screen", opacity: 0.62 }, dialFace);
  E("circle", { cx: CX, cy: CY, r: R.dial, fill: "url(#gDialRim)" }, dialFace);

  /* minute track */
  const track = E("g", { id: "minuteTrack" }, dialFace);
  for (let i = 0; i < 60; i++) {
    const ang = i * 6;
    const isHour = i % 5 === 0;
    /* fine printed hashes sit just inside the flange, the five-minute
       markers are longer and brighter */
    const [x1, y1] = polar(R.dial - 2.6, ang);
    const [x2, y2] = polar(R.dial - (isHour ? 10.4 : 6.2), ang);
    E("line", {
      x1: P(x1), y1: P(y1), x2: P(x2), y2: P(y2),
      stroke: "#f7fdfa", "stroke-opacity": isHour ? 0.95 : 0.55,
      "stroke-width": isHour ? 2.4 : 0.85, "stroke-linecap": "round"
    }, track);
  }
  /* a hairline chapter ring separating track and flange */
  E("circle", {
    cx: CX, cy: CY, r: P(R.dial - 1.1), fill: "none",
    stroke: "#eafff5", "stroke-opacity": 0.5, "stroke-width": 0.7
  }, track);

  /* applied batons */
  const idxG = E("g", { id: "indices" }, dialLit);

  function baton(hour) {
    const L = 23, W1 = 7.2, W2 = 4.6, rIn = 97;
    const g = E("g", { transform: `rotate(${hour * 30} ${CX} ${CY})` }, idxG);
    const x = CX;
    const yTop = CY - rIn - L;
    const yBot = CY - rIn;
    const gid = `idx${hour}`;
    /* the frame is a brushed white-gold block, seen almost head-on */
    grad(gid, "linearGradient", [
      [0, "#d9dee3"], [0.1, "#ffffff"], [0.34, "#f4f7f9"], [0.62, "#e2e7eb"], [0.86, "#ffffff"], [1, "#cdd3d9"]
    ], { x1: 0, y1: 0, x2: 1, y2: 0.2 });
    grad(gid + "h", "linearGradient", [
      [0, "#8d949c"], [0.16, "#eef2f5"], [0.5, "#ffffff"], [0.84, "#e9edf0"], [1, "#98a0a8"]
    ], { x1: 0, y1: 0, x2: 1, y2: 0 });

    /* tight drop shadow, lower-right */
    const metalWrap = g;
    E("rect", {
      x: P(x - W1 + 1.5), y: P(yTop + 2), width: P(W1 * 2), height: P(L + 0.5),
      fill: "#02120b", opacity: 0.34, filter: "url(#fBlur1)"
    }, g);

    /* body with a beveled top edge */
    E("path", { class: "idx-metal",
      d: `M ${P(x - W1)} ${P(yBot)} L ${P(x - W1)} ${P(yTop + 2.4)} Q ${P(x - W1 + 1.6)} ${P(yTop + 0.4)} ${P(x - W1 + 3)} ${P(yTop)}
          L ${P(x + W1 - 3)} ${P(yTop)} Q ${P(x + W1 - 1.6)} ${P(yTop + 0.4)} ${P(x + W1)} ${P(yTop + 2.4)} L ${P(x + W1)} ${P(yBot)} Z`,
      fill: `url(#${gid}h)`
    }, g);
    /* the bright facet on the upper left half */
    E("path", { class: "idx-metal",
      d: `M ${P(x - W1 + 0.6)} ${P(yBot)} L ${P(x - W1 + 0.6)} ${P(yTop + 2.8)} Q ${P(x - W1 + 2)} ${P(yTop + 1)} ${P(x - W1 + 3.4)} ${P(yTop + 0.8)}
          L ${P(x)} ${P(yTop + 0.8)} L ${P(x)} ${P(yBot)} Z`,
      fill: "#ffffff", opacity: 0.5
    }, g);
    /* lume inlay, inset so the metal frame stays visible on all four sides */
    E("rect", {
      x: P(x - W2 / 2), y: P(yTop + 3.4), width: P(W2), height: P(L - 5.2), rx: 0.8,
      fill: "url(#gLume)", class: "lume"
    }, g);
    /* the frame's inner bevel catches a dark line, the top catches light */
    E("rect", {
      x: P(x - W2 / 2), y: P(yTop + 3.4), width: P(W2), height: P(L - 5.2), rx: 0.8,
      fill: "none", stroke: "#6f7d75", "stroke-opacity": 0.45, "stroke-width": 0.6
    }, g);
    E("rect", {
      x: P(x - W1) + 0.4, y: P(yTop) + 0.4, width: P(W1 * 2) - 0.8, height: 1.5,
      fill: "#ffffff", opacity: 0.85, class: "idx-metal"
    }, g);
    /* bottom edge catches a thin highlight */
    E("rect", { x: P(x - W1), y: P(yBot) - 0.9, width: P(W1 * 2), height: 0.9, fill: "#ffffff", opacity: 0.5 }, g);
  }

  for (let h = 1; h <= 12; h++) if (h !== 3) baton(h);
  /* the aperture takes the place of the 3 o'clock baton */

  /* print */
  const print = E("g", { id: "dialPrint", class: "dial-print" }, dialLit);
  const serif = "Cormorant Garamond, 'Times New Roman', serif";

  function txt(str, y, size, opts) {
    const o = Object.assign({ weight: 600, ls: 1, opacity: 0.97, x: CX, family: serif }, opts || {});
    const t = E("text", {
      x: o.x, y: P(y), "text-anchor": "middle", "font-family": o.family,
      "font-size": size, "font-weight": o.weight, "letter-spacing": o.ls,
      fill: "url(#gPrint)", opacity: o.opacity
    }, print);
    t.textContent = str;
    return t;
  }

  function coronet(parent, scale) {
    const g = E("g", { transform: `scale(${scale || 1})` }, parent);
    E("path", {
      d: "M -12.6 3.4 L -9.8 -7.4 L -6.2 2.9 L -3.4 -9.1 L 0 2.1 L 3.4 -9.1 L 6.2 2.9 L 9.8 -7.4 L 12.6 3.4 Z",
      fill: "url(#gPrint)"
    }, g);
    E("path", {
      d: "M -12.6 3.4 L -12.2 7.1 Q 0 10.2 12.2 7.1 L 12.6 3.4 Q 0 7.1 -12.6 3.4 Z",
      fill: "url(#gPrint)"
    }, g);
    [[-9.8, -7.2, 1.2], [-4.9, -9.9, 1.5], [0, -10.3, 1.65], [4.9, -9.9, 1.5], [9.8, -7.2, 1.2]].forEach((c) => {
      E("circle", { cx: c[0], cy: c[1], r: c[2], fill: "url(#gPrint)" }, g);
    });
    return g;
  }

  coronet(E("g", { transform: `translate(${CX - 4} ${CY - 88})` }, print), 0.95);

  txt("ROLEX", CY - 59, 19.5, { x: CX - 5, ls: 4.2, weight: 500 });
  txt("OYSTER PERPETUAL", CY - 43, 7.4, { x: CX - 11, ls: 1.9, weight: 600, opacity: 1 });
  txt("DATEJUST", CY - 33.8, 7.4, { x: CX - 11, ls: 1.9, weight: 600, opacity: 1 });

  txt("SUPERLATIVE CHRONOMETER", CY + 58, 5.4, { x: CX - 8, ls: 1.35, weight: 700, opacity: 0.95 });
  txt("OFFICIALLY CERTIFIED", CY + 65.4, 5.4, { x: CX - 8, ls: 1.35, weight: 700, opacity: 0.95 });

  txt("SWISS", CY + 96, 5.4, { x: CX - 17, ls: 0.9, weight: 700, opacity: 0.95 });
  txt("MADE", CY + 96, 5.4, { x: CX + 17, ls: 0.9, weight: 700, opacity: 0.95 });
  coronet(E("g", { transform: `translate(${CX} ${CY + 93.8})` }, print), 0.2);

  /* =========================================================== DATE WINDOW */
  const dateG = E("g", { id: "dateWindow" }, svg);

  function plate(parent, x, y, w, h, font, cell, idSuffix, K, withFrame) {
    const g = E("g", {}, parent);
    const s = K || 1;
    const frame = withFrame !== false;

    if (frame) {
    E("rect", {
      x: P(x - w / 2 - 3.2 * s), y: P(y - h / 2 - 3.2 * s),
      width: P(w + 6.4 * s), height: P(h + 6.4 * s), rx: P(6 * s),
      fill: "#02130c", opacity: 0.26, filter: "url(#fBlur1)"
    }, g);
    E("rect", {
      x: P(x - w / 2 - 1.9 * s), y: P(y - h / 2 - 1.9 * s),
      width: P(w + 3.8 * s), height: P(h + 3.8 * s), rx: P(4.2 * s),
      fill: "none", stroke: "url(#gChromeH)", "stroke-width": P(1.5 * s)
    }, g);
    E("rect", {
      x: P(x - w / 2 - 0.9 * s), y: P(y - h / 2 - 0.9 * s),
      width: P(w + 1.8 * s), height: P(h + 1.8 * s), rx: P(3.6 * s),
      fill: "none", stroke: "#3f4842", "stroke-opacity": 0.45, "stroke-width": P(0.8 * s)
    }, g);

    /* the bevel of the aperture: light from the top left, shadow bottom right */
    E("rect", {
      x: P(x - w / 2), y: P(y - h / 2), width: P(w), height: P(h), rx: P(2.6 * s),
      fill: "none", stroke: "url(#gApertureBevel)", "stroke-width": P(1.5 * s)
    }, g);
    }

    const cid = "clipD" + idSuffix;
    clip(cid, E("rect", { x: P(x - w / 2), y: P(y - h / 2), width: P(w), height: P(h), rx: P(2.6 * s) }));
    const win = E("g", { "clip-path": `url(#${cid})` }, g);
    const slide = E("g", { id: "dateStrip" + idSuffix }, win);

    E("rect", {
      x: P(x - w / 2), y: P(y - h / 2), width: P(w), height: P(cell * 31 + h),
      fill: "url(#gDisc)"
    }, slide);

    for (let n = 1; n <= 31; n++) {
      const t = E("text", {
        x: P(x), y: P(y + (n - 1) * cell + font * 0.36), "text-anchor": "middle",
        "font-family": "Manrope, sans-serif", "font-size": P(font), "font-weight": 600,
        fill: "#151b17", "letter-spacing": P(0.3 * s)
      }, slide);
      t.textContent = String(n);
    }

    E("rect", {
      x: P(x - w / 2), y: P(y - h / 2), width: P(w), height: P(h), rx: P(2.6 * s),
      fill: "none", stroke: "#8f9c93", "stroke-opacity": 0.3, "stroke-width": P(0.7 * s)
    }, g);
    /* the disc curves away under the aperture */
    E("rect", {
      x: P(x - w / 2), y: P(y - h / 2), width: P(w), height: P(h * 0.15), rx: P(2.4 * s),
      fill: "url(#gApertureShade)"
    }, g);

    return slide;
  }

  const stripMain = plate(dateG, DATE.x, DATE.y, DATE.w, DATE.h, 17, DATE.h, "Main", 1, true);
  const CELL_MAIN = DATE.h;

  /* The card seen through the crystal is the *unmagnified* one.  Hide it inside
     the bubble: the bubble paints its own magnified card on top. */
  const maskR = E("mask", { id: "mLens", maskUnits: "userSpaceOnUse",
                            x: 0, y: 0, width: 720, height: 1002 }, defs);
  E("rect", { x: 0, y: 0, width: 720, height: 1002, fill: "#ffffff" }, maskR);
  E("circle", { cx: LENS.x, cy: LENS.y, r: LENS.r + 2, fill: "#000000", filter: "url(#fBlur2)" }, maskR);
  dateG.setAttribute("mask", "url(#mLens)");

  /* ================================================================ HANDS */
  const handsG = E("g", { id: "hands" }, svg);
  const HOUR_LEN = 70, MIN_LEN = 110, SEC_LEN = 122;

  /* a Datejust hand: slender polished rod, wider base, pointed tip, lume slot */
  /* A Datejust hand: a white-gold blade with a lume channel down the middle.
     Three hard-edged layers: frame, channel shadow, lume. */
  function handBase(len, wBase, wTip, lumeW, lumeLen) {
    const g = E("g", {}, handsG);
    const inner = E("g", { filter: "url(#fHandShadow)" }, g);
    const tipY = CY - len;

    /* outer frame */
    const outline = `M ${CX - wBase} ${CY - 8}
       L ${CX - wTip} ${tipY + 16}
       Q ${CX - wTip * 0.45} ${tipY + 4} ${CX} ${tipY - 1.5}
       Q ${CX + wTip * 0.45} ${tipY + 4} ${CX + wTip} ${tipY + 16}
       L ${CX + wBase} ${CY - 8} Z`;
    E("path", { d: outline, fill: "url(#gHandFrame)", class: "hand-metal" }, inner);
    E("path", { d: outline, fill: "none", stroke: "#232b30", "stroke-opacity": 0.75, "stroke-width": 0.9, class: "hand-metal" }, inner);

    /* the recessed channel, inset from the frame */
    const cy0 = tipY + 13, cy1 = CY - 19;
    const chan = `M ${CX - lumeW} ${cy1}
       L ${CX - lumeW * 0.55} ${cy0 + 2}
       Q ${CX} ${cy0 - 2} ${CX + lumeW * 0.55} ${cy0 + 2}
       L ${CX + lumeW} ${cy1} Z`;
    E("path", { d: chan, fill: "#101917", opacity: 0.55 }, inner);

    /* lume inside the channel */
    E("path", {
      d: `M ${CX - lumeW + 0.7} ${cy1 - 1}
         L ${CX - lumeW * 0.55 + 0.6} ${cy0 + 3}
         Q ${CX} ${cy0 - 0.6} ${CX + lumeW * 0.55 - 0.6} ${cy0 + 3}
         L ${CX + lumeW - 0.7} ${cy1 - 1} Z`,
      fill: "url(#gLume)", class: "lume"
    }, inner);

    /* specular down the left flank of the frame */
    E("path", {
      d: `M ${CX - wBase + 0.8} ${CY - 10} L ${CX - wTip + 1} ${tipY + 17} L ${CX - wTip + 2.1} ${tipY + 17} L ${CX - wBase + 2} ${CY - 10} Z`,
      fill: "#ffffff", opacity: 0.85, class: "hand-metal"
    }, inner);
    /* shadow down the right flank */
    E("path", {
      d: `M ${CX + wBase - 1.4} ${CY - 10} L ${CX + wTip - 1.5} ${tipY + 17} L ${CX + wTip - 0.5} ${tipY + 17} L ${CX + wBase - 0.6} ${CY - 10} Z`,
      fill: "#3a4249", opacity: 0.5, class: "hand-metal"
    }, inner);

    return g;
  }

  const hourHand = handBase(HOUR_LEN, 7.2, 4.6, 3.1, 30);
  hourHand.setAttribute("id", "hourHand");
  const minHand = handBase(MIN_LEN, 4.6, 3.2, 2.0, 58);
  minHand.setAttribute("id", "minuteHand");

  const secHand = E("g", { id: "secondHand" }, handsG);
  const secInner = E("g", { filter: "url(#fHandShadow)" }, secHand);
  E("rect", { x: CX - 0.95, y: CY - SEC_LEN, width: 1.9, height: SEC_LEN + 30, fill: "url(#gChromeH)" }, secInner);
  E("rect", { x: CX - 0.95, y: CY - SEC_LEN, width: 1.9, height: SEC_LEN + 30, fill: "none", stroke: "#2f383d", "stroke-opacity": 0.45, "stroke-width": 0.35 }, secInner);
  E("rect", { x: CX - 1.9, y: CY + 8, width: 3.8, height: 21, rx: 1.9, fill: "url(#gChromeH)" }, secInner);
  E("circle", { cx: CX, cy: CY - SEC_LEN + 18, r: 4.4, fill: "url(#gChromeH)", stroke: "#eef3f6", "stroke-width": 0.6 }, secInner);
  E("circle", { cx: CX, cy: CY - SEC_LEN + 18, r: 1.9, fill: "#f4fbf7", class: "lume" }, secInner);

  const cap = E("g", { id: "centerCap" }, handsG);
  E("circle", { cx: CX + 1.4, cy: CY + 2, r: 9, fill: "#04140d", opacity: 0.42, filter: "url(#fBlur2)" }, cap);
  E("circle", { cx: CX, cy: CY, r: 7, fill: "url(#gChromeD)" }, cap);
  E("circle", { cx: CX, cy: CY, r: 7, fill: "none", stroke: "#565e63", "stroke-opacity": 0.5, "stroke-width": 0.6 }, cap);
  E("circle", { cx: CX, cy: CY, r: 2.8, fill: "#2b3338" }, cap);
  E("circle", { cx: CX - 2.1, cy: CY - 2.3, r: 2.2, fill: "#ffffff", opacity: 0.62 }, cap);

  /* ============================================================== CRYSTAL */
  const crystal = E("g", { id: "crystal" }, svg);
  E("circle", { cx: CX, cy: CY, r: R.dial + 3.4, fill: "none", stroke: "#ffffff", "stroke-opacity": 0.3, "stroke-width": 2.6 }, crystal);
  E("circle", { cx: CX, cy: CY, r: R.dial + 1.2, fill: "none", stroke: "#04231a", "stroke-opacity": 0.3, "stroke-width": 1.4 }, crystal);

  const glare = E("g", { id: "glare" }, crystal);
  E("ellipse", {
    cx: CX - 66, cy: CY - 34, rx: 74, ry: 44, fill: "url(#gGlare)",
    transform: `rotate(-38 ${CX - 66} ${CY - 34})`, opacity: 0.2
  }, glare);
  E("ellipse", {
    cx: CX + 54, cy: CY + 64, rx: 52, ry: 28, fill: "url(#gGlareSoft)",
    transform: `rotate(-26 ${CX + 54} ${CY + 64})`, opacity: 0.08
  }, glare);
  E("path", {
    d: `M ${CX - 123} ${CY - 14} A ${R.dial} ${R.dial} 0 0 1 ${CX + 15} ${CY - 126}`,
    fill: "none", stroke: "#ffffff", "stroke-opacity": 0.24, "stroke-width": 1.8, "stroke-linecap": "round"
  }, glare);

  /* --------------------------------------------------------------- cyclops */
  const cyclops = E("g", { id: "cyclops" }, crystal);
  E("circle", { cx: LENS.x + 5, cy: LENS.y + 7, r: LENS.r, fill: "#02150d", opacity: 0.34, filter: "url(#fBlur6)" }, cyclops);
  E("circle", { cx: LENS.x + 2, cy: LENS.y + 3, r: LENS.r, fill: "none", stroke: "#04231a", "stroke-opacity": 0.22, "stroke-width": 3, filter: "url(#fBlur2)" }, cyclops);

  clip("clipLens", E("circle", { cx: LENS.x, cy: LENS.y, r: LENS.r - 1.2 }));
  clip("clipBubble", E("circle", { cx: LENS.x, cy: LENS.y, r: LENS.r - 1.2 }));
  const magG = E("g", { "clip-path": "url(#clipLens)" }, cyclops);

  /* the bubble shows a magnified slice of the real dial ... */
  const MAG = 1.75;
  const magT = `translate(${P(LENS.x)} ${P(LENS.y)}) scale(${MAG}) translate(${P(-LENS.x)} ${P(-LENS.y)})`;
  E("g", { transform: magT }, magG).appendChild(
    E("use", { href: "#dial" })
  );
  /* ... and its own magnified date card, drawn at lens scale */
  const LW = DATE.w * MAG, LH = DATE.h * MAG, LF = 33, LCELL = LH;
  const lensCard = E("g", {}, magG);
  /* the white card, slightly bigger than the numbers so a clear margin shows */
  E("rect", {
    x: P(LENS.x - LW / 2 - 4), y: P(LENS.y - LH / 2 - 3), width: P(LW + 8), height: P(LH + 6),
    rx: 1.6, fill: "url(#gDisc)"
  }, lensCard);
  const stripLens = plate(lensCard, LENS.x, LENS.y, LW, LH, LF, LCELL, "Lens", 1, true);
  /* the bubble has its own aperture bevel and a soft reflection */
  E("rect", {
    x: P(LENS.x - LW / 2), y: P(LENS.y - LH / 2), width: P(LW), height: P(LH),
    fill: "none", stroke: "url(#gApertureBevel)", "stroke-width": 1.6, opacity: 0.75
  }, lensCard);
  E("rect", {
    x: P(LENS.x - LW / 2), y: P(LENS.y - LH / 2), width: P(LW), height: P(LH * 0.12),
    fill: "#ffffff", opacity: 0.22
  }, lensCard);

  /* the hands are magnified by the same bubble: a second, clipped copy of
     #hands at lens scale, so a hand passing the aperture swells under the glass */
  const handMagnified = E("g", { transform: magT, "clip-path": "url(#clipBubble)" }, cyclops);
  handMagnified.appendChild(E("use", { href: "#hands" }));

  E("circle", {
    cx: LENS.x, cy: LENS.y, r: LENS.r, fill: "none",
    stroke: "#123d2c", "stroke-width": 9, "stroke-opacity": 0.22
  }, magG);

  E("circle", { cx: LENS.x, cy: LENS.y, r: LENS.r, fill: "url(#gLensTint)" }, cyclops);
  /* the edge of the sapphire bubble: a hard glass line with a dark contact shadow */
  E("circle", { cx: LENS.x, cy: LENS.y, r: LENS.r - 0.2, fill: "none", stroke: "#0c2a20", "stroke-opacity": 0.35, "stroke-width": 2.4 }, cyclops);
  E("circle", { cx: LENS.x, cy: LENS.y, r: LENS.r - 1.4, fill: "none", stroke: "url(#gLensRim)", "stroke-width": 1.5 }, cyclops);
  E("circle", { cx: LENS.x, cy: LENS.y, r: LENS.r - 3.2, fill: "none", stroke: "#ffffff", "stroke-opacity": 0.22, "stroke-width": 0.8 }, cyclops);
  E("ellipse", {
    cx: LENS.x - 12, cy: LENS.y - 20, rx: 18, ry: 10, fill: "#ffffff", opacity: 0.38,
    transform: `rotate(-32 ${LENS.x - 12} ${LENS.y - 20})`, filter: "url(#fBlur1)"
  }, cyclops);
  E("path", {
    d: `M ${P(LENS.x - 30)} ${P(LENS.y + 22)} A ${LENS.r - 8} ${LENS.r - 8} 0 0 0 ${P(LENS.x + 30)} ${P(LENS.y + 12)}`,
    fill: "none", stroke: "#ffffff", "stroke-opacity": 0.16, "stroke-width": 1.6
  }, cyclops);

  /* ================================================================= CROWN */
  const crownG = E("g", { id: "crownGroup" }, svg);
  E("ellipse", { cx: CX + R.case + 4, cy: CY + 6, rx: 12, ry: 25, fill: "#0a1a12", opacity: 0.3, filter: "url(#fBlur3)" }, crownG);

  const crownInner = E("g", { class: "lit" }, crownG);
  const crownX = CX + R.case - 5;
  const crownY = CY + 1;

  /* tube: a short polished neck between case and crown */
  E("rect", { x: crownX - 3, y: crownY - 9.5, width: 14, height: 19, rx: 3, fill: "url(#gChromeV)" }, crownInner);
  E("rect", { x: crownX - 3, y: crownY - 9.5, width: 14, height: 19, rx: 3, fill: "#0d1613", opacity: 0.22 }, crownInner);
  E("rect", { x: crownX - 3, y: crownY - 9.5, width: 14, height: 19, rx: 3, fill: "none", stroke: "#5d646c", "stroke-opacity": 0.45, "stroke-width": 0.8 }, crownInner);

  const crownCapG = E("g", { id: "crownCapG" }, crownInner);
  const capX = crownX + 8, capY = crownY - 20, capW = 20, capH = 40;
  E("rect", { x: capX, y: capY, width: capW, height: capH, rx: 8, fill: "url(#gCrownCap)" }, crownCapG);
  for (let i = 0; i < 17; i++) {
    const y = capY + 3.2 + i * 2.1;
    E("line", { x1: capX + 2.2, y1: y, x2: capX + capW - 2.2, y2: y, stroke: "#525a62", "stroke-opacity": 0.45, "stroke-width": 0.8 }, crownCapG);
    E("line", { x1: capX + 2.2, y1: y + 0.85, x2: capX + capW - 2.2, y2: y + 0.85, stroke: "#ffffff", "stroke-opacity": 0.5, "stroke-width": 0.6 }, crownCapG);
  }
  E("rect", { x: capX, y: capY, width: 3.6, height: capH, rx: 1.8, fill: "url(#gChromeV)", opacity: 0.8 }, crownCapG);
  E("rect", { x: capX + capW - 4, y: capY, width: 4, height: capH, rx: 2, fill: "#2a3037", opacity: 0.35 }, crownCapG);
  E("rect", { x: capX, y: capY, width: capW, height: capH, rx: 8, fill: "none", stroke: "#414850", "stroke-opacity": 0.55, "stroke-width": 0.9 }, crownCapG);
  E("rect", { x: capX + 2.6, y: capY + 2.4, width: 2.6, height: capH - 4.8, rx: 1.3, fill: "#ffffff", opacity: 0.45 }, crownCapG);
  const cc = E("g", { transform: `translate(${capX + capW / 2} ${crownY}) scale(0.44)` }, crownCapG);
  E("path", {
    d: "M -12.6 3.4 L -9.8 -7.4 L -6.2 2.9 L -3.4 -9.1 L 0 2.1 L 3.4 -9.1 L 6.2 2.9 L 9.8 -7.4 L 12.6 3.4 Z",
    fill: "#f2f7fa", opacity: 0.6
  }, cc);

  /* ================================================================ NIGHT */
  const night = E("g", { id: "night", opacity: 0 }, svg);
  const nightDial = E("circle", { cx: CX, cy: CY, r: R.dial, fill: "#020c10", opacity: 0 }, night);
  const glowLayer = E("g", { id: "lumeGlow", opacity: 0, style: "mix-blend-mode:screen" }, svg);
  E("circle", { cx: CX, cy: CY, r: R.dial, fill: "#12e6a3", opacity: 0.05, filter: "url(#fBlur16)" }, glowLayer);

  window.__watch = {
    CX, CY, R, DATE, LENS,
    CELL: CELL_MAIN, CELL_LENS: LCELL,
    hourHand, minHand, secHand,
    stripMain, stripLens,
    crownCapG, crownG, glare, night, nightDial, glowLayer,
    dial: dialG, dialFace, indices: idxG, print, caseG, braceletG, bezel, crystal, hands: handsG
  };
  svg.dispatchEvent(new CustomEvent("watch:ready"));
})();
