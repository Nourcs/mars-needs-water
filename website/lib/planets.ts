import * as THREE from "three";
import { marsSeaLevel } from "./ocean";

/** What the scene reads every frame. The React component writes to it. */
export type PlanetState = {
  /** Metres the sea level on Earth has dropped. */
  drain: number;
  /** Share (0..1) of Earth's oceans now on Mars. */
  wet: number;
};

export type PlanetElements = {
  canvas: HTMLCanvasElement;
  /** The full-screen container the canvas fills. */
  hero: HTMLElement;
  /** The free box (between the top bar and the controls) the planets are fitted into. */
  space: HTMLElement;
  earthLabel: HTMLElement;
  marsLabel: HTMLElement;
  stamp: HTMLElement;
};

/** Mars is 0.532 of Earth's diameter (6,779 km against 12,742 km). */
const MARS_SCALE = 0.532;
const GAP = 0.24;

const ELEV = "float elev(vec2 uv){ vec4 t = texture2D(tH, uv); return t.g*t.g*8850. - t.r*t.r*11000.; }";

const EARTH_VERT = `uniform sampler2D tH; uniform float uDrain; varying vec2 vUv; varying vec3 vN, vT, vP; ${ELEV}
void main(){ vUv = uv; float e = max(elev(uv), -uDrain); vec3 p = position * (1. + e * 26. / 6371000.);
  vN = normalize(normalMatrix * normal); vT = normalize(normalMatrix * cross(vec3(0.,1.,0.), normal) + 1e-5);
  vec4 mv = modelViewMatrix * vec4(p, 1.); vP = mv.xyz; gl_Position = projectionMatrix * mv; }`;

const EARTH_FRAG = `uniform sampler2D tH, tC; uniform float uDrain; uniform vec3 uSun; varying vec2 vUv; varying vec3 vN, vT, vP; ${ELEV}
void main(){
  vec2 px = vec2(1./2048., 1./1024.);
  float e = elev(vUv), s = -uDrain;
  float ex = max(elev(vUv + vec2(px.x, 0.)), s) - max(elev(vUv - vec2(px.x, 0.)), s);
  float ey = max(elev(vUv + vec2(0., px.y)), s) - max(elev(vUv - vec2(0., px.y)), s);
  vec3 N = normalize(vN), T = normalize(vT), B = cross(N, T), V = normalize(-vP);
  vec3 n = normalize(N - (ex * T + ey * B) * .00042);
  float d = -e;
  vec3 bed = mix(vec3(.90,.82,.63), vec3(.76,.63,.44), smoothstep(0., 200., d));
  bed = mix(bed, vec3(.55,.44,.31), smoothstep(200., 3000., d));
  bed = mix(bed, vec3(.36,.28,.21), smoothstep(3000., 6000., d));
  bed = mix(bed, vec3(.17,.13,.11), smoothstep(6000., 10000., d));
  bed *= 1. - .16 * smoothstep(.86, 1., fract(d / 1000.)) * smoothstep(150., 600., d);   // bathtub rings
  vec3 land = texture2D(tC, vUv).rgb; land = mix(vec3(dot(land, vec3(.33))), land, 1.15);
  vec3 col = mix(bed, land, smoothstep(-8., 12., e));
  float lit = dot(n, uSun), day = smoothstep(-.35, .55, dot(N, uSun));
  col *= (.32 + .78 * clamp(lit * .5 + .5, 0., 1.)) * mix(.3, 1., day);
  float wd = s - e;
  if (wd > 0.) {
    vec3 w = mix(vec3(.25,.74,.86), vec3(.05,.33,.62), smoothstep(0., 1500., wd));
    w = mix(w, vec3(.02,.13,.34), smoothstep(1500., 6500., wd));
    float wl = clamp(dot(N, uSun) * .5 + .5, 0., 1.);
    w *= (.3 + .8 * wl) * mix(.3, 1., day);
    w += vec3(1., .95, .8) * pow(max(dot(reflect(-uSun, N), V), 0.), 60.) * .55;
    col = mix(col * vec3(.5,.8,.9), w, .72 + .28 * smoothstep(0., 120., wd));
  }
  float rim = pow(1. - max(dot(N, V), 0.), 3.2);
  col += mix(vec3(.25,.55,1.), vec3(.9,.6,.35), clamp(uDrain / 6000., 0., 1.)) * rim * .55 * mix(.25, 1., day);
  gl_FragColor = vec4(col, 1.);
}`;

const GLOW_VERT = `varying vec3 vN, vP;
void main(){ vN = normalize(normalMatrix * normal); vec4 mv = modelViewMatrix * vec4(position, 1.); vP = mv.xyz; gl_Position = projectionMatrix * mv; }`;

const GLOW_FRAG = `uniform float uDry; varying vec3 vN, vP;
void main(){ float d = dot(normalize(vN), normalize(vP)); float i = pow(smoothstep(0., .5, d), 2.2);
  gl_FragColor = vec4(mix(vec3(.3,.62,1.), vec3(1.,.6,.28), uDry), 1.) * i * .75; }`;

// Mars: Viking colour mosaic + MOLA elevation. Water fills the real lowlands first (northern plains, Hellas).
const MARS_VERT = `uniform sampler2D tH; uniform float uSea; varying vec2 vUv; varying vec3 vN, vT, vP;
void main(){ vUv = uv; float e = max(texture2D(tH, uv).r, uSea); vec3 p = position * (1. + (e - .25) * .07);
  vN = normalize(normalMatrix * normal); vT = normalize(normalMatrix * cross(vec3(0.,1.,0.), normal) + 1e-5);
  vec4 mv = modelViewMatrix * vec4(p, 1.); vP = mv.xyz; gl_Position = projectionMatrix * mv; }`;

const MARS_FRAG = `uniform sampler2D tH, tC; uniform float uSea; uniform vec3 uSun; varying vec2 vUv; varying vec3 vN, vT, vP;
float hh(vec2 uv){ return max(texture2D(tH, uv).r, uSea); }
void main(){
  vec2 px = vec2(1./1000., 1./500.);
  float e = texture2D(tH, vUv).r;
  float ex = hh(vUv + vec2(px.x, 0.)) - hh(vUv - vec2(px.x, 0.)), ey = hh(vUv + vec2(0., px.y)) - hh(vUv - vec2(0., px.y));
  vec3 N = normalize(vN), T = normalize(vT), B = cross(N, T), V = normalize(-vP);
  vec3 n = normalize(N - (ex * T + ey * B) * 9.);
  vec3 col = texture2D(tC, vUv).rgb; col = mix(vec3(dot(col, vec3(.33))), col, .92) * vec3(1., .97, .95);
  float lit = dot(n, uSun), day = smoothstep(-.35, .55, dot(N, uSun));
  col *= (.3 + .8 * clamp(lit * .5 + .5, 0., 1.)) * mix(.3, 1., day);
  float wd = (uSea - e) * 29400.;   // water depth in metres
  if (wd > 0.) {
    vec3 w = mix(vec3(.25,.74,.86), vec3(.05,.33,.62), smoothstep(0., 1500., wd));
    w = mix(w, vec3(.02,.13,.34), smoothstep(1500., 7000., wd));
    w *= (.3 + .8 * clamp(dot(N, uSun) * .5 + .5, 0., 1.)) * mix(.3, 1., day);
    w += vec3(1., .95, .8) * pow(max(dot(reflect(-uSun, N), V), 0.), 60.) * .55;
    col = mix(col * vec3(.5,.8,.9), w, .7 + .3 * smoothstep(0., 400., wd));
  }
  float rim = pow(1. - max(dot(N, V), 0.), 3.2);
  col += mix(vec3(.95,.55,.3), vec3(.3,.6,1.), smoothstep(-.5, .45, uSea)) * rim * .4 * mix(.25, 1., day);
  gl_FragColor = vec4(col, 1.);
}`;

/**
 * Builds the Earth + Mars scene on the given canvas and starts drawing.
 * Returns a function that stops it and frees everything.
 */
export function createPlanets(el: PlanetElements, state: PlanetState): () => void {
  const { canvas, hero, space } = el;
  const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const cleanups: (() => void)[] = [];
  const on = <K extends keyof HTMLElementEventMap>(
    target: HTMLElement | Window, type: K | "resize", fn: (e: never) => void,
  ) => {
    target.addEventListener(type, fn as EventListener);
    cleanups.push(() => target.removeEventListener(type, fn as EventListener));
  };

  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
  renderer.setPixelRatio(Math.min(devicePixelRatio || 1, 2));
  const scene = new THREE.Scene();
  const cam = new THREE.PerspectiveCamera(32, 1, 0.1, 200);
  cam.position.z = 5.4;

  const loader = new THREE.TextureLoader();
  const map = (url: string) => {
    const t = loader.load(url);
    t.wrapS = THREE.RepeatWrapping; t.minFilter = THREE.LinearFilter; t.generateMipmaps = false;
    return t;
  };
  const earthHeight = map("/earth/height.png"), earthColor = map("/earth/color.jpg");
  const marsHeight = map("/mars/height.png"), marsColor = map("/mars/color.jpg");
  earthColor.anisotropy = 4;

  const SUN = new THREE.Vector3(-0.55, 0.5, 0.67).normalize();

  // ---- Earth ----
  const earthMat = new THREE.ShaderMaterial({
    uniforms: { tH: { value: earthHeight }, tC: { value: earthColor }, uDrain: { value: 0 }, uSun: { value: SUN } },
    vertexShader: EARTH_VERT, fragmentShader: EARTH_FRAG,
  });
  const tilt = new THREE.Group();
  const earth = new THREE.Mesh(new THREE.SphereGeometry(1, 320, 160), earthMat);
  tilt.rotation.z = 0.22; tilt.rotation.x = 0.28; tilt.add(earth); scene.add(tilt);
  earth.rotation.y = -Math.PI / 2 + 0.45;
  const glowMat = new THREE.ShaderMaterial({
    side: THREE.BackSide, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false,
    uniforms: { uDry: { value: 0 } }, vertexShader: GLOW_VERT, fragmentShader: GLOW_FRAG,
  });
  tilt.add(new THREE.Mesh(new THREE.SphereGeometry(1.15, 64, 32), glowMat));

  // ---- Mars ----
  const marsMat = new THREE.ShaderMaterial({
    uniforms: { tH: { value: marsHeight }, tC: { value: marsColor }, uSea: { value: -1 }, uSun: { value: SUN } },
    vertexShader: MARS_VERT, fragmentShader: MARS_FRAG,
  });
  const mars = new THREE.Mesh(new THREE.SphereGeometry(1, 256, 128), marsMat);
  mars.rotation.x = 0.3; mars.rotation.y = 1.2; scene.add(mars);

  // ---- Stars ----
  const sp = new Float32Array(900 * 3);
  for (let i = 0; i < 900; i++) {
    const v = new THREE.Vector3(Math.random() - 0.5, Math.random() - 0.5, Math.random() - 0.5).normalize().multiplyScalar(60);
    v.z = -Math.abs(v.z) - 8; sp.set([v.x, v.y, v.z], i * 3);
  }
  const starGeo = new THREE.BufferGeometry().setAttribute("position", new THREE.BufferAttribute(sp, 3));
  const starMat = new THREE.PointsMaterial({ color: 0xbfd0ff, size: 1.6, sizeAttenuation: false, transparent: true, opacity: 0.75 });
  scene.add(new THREE.Points(starGeo, starMat));

  // ---- Rockets: sparks that fly from Earth to Mars while the sea level is going down ----
  const RN = 70;
  const rp = new Float32Array(RN * 3).fill(999), rt = new Float32Array(RN).fill(-1), rs = new Float32Array(RN * 3);
  const rocketGeo = new THREE.BufferGeometry().setAttribute("position", new THREE.BufferAttribute(rp, 3));
  const dot = document.createElement("canvas"); dot.width = dot.height = 32;
  const dg = dot.getContext("2d")!, gr = dg.createRadialGradient(16, 16, 0, 16, 16, 16);
  gr.addColorStop(0, "rgba(255,255,255,1)"); gr.addColorStop(0.35, "rgba(255,190,110,.9)"); gr.addColorStop(1, "rgba(255,120,30,0)");
  dg.fillStyle = gr; dg.fillRect(0, 0, 32, 32);
  const sprite = new THREE.CanvasTexture(dot); sprite.colorSpace = THREE.SRGBColorSpace;
  const rocketMat = new THREE.PointsMaterial({ map: sprite, depthWrite: false, depthTest: false, color: 0xffffff, size: 11, sizeAttenuation: false, transparent: true, opacity: 0.95, blending: THREE.AdditiveBlending });
  const rockets = new THREE.Points(rocketGeo, rocketMat);
  rockets.frustumCulled = false; scene.add(rockets);

  // ---- Layout: fit both planets, side by side at true relative size, into the free box ----
  let W = 0, H = 0, R = 1;
  function layout() {
    const r = canvas.getBoundingClientRect(); if (!r.width) return;
    W = r.width; H = r.height; renderer.setSize(W, H, false); cam.aspect = W / H; cam.updateProjectionMatrix();
    const hh = Math.tan(cam.fov * Math.PI / 360) * cam.position.z, hw = hh * cam.aspect;
    const K = MARS_SCALE, hr = hero.getBoundingClientRect(), box = space.getBoundingClientRect();
    const bw = box.width / W * 2 * hw, bh = box.height / H * 2 * hh, lab = 46 / H * 2 * hh;
    const bx = ((box.left + box.width / 2 - r.left) / W * 2 - 1) * hw, by = (1 - (box.top + box.height / 2 - r.top) / H * 2) * hh;
    R = Math.min((bh - lab) / 2 * 0.94, (bw - 0.2) / (2 + GAP + 2 * K + 0.2));
    const x0 = bx - R * (2 + GAP + 2 * K) / 2, cy = by + lab / 2;
    tilt.scale.setScalar(R); tilt.position.set(x0 + R, cy, 0);
    mars.scale.setScalar(R * K); mars.position.set(x0 + R * (2 + GAP + K), cy, 0);
    const place = (label: HTMLElement, obj: THREE.Object3D, rad: number) => {
      const v = obj.position.clone().project(cam), half = label.offsetWidth / 2 + 8;
      label.style.left = Math.max(half, Math.min(hr.width - half, r.left - hr.left + (v.x + 1) / 2 * W)) + "px";
      label.style.top = (r.top - hr.top + (1 - v.y) / 2 * H + rad / hh * H / 2 + 12) + "px";
    };
    place(el.marsLabel, mars, R * K); place(el.earthLabel, tilt, R * 1.02);
    const c = tilt.position.clone().project(cam);
    el.stamp.style.left = (r.left - hr.left + (c.x + 1) / 2 * W) + "px";
    el.stamp.style.top = (r.top - hr.top + (1 - c.y) / 2 * H) + "px";
  }
  on(window, "resize", layout);
  const ro = new ResizeObserver(layout); ro.observe(hero); cleanups.push(() => ro.disconnect());
  document.fonts?.ready.then(layout);
  layout();

  // ---- Drag either planet: whichever one the pointer starts on (the gap between them splits the canvas) ----
  type Body = "e" | "m";
  let dragging: Body | null = null, px = 0, py = 0, idle = 0;
  const REST = reduce ? 0 : 0.05, MREST = reduce ? 0 : 0.08, vel = { e: REST, m: MREST };
  const pick = (e: PointerEvent): Body => {
    const r = canvas.getBoundingClientRect();
    const v = new THREE.Vector3(mars.position.x - mars.scale.x * 1.2, mars.position.y, 0).project(cam);
    return e.clientX > r.left + (v.x + 1) / 2 * r.width ? "m" : "e";
  };
  on(canvas, "pointerdown", (e: PointerEvent) => {
    dragging = pick(e); px = e.clientX; py = e.clientY; canvas.classList.add("drag"); canvas.setPointerCapture(e.pointerId);
  });
  on(canvas, "pointermove", (e: PointerEvent) => {
    if (!dragging) return;
    const dx = e.clientX - px, dy = e.clientY - py; px = e.clientX; py = e.clientY;
    const k = dragging === "m" ? 0.011 : 0.006, body = dragging === "m" ? mars : earth, tip = dragging === "m" ? mars : tilt;
    body.rotation.y += dx * k; vel[dragging] = dx * k * 30;
    tip.rotation.x = Math.max(-0.9, Math.min(0.9, tip.rotation.x + dy * k * 0.66)); idle = 0;
  });
  const release = () => { dragging = null; canvas.classList.remove("drag"); };
  on(canvas, "pointerup", release); on(canvas, "pointercancel", release);

  // ---- Frame loop ----
  const a = new THREE.Vector3(), b = new THREE.Vector3(), c = new THREE.Vector3();
  let last = performance.now(), lastDrain = state.drain, speed = 0, emit = 0, raf = 0;
  function frame(now: number) {
    raf = requestAnimationFrame(frame);
    const dt = Math.min(0.05, Math.max(0.001, (now - last) / 1000)); last = now;
    // How fast the sea is dropping, in metres a second, with a short tail so the sparks do not cut off.
    speed = Math.max(speed * Math.pow(0.02, dt), Math.max(0, state.drain - lastDrain) / dt); if (speed < 1) speed = 0;
    lastDrain = state.drain;

    idle += dragging ? 0 : dt; const ease = Math.min(1, dt * (idle > 1.5 ? 1.2 : 0.6));
    if (dragging !== "e") { earth.rotation.y += vel.e * dt; vel.e += (REST - vel.e) * ease; }
    if (dragging !== "m") { mars.rotation.y += vel.m * dt; vel.m += (MREST - vel.m) * ease; }
    earthMat.uniforms.uDrain.value = state.drain;
    glowMat.uniforms.uDry.value = Math.min(1, state.drain / 6000);
    marsMat.uniforms.uSea.value = marsSeaLevel(state.wet);

    if (!reduce) {
      emit += dt * (speed > 0 ? 10 + Math.min(40, speed / 30) : 0);
      while (emit >= 1) {
        emit -= 1; const i = rt.findIndex(v => v < 0); if (i < 0) break; rt[i] = 0;
        a.set(Math.random() - 0.3, Math.random() - 0.2, 0.7 + Math.random() * 0.5).normalize().multiplyScalar(R * 1.02).add(tilt.position);
        rs.set([a.x, a.y, a.z], i * 3);
      }
      for (let i = 0; i < RN; i++) {
        if (rt[i] < 0) continue; rt[i] += dt * 0.55;
        if (rt[i] >= 1) { rt[i] = -1; rp.set([999, 999, 999], i * 3); continue; }
        const u = rt[i], k = u * u * (3 - 2 * u), q = 1 - k;
        a.fromArray(rs, i * 3); c.copy(mars.position); b.copy(a).lerp(c, 0.5); b.y += R * 0.5; b.z += 0.8;
        rp[i * 3] = q * q * a.x + 2 * q * k * b.x + k * k * c.x;
        rp[i * 3 + 1] = q * q * a.y + 2 * q * k * b.y + k * k * c.y;
        rp[i * 3 + 2] = q * q * a.z + 2 * q * k * b.z + k * k * c.z;
      }
      rocketGeo.attributes.position.needsUpdate = true;
    }
    renderer.render(scene, cam);
  }
  raf = requestAnimationFrame(frame);

  return () => {
    cancelAnimationFrame(raf);
    cleanups.forEach(fn => fn());
    [earth.geometry, mars.geometry, starGeo, rocketGeo, earthMat, glowMat, marsMat, starMat, rocketMat,
      earthHeight, earthColor, marsHeight, marsColor, sprite].forEach(x => x.dispose());
    tilt.children.forEach(ch => { if (ch instanceof THREE.Mesh) ch.geometry.dispose(); });
    renderer.dispose();
  };
}
