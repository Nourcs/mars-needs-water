// The real numbers behind the page.

/** Deepest point the slider reaches (Challenger Deep), in metres below today's sea level. */
export const MAX_DEPTH = 10935;
/** Below this level the oceans count as empty. */
export const EMPTY_DEPTH = 10020;

/** All water on Earth and the water in the oceans, in litres. */
export const EARTH_LITRES = 1386000000000000000000n;
export const OCEAN_LITRES = 1335000000000000000000n;

// Share of ocean volume left at each sea level (64 steps, level = (i/64)^2 x 11,000 m), from real bathymetry.
const HYPS = [1,.99928,.99712,.99354,.98858,.98228,.97466,.96574,.95553,.94403,.93125,.91719,.90188,.88532,.86752,.84849,.82823,.80675,.78405,.76015,.73506,.7088,.6814,.65288,.62327,.59259,.56089,.52821,.4946,.46013,.42488,.38894,.35247,.31572,.27899,.24265,.20715,.17301,.14077,.11093,.084,.06059,.04101,.02528,.01381,.00656,.00276,.00117,.00069,.00049,.00035,.00025,.00017,.00011,.00007,.00004,.00002,.00001,0,0,0,0,0,0,0];

/** Share of the oceans still on Earth (1..0) when the sea level has dropped by `level` metres. */
export function oceanLeft(level: number): number {
  if (level >= EMPTY_DEPTH) return 0;
  const x = Math.sqrt(level / 11000) * 64, i = Math.floor(x);
  return HYPS[i] + (HYPS[i + 1] - HYPS[i]) * (x - i);
}

// Mars sea level (in height-map units, 0..1 = -8.2..21.2 km) for each share of Earth's oceans delivered (32 steps).
const MARS_SEA = [.027,.1721,.203,.2279,.2496,.2693,.2873,.3035,.3177,.3303,.342,.3531,.3639,.3744,.3848,.3952,.4054,.4156,.4257,.4358,.4458,.4558,.4658,.4758,.4857,.4956,.5055,.5153,.5252,.535,.5449,.5547,.5645];

/** Mars sea level for a share (0..1) of Earth's oceans delivered. -1 means no water yet. */
export function marsSeaLevel(share: number): number {
  if (share < .0005) return -1;
  const x = Math.min(1, share) * 32, i = Math.min(31, Math.floor(x));
  return MARS_SEA[i] + (MARS_SEA[i + 1] - MARS_SEA[i]) * (x - i);
}

export function marsStatus(share: number): string {
  if (share >= 1) return "99.7% ocean. Oops.";
  if (share > .5) return "Mostly ocean now";
  if (share > .12) return "Has a northern ocean";
  if (share > .005) return "Slightly damp";
  return "Thirsty";
}

/** Radio lines, each said once when the sea level first drops past its depth (metres). */
export const MILESTONES: [number, string][] = [
  [10, "Ten metres! Mars thanks you. Venice has also been in touch."],
  [55, "The Bering Strait is now a footpath. Russia and Alaska are neighbours again."],
  [120, "Doggerland is back. Britain is no longer an island. We have not told Britain."],
  [200, "The continental shelves are dry. That was the shallow end."],
  [300, "The Strait of Gibraltar has closed. The Mediterranean is now a company lake."],
  [1000, "One kilometre down. Accounting has asked me to say: wow."],
  [2500, "The Mid-Atlantic Ridge is showing. Longest mountain range on Earth. Nobody had seen it. You are welcome."],
  [3700, "Average ocean depth reached. Above-average effort."],
  [6000, "Only the trenches are left. Deep, narrow and full of opportunity."],
  [EMPTY_DEPTH, "The Challenger Deep is dry. The quarter is closed. Please continue."],
];

export const WELCOME = "Earth has the water. Mars wants it. Drag the slider.";
export const REFILLED = "The oceans have been refilled. This will come out of your pay.";

export const groupDigits = (s: string) => s.replace(/\B(?=(\d{3})+(?!\d))/g, ",");
