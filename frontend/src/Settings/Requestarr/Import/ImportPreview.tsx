import React from 'react';
import FieldSet from 'Components/FieldSet';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import columnsOf from 'Portal/Components/columnsOf';
import { contentTypeText, titleText } from 'Portal/portalText';
import translate from 'Utilities/String/translate';
import { ImportPlan, PlannedFollow, PlannedRequest } from './importTypes';
import styles from './ImportPage.css';

const columns = columnsOf([
  'PortalTitle',
  'PortalType',
  'PortalRequester',
  'PortalFollowers',
  'PortalWhen',
  'ImportInRequestrr',
  'ImportInLibrary',
]);

// One line of the list: a request the import makes, or one Requestarr already has that gains followers.
interface Row {
  key: string;
  type: PlannedRequest['type'];
  title: string;
  requester: string;
  followers: string;
  when: string;
  ended: string;
  inLibrary: string;
}

function inLibraryText(instance?: string) {
  return instance
    ? translate('ImportInLibraryYes', { instance })
    : translate('ImportInLibraryNo');
}

function followerNames(followers: PlannedRequest['followers']) {
  return followers.map((f) => f.person.name).join(', ');
}

function requestRow(request: PlannedRequest): Row {
  return {
    key: `new:${request.type}:${request.title}:${request.requested}`,
    type: request.type,
    title: titleText({ title: request.title, year: request.year ?? null }),
    requester: request.requester.name,
    followers: followerNames(request.followers),
    when: request.requested,
    ended: translate(
      request.outcome === 'alreadyAvailable'
        ? 'ImportEndedAvailable'
        : 'ImportEndedSent'
    ),
    inLibrary: inLibraryText(request.instance),
  };
}

function followRow(follow: PlannedFollow): Row {
  return {
    key: `existing:${follow.requestId}`,
    type: follow.type,
    title: titleText({ title: follow.title, year: follow.year ?? null }),
    requester: follow.requester ?? '',
    followers: followerNames(follow.followers),
    when: follow.requested,
    ended: translate('ImportEndedExisting'),
    inLibrary: inLibraryText(follow.instance),
  };
}

function rowsOf(plan: ImportPlan) {
  return [
    ...plan.requests.map(requestRow),
    ...plan.follows.map(followRow),
  ].sort((a, b) => a.when.localeCompare(b.when));
}

function Rows({ rows }: { rows: Row[] }) {
  return (
    <Table columns={columns}>
      <TableBody>
        {rows.map((row) => (
          <TableRow key={row.key}>
            <TableRowCell>{row.title}</TableRowCell>
            <TableRowCell>{contentTypeText(row.type)}</TableRowCell>
            <TableRowCell>{row.requester}</TableRowCell>
            <TableRowCell>{row.followers}</TableRowCell>
            <RelativeDateCell date={row.when} />
            <TableRowCell>{row.ended}</TableRowCell>
            <TableRowCell>{row.inLibrary}</TableRowCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

// What isn't in the list, in a line each: searches that weren't requests, replies with no requester, and earlier
// imports the library has now (linked when the admin confirms).
function Notes({ plan }: { plan: ImportPlan }) {
  return (
    <>
      {plan.notRequests > 0 && (
        <div className={styles.note}>
          {translate('ImportNotRequests', { count: plan.notRequests })}
        </div>
      )}
      {plan.noRequester > 0 && (
        <div className={styles.note}>
          {translate('ImportNoRequester', { count: plan.noRequester })}
        </div>
      )}
      {plan.links.length > 0 && (
        <div className={styles.note}>
          {translate('ImportLinks', {
            titles: plan.links.map((l) => l.title).join(', '),
          })}
        </div>
      )}
    </>
  );
}

// What an import would do: one list of every past request, in the library or not, with who asked and who followed.
// Nothing is written until the admin confirms.
function ImportPreview({ plan }: { plan: ImportPlan }) {
  const rows = rowsOf(plan);

  return (
    <>
      <div className={styles.summary}>
        {translate('ImportSummary', {
          read: plan.messagesRead,
          requests: rows.length,
          notInLibrary: plan.requests.filter((r) => !r.inLibrary).length,
          already: plan.alreadyImported,
        })}
      </div>
      <FieldSet legend={translate('ImportRequests')}>
        {rows.length > 0 ? <Rows rows={rows} /> : translate('ImportNothing')}
        <Notes plan={plan} />
      </FieldSet>
    </>
  );
}

export default ImportPreview;
