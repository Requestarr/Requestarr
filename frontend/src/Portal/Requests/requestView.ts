// The Requests page's views, as Sonarr's and Radarr's lists have them. The choice is remembered in this browser only.
export const REQUEST_VIEWS = ['overview', 'posters', 'table'] as const;

export type RequestView = (typeof REQUEST_VIEWS)[number];

const STORAGE_KEY = 'requestarr.requests.view';

export function savedView(): RequestView {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);

    return REQUEST_VIEWS.find((view) => view === saved) ?? 'overview';
  } catch {
    // Storage turned off or full: the default view.
    return 'overview';
  }
}

export function saveView(view: RequestView) {
  try {
    localStorage.setItem(STORAGE_KEY, view);
  } catch {
    // Storage turned off or full: the choice lasts until the page is left.
  }
}
