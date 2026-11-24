import * as app from './appActions';
import * as commands from './commandActions';
import * as customFilters from './customFilterActions';
import * as importPreview from './importPreviewActions';
import * as localization from './localizationActions';
import * as members from './memberActions';
import * as memberIndex from './memberIndexActions';
import * as paths from './pathActions';
import * as portalLists from './portalListActions';
import * as providerOptions from './providerOptionActions';
import * as requests from './requestActions';
import * as requestIndex from './requestIndexActions';
import * as serverMembers from './serverMemberActions';
import * as settings from './settingsActions';
import * as system from './systemActions';
import * as tags from './tagActions';

export default [
  app,
  commands,
  customFilters,
  importPreview,
  paths,
  portalLists,
  providerOptions,
  localization,
  members,
  memberIndex,
  requests,
  requestIndex,
  serverMembers,
  settings,
  system,
  tags
];
