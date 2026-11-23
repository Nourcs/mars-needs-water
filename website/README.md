# Mars Needs Water: website

One full-screen scene: Earth and Mars at their true relative size. Drag either planet to turn it, and drag
the sea-level slider to move Earth's oceans to Mars.

## Run it

You need Node.js 20 or newer (https://nodejs.org).

```bash
cd website
npm install      # first time only
npm run dev
```

Then open http://localhost:3000.

## Where things are

- `app/page.tsx`, `app/layout.tsx`, `app/globals.css`: the page, fonts and styles.
- `components/PlanetScene.tsx`: the screen (logo, labels, radio line, slider, tally).
- `lib/planets.ts`: the three.js scene and the shaders for both planets.
- `lib/ocean.ts`: the real numbers (ocean volume by depth, Mars sea level, radio lines).
- `public/earth`, `public/mars`: the height and colour maps.

## Credits

Earth terrain: Mapzen Terrain Tiles. Earth colours: Natural Earth. Mars: NASA Viking mosaic and MOLA
elevation. Game map data © OpenStreetMap contributors, Overture Maps Foundation.
