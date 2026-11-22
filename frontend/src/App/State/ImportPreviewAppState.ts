import SortDirection from 'Helpers/Props/SortDirection';
import { TableAppSectionState } from './AppSectionState';

// One line of Import → From Requestrr's preview: a request the import makes, or one Requestarr already has that
// gains followers.
export interface ImportPreviewRow {
  key: string;
  type: 'movie' | 'tv';
  title: string;
  requester: string;
  followers: string;
  when: string;
  ended: string;
  inLibrary: string;
}

// The preview's store section (Store/Actions/importPreviewActions.js).
interface ImportPreviewAppState extends TableAppSectionState {
  items: ImportPreviewRow[];
  sortKey: string;
  sortDirection: SortDirection;
}

export default ImportPreviewAppState;
