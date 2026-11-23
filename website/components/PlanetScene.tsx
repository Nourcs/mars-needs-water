"use client";

import { useEffect, useRef, useState } from "react";
import { createPlanets, type PlanetState } from "@/lib/planets";
import {
  EARTH_LITRES, EMPTY_DEPTH, MAX_DEPTH, MILESTONES, OCEAN_LITRES, REFILLED, WELCOME,
  groupDigits, marsStatus, oceanLeft,
} from "@/lib/ocean";

/** How long each radio line stays up before the next one may replace it. */
const LINE_MS = 2400;

export default function PlanetScene() {
  // Metres the sea level has dropped. Everything on screen is derived from this one number.
  const [level, setLevel] = useState(0);
  const [line, setLine] = useState(WELCOME);
  const [webgl, setWebgl] = useState(true);

  const hero = useRef<HTMLDivElement>(null);
  const canvas = useRef<HTMLCanvasElement>(null);
  const space = useRef<HTMLDivElement>(null);
  const earthLabel = useRef<HTMLDivElement>(null);
  const marsLabel = useRef<HTMLDivElement>(null);
  const stamp = useRef<HTMLDivElement>(null);

  const scene = useRef<PlanetState>({ drain: 0, wet: 0 });
  const radio = useRef({ said: -1, queue: [] as string[], shownAt: 0 });

  const gone = 1 - oceanLeft(level);          // share of the oceans now on Mars
  const done = level >= EMPTY_DEPTH;
  const shipped = OCEAN_LITRES * BigInt(Math.round(gone * 1e9)) / 1000000000n;

  // Start the 3D scene once.
  useEffect(() => {
    try {
      return createPlanets({
        canvas: canvas.current!, hero: hero.current!, space: space.current!,
        earthLabel: earthLabel.current!, marsLabel: marsLabel.current!, stamp: stamp.current!,
      }, scene.current);
    } catch (err) {
      console.error(err);
      setWebgl(false);
    }
  }, []);

  // Hand the numbers to the scene and queue any radio lines we just passed.
  useEffect(() => {
    scene.current.drain = level;
    scene.current.wet = gone;
    const r = radio.current;
    let reach = -1;
    MILESTONES.forEach(([depth], i) => { if (level >= depth) reach = i; });
    if (reach > r.said) {
      for (let i = r.said + 1; i <= reach; i++) r.queue.push(MILESTONES[i][1]);
      if (reach === MILESTONES.length - 1) { r.queue = [MILESTONES[reach][1]]; r.shownAt = 0; }
    }
    r.said = reach;
  }, [level, gone]);

  // Show queued radio lines one at a time.
  useEffect(() => {
    const id = setInterval(() => {
      const r = radio.current;
      if (!r.queue.length || Date.now() - r.shownAt < LINE_MS) return;
      setLine(r.queue.shift()!);
      r.shownAt = Date.now();
      if (r.queue.length > 2) r.queue = r.queue.slice(-2);
    }, 150);
    return () => clearInterval(id);
  }, []);

  const refill = () => {
    radio.current = { said: -1, queue: [REFILLED], shownAt: 0 };
    setLevel(0);
  };

  return (
    <main className="hero" ref={hero}>
      <canvas id="gl" ref={canvas} hidden={!webgl} aria-label="Earth and Mars, to scale. Drag either planet to turn it." />
      <div className="top">
        <h1 className="logo"><i />MARS NEEDS <em>WATER</em></h1>
        <span className="hint">Drag either planet to turn it</span>
      </div>
      <div className="space" ref={space} />
      <div className="marslab" ref={earthLabel} hidden={!webgl}>
        <b>Earth</b><span>12,742 km wide</span>
      </div>
      <div className="marslab" ref={marsLabel} hidden={!webgl}>
        <b>Mars</b><span>6,779 km wide</span><br /><span>{marsStatus(gone)}</span>
      </div>
      <div className={done ? "stamp on" : "stamp"} ref={stamp}>SOLD OUT</div>

      <div className="dock">
        <p className="radio" aria-live="polite">{line}</p>
        <div className="gauge">
          <label htmlFor="lvl">
            <span>Earth&apos;s sea level</span>
            <b>{level < 1 ? "0 m" : "−" + groupDigits(String(Math.round(level))) + " m"}</b>
          </label>
          {/* The slider is square-root scaled so the shallow, interesting part gets more room. */}
          <input
            type="range" id="lvl" min={0} max={1000} step={1}
            value={Math.round(Math.sqrt(level / MAX_DEPTH) * 1000)}
            onChange={e => { const v = +e.target.value / 1000; setLevel(v * v * MAX_DEPTH); }}
          />
          <div className="ticks" aria-hidden="true">
            <span style={{ left: 0 }}>Today</span>
            <span style={{ left: "13.5%" }}>Shelves dry · 200 m</span>
            <span style={{ left: "58%" }}>Average depth · 3,700 m</span>
            <span style={{ right: 0 }}>Challenger Deep</span>
          </div>
        </div>
        <div className="tally">
          <span><b>{groupDigits((EARTH_LITRES - shipped).toString())}</b> litres left on Earth</span>
          <span><b>{(gone * 100).toFixed(gone < 0.01 ? 4 : 2)}%</b> of the oceans on Mars</span>
          {done && <button className="refill" type="button" onClick={refill}>Put it back</button>}
        </div>
        <p className="credit">
          Earth terrain: Mapzen Terrain Tiles · Earth colours: Natural Earth · Mars: NASA Viking mosaic and MOLA
          elevation · Game map data © OpenStreetMap contributors, Overture Maps Foundation
        </p>
      </div>
    </main>
  );
}
