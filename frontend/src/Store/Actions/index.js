import * as app from './appActions';
import * as commands from './commandActions';
import * as customFilters from './customFilterActions';
import * as localization from './localizationActions';
import * as members from './memberActions';
import * as memberIndex from './memberIndexActions';
import * as paths from './pathActions';
import * as portalLists from './portalListActions';
import * as providerOptions from './providerOptionActions';
import * as requests from './requestActions';
import * as requestIndex from './requestIndexActions';
import * as settings from './settingsActions';
import * as system from './systemActions';
import * as tags from './tagActions';

export default [
  app,
  commands,
  customFilters,
  paths,
  portalLists,
  providerOptions,
  localization,
  members,
  memberIndex,
  requests,
  requestIndex,
  settings,
  system,
  tags
];
