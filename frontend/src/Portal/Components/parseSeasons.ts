// One season (3) or a run of them (1-3, as Discord writes them, with a hyphen or a dash).
const PART = /^(\d+)(?:\s*[-–]\s*(\d+))?$/;

// Typed seasons as numbers: "1, 3, 5-7" (commas or spaces between them). Empty is none, which a removal reads as the
// whole title. Null when any part isn't a season number or a run, so nothing is sent on a typo ("S2" would otherwise
// read as no seasons, the whole title).
function parseSeasons(text: string): number[] | null {
  const parts = text.split(/[\s,]+/).filter(Boolean);
  const seasons: number[] = [];

  for (const part of parts) {
    const match = PART.exec(part);

    if (!match) {
      return null;
    }

    const first = Number(match[1]);
    const last = match[2] === undefined ? first : Number(match[2]);

    if (last < first) {
      return null;
    }

    for (let season = first; season <= last; season++) {
      seasons.push(season);
    }
  }

  return [...new Set(seasons)];
}

export default parseSeasons;
