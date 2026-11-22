import React, { useCallback, useEffect, useMemo } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import ImportPreviewAppState, {
  ImportPreviewRow,
} from 'App/State/ImportPreviewAppState';
import FieldSet from 'Components/FieldSet';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { visibleCells } from 'Portal/Components/PortalList';
import { contentTypeText, titleText } from 'Portal/portalText';
import { set } from 'Store/Actions/baseActions';
import {
  section,
  setImportPreviewSort,
} from 'Store/Actions/importPreviewActions';
import createClientSideCollectionSelector from 'Store/Selectors/createClientSideCollectionSelector';
import translate from 'Utilities/String/translate';
import { ImportPlan, PlannedFollow, PlannedRequest } from './importTypes';
import styles from './ImportPage.css';

function inLibraryText(instance?: string) {
  return instance
    ? translate('ImportInLibraryYes', { instance })
    : translate('ImportInLibraryNo');
}

function followerNames(followers: PlannedRequest['followers']) {
  return followers.map((f) => f.person.name).join(', ');
}

function requestRow(request: PlannedRequest): ImportPreviewRow {
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

function followRow(follow: PlannedFollow): ImportPreviewRow {
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

// The preview's rows in the store, sorted by the page's choice: the plan's rows are put there each time it changes.
function usePreviewRows(plan: ImportPlan) {
  const dispatch = useDispatch();
  const selector = useMemo(
    () => createClientSideCollectionSelector(section),
    []
  );
  const state = useSelector(selector) as ImportPreviewAppState;

  useEffect(() => {
    dispatch(
      set({
        section,
        items: [
          ...plan.requests.map(requestRow),
          ...plan.follows.map(followRow),
        ],
      })
    );
  }, [plan, dispatch]);

  const onSortPress = useCallback(
    (sortKey: string) => dispatch(setImportPreviewSort({ sortKey })),
    [dispatch]
  );

  return { state, onSortPress };
}

function Rows({ plan }: { plan: ImportPlan }) {
  const { state, onSortPress } = usePreviewRows(plan);

  return (
    <Table
      columns={state.columns}
      sortKey={state.sortKey}
      sortDirection={state.sortDirection}
      onSortPress={onSortPress}
    >
      <TableBody>
        {state.items.map((row) => (
          <TableRow key={row.key}>
            {visibleCells(state.columns, {
              title: <TableRowCell>{row.title}</TableRowCell>,
              type: <TableRowCell>{contentTypeText(row.type)}</TableRowCell>,
              requester: <TableRowCell>{row.requester}</TableRowCell>,
              followers: <TableRowCell>{row.followers}</TableRowCell>,
              when: <RelativeDateCell date={row.when} />,
              ended: <TableRowCell>{row.ended}</TableRowCell>,
              inLibrary: <TableRowCell>{row.inLibrary}</TableRowCell>,
            })}
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
// Nothing is written until the admin presses Import in the toolbar.
function ImportPreview({ plan }: { plan: ImportPlan }) {
  const count = plan.requests.length + plan.follows.length;

  return (
    <>
      <div className={styles.summary}>
        {translate('ImportSummary', {
          read: plan.messagesRead,
          requests: count,
          notInLibrary: plan.requests.filter((r) => !r.inLibrary).length,
          already: plan.alreadyImported,
        })}
      </div>
      <FieldSet legend={`${translate('ImportRequests')} (${count})`}>
        <Notes plan={plan} />
        {count > 0 ? <Rows plan={plan} /> : translate('ImportNothing')}
      </FieldSet>
    </>
  );
}

export default ImportPreview;
