// A ready fix for a show Sonarr grabs wrong, as the server keeps it (SeriesFix).
export interface SeriesFix {
  id: number;
  tvdbId: number;
  series: string;
  shortName: string;
  mustContain: string[];
  mustNotContain: string[];
  reason: string | null;
  enabled: boolean;
  shipped: boolean;
}

// What the edit modal sends (SeriesFixInput).
export type SeriesFixInput = Omit<SeriesFix, 'id' | 'shipped'>;

export const NEW_FIX: SeriesFixInput = {
  tvdbId: 0,
  series: '',
  shortName: '',
  mustContain: [],
  mustNotContain: [],
  reason: '',
  enabled: true,
};
