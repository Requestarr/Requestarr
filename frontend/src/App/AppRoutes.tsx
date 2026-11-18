import React from 'react';
import { Route } from 'react-router-dom';
import NotFound from 'Components/NotFound';
import Switch from 'Components/Router/Switch';
import AccessRequestsPage from 'Portal/AccessRequests/AccessRequestsPage';
import AuditLogPage from 'Portal/Audit/AuditLogPage';
import MemberDetailPage from 'Portal/Members/MemberDetailPage';
import MembersPage from 'Portal/Members/MembersPage';
import OutagesPage from 'Portal/Outages/OutagesPage';
import QueuePage from 'Portal/Queue/QueuePage';
import RequestDetailPage from 'Portal/Requests/RequestDetailPage';
import RequestsPage from 'Portal/Requests/RequestsPage';
import DevelopmentSettingsConnector from 'Settings/Development/DevelopmentSettingsConnector';
import GeneralSettingsConnector from 'Settings/General/GeneralSettingsConnector';
import ConnectionSettings from 'Settings/Requestarr/ConnectionSettings';
import DiscordSettings from 'Settings/Requestarr/DiscordSettings';
import InstancesSettings from 'Settings/Requestarr/InstancesSettings';
import RequestSettings from 'Settings/Requestarr/RequestSettings';
import SettingsChanges from 'Settings/Requestarr/SettingsChanges';
import Settings from 'Settings/Settings';
import TagSettings from 'Settings/Tags/TagSettings';
import UISettingsConnector from 'Settings/UI/UISettingsConnector';
import HomeRedirect from 'Setup/HomeRedirect';
import SetupPage from 'Setup/SetupPage';
import BackupsConnector from 'System/Backup/BackupsConnector';
import LogsTableConnector from 'System/Events/LogsTableConnector';
import Logs from 'System/Logs/Logs';
import Status from 'System/Status/Status';
import Tasks from 'System/Tasks/Tasks';
import Updates from 'System/Updates/Updates';

function AppRoutes() {
  return (
    <Switch>
      {/*
        Home
      */}

      <Route exact={true} path="/" component={HomeRedirect} />

      {window.Requestarr.urlBase && (
        <Route
          exact={true}
          path="/"
          // eslint-disable-next-line @typescript-eslint/ban-ts-comment
          // @ts-ignore
          addUrlBase={false}
          component={HomeRedirect}
        />
      )}

      {/*
        Admin portal
      */}

      <Route exact={true} path="/requests" component={RequestsPage} />

      <Route path="/requests/:id" component={RequestDetailPage} />

      <Route exact={true} path="/members" component={MembersPage} />

      <Route path="/members/:id" component={MemberDetailPage} />

      <Route path="/queue" component={QueuePage} />

      <Route path="/access-requests" component={AccessRequestsPage} />

      <Route path="/outages" component={OutagesPage} />

      <Route path="/audit" component={AuditLogPage} />

      {/*
        Settings
      */}

      <Route exact={true} path="/settings" component={Settings} />

      <Route path="/settings/tags" component={TagSettings} />

      <Route path="/settings/general" component={GeneralSettingsConnector} />

      <Route path="/settings/ui" component={UISettingsConnector} />

      <Route path="/settings/discord" component={DiscordSettings} />

      <Route path="/settings/instances" component={InstancesSettings} />

      <Route path="/settings/requests" component={RequestSettings} />

      <Route path="/settings/connections" component={ConnectionSettings} />

      <Route path="/settings/changes" component={SettingsChanges} />

      <Route path="/setup" component={SetupPage} />

      <Route
        path="/settings/development"
        component={DevelopmentSettingsConnector}
      />

      {/*
        System
      */}

      <Route path="/system/status" component={Status} />

      <Route path="/system/tasks" component={Tasks} />

      <Route path="/system/backup" component={BackupsConnector} />

      <Route path="/system/updates" component={Updates} />

      <Route path="/system/events" component={LogsTableConnector} />

      <Route path="/system/logs/files" component={Logs} />

      {/*
        Not Found
      */}

      <Route path="*" component={NotFound} />
    </Switch>
  );
}

export default AppRoutes;
