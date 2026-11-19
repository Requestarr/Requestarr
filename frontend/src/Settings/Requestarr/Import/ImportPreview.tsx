import React from 'react';
import FieldSet from 'Components/FieldSet';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import columnsOf from 'Portal/Components/columnsOf';
import translate from 'Utilities/String/translate';
import { ImportPlan, PlannedRequest } from './importTypes';
import styles from './ImportPage.css';

const requestColumns = columnsOf([
  'PortalTitle',
  'PortalInstance',
  'PortalRequester',
  'PortalWhen',
  'PortalFollowers',
  'PortalStatus',
]);
const followColumns = columnsOf(['PortalTitle', 'PortalWho', 'PortalWhen']);
const skippedColumns = columnsOf([
  'PortalWhat',
  'PortalWho',
  'PortalWhen',
  'ImportWhy',
]);
const unmatchedColumns = columnsOf([
  'PortalTitle',
  'ImportLink',
  'PortalWho',
  'PortalWhen',
]);

function title(request: PlannedRequest) {
  return request.year ? `${request.title} (${request.year})` : request.title;
}

function Requests({ plan }: { plan: ImportPlan }) {
  return (
    <Table columns={requestColumns}>
      <TableBody>
        {plan.requests.map((request) => (
          <TableRow
            key={`${request.instance}:${request.title}:${request.requested}`}
          >
            <TableRowCell>{title(request)}</TableRowCell>
            <TableRowCell>{request.instance}</TableRowCell>
            <TableRowCell>{request.requester.name}</TableRowCell>
            <RelativeDateCell date={request.requested} />
            <TableRowCell>
              {request.followers.map((f) => f.person.name).join(', ')}
            </TableRowCell>
            <TableRowCell>
              {translate(
                request.available || request.hasFiles
                  ? 'ImportInLibrary'
                  : 'ImportNotDownloaded'
              )}
            </TableRowCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

function Skipped({ plan }: { plan: ImportPlan }) {
  return (
    <Table columns={skippedColumns}>
      <TableBody>
        {plan.skipped.map((skipped) => (
          <TableRow key={skipped.messageId}>
            <TableRowCell>{skipped.what ?? ''}</TableRowCell>
            <TableRowCell>{skipped.who ?? ''}</TableRowCell>
            <RelativeDateCell date={skipped.at} />
            <TableRowCell>{skipped.reason}</TableRowCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

function Unmatched({ plan }: { plan: ImportPlan }) {
  return (
    <Table columns={unmatchedColumns}>
      <TableBody>
        {plan.unmatched.map((title) => (
          <TableRow key={`${title.link}:${title.title}`}>
            <TableRowCell>{title.title ?? ''}</TableRowCell>
            <TableRowCell>{title.link}</TableRowCell>
            <TableRowCell>{title.askedBy.join(', ')}</TableRowCell>
            <RelativeDateCell date={title.firstAsked} />
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

function Follows({ plan }: { plan: ImportPlan }) {
  return (
    <Table columns={followColumns}>
      <TableBody>
        {plan.follows.map((follow) => (
          <TableRow
            key={`${follow.requestId}:${follow.follower.person.discordId}`}
          >
            <TableRowCell>{follow.title}</TableRowCell>
            <TableRowCell>{follow.follower.person.name}</TableRowCell>
            <RelativeDateCell date={follow.follower.asked} />
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

// What an import would do: every request and its followers, followers for requests Requestarr already has, what's
// skipped and why, and the titles no Sonarr or Radarr has. Nothing is written until the admin confirms.
function ImportPreview({ plan }: { plan: ImportPlan }) {
  return (
    <>
      <div className={styles.summary}>
        {translate('ImportSummary', {
          read: plan.messagesRead,
          requests: plan.requests.length,
          follows: plan.follows.length,
          skipped: plan.skipped.length,
          unmatched: plan.unmatched.length,
          already: plan.alreadyImported,
        })}
      </div>
      <FieldSet legend={translate('ImportRequests')}>
        {plan.requests.length > 0 ? (
          <Requests plan={plan} />
        ) : (
          translate('ImportNothing')
        )}
      </FieldSet>
      {plan.follows.length > 0 && (
        <FieldSet legend={translate('ImportFollows')}>
          <Follows plan={plan} />
        </FieldSet>
      )}
      {plan.skipped.length > 0 && (
        <FieldSet legend={translate('ImportSkipped')}>
          <Skipped plan={plan} />
        </FieldSet>
      )}
      {plan.unmatched.length > 0 && (
        <FieldSet legend={translate('ImportUnmatched')}>
          <Unmatched plan={plan} />
        </FieldSet>
      )}
    </>
  );
}

export default ImportPreview;
