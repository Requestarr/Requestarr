import React, { ReactNode } from 'react';
import { useParams } from 'react-router-dom';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import FieldSet from 'Components/FieldSet';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import PortalPage from '../Components/PortalPage';
import RemovalsTable from '../Components/RemovalsTable';
import TimelineTable from '../Components/TimelineTable';
import useDateText from '../Components/useDateText';
import {
  contentTypeText,
  requestStatusText,
  seasonsText,
  titleText,
} from '../portalText';
import { PortalPerson, PortalRequestDetail } from '../PortalTypes';
import usePortal from '../usePortal';
import styles from '../Portal.css';

function People({ people }: { people: PortalPerson[] }) {
  if (!people.length) {
    return <span className={styles.muted}>{translate('PortalNobody')}</span>;
  }

  return (
    <>
      {people.map((person, index) => (
        <span key={person.id}>
          {index > 0 && ', '}
          <Link to={`/members/${person.id}`}>
            {person.name ?? translate('PortalUnknownMember')}
          </Link>
        </span>
      ))}
    </>
  );
}

// The summary's rows, title then value; a row with nothing to show is left out.
function summaryRows(
  detail: PortalRequestDetail,
  dateText: (date: string | null) => string
): [string, ReactNode][] {
  const { request } = detail;
  const rows: [string, ReactNode][] = [
    ['PortalType', contentTypeText(request.contentType)],
    ['PortalStatus', requestStatusText(request)],
    ['PortalProgress', detail.progressDetail],
    ['PortalReason', detail.decisionReason],
    ['PortalRequester', <People key="r" people={[request.requester]} />],
    ['PortalFollowers', <People key="f" people={request.followers} />],
    ['PortalInstance', request.instance ?? translate('PortalNotSent')],
    ['PortalSeasonsAsked', seasonsText(request.seasons)],
    ['PortalSeasonsThere', seasonsText(detail.availableSeasons)],
    ['PortalRequested', dateText(request.requested)],
    ['PortalDecided', dateText(request.decided)],
    ['PortalFulfilled', dateText(request.fulfilled)],
  ];

  return rows.filter(([, value]) => value);
}

function Summary({ detail }: { detail: PortalRequestDetail }) {
  const dateText = useDateText();

  return (
    <DescriptionList>
      {summaryRows(detail, dateText).map(([title, value]) => (
        <DescriptionListItem
          key={title}
          title={translate(title)}
          data={value}
        />
      ))}
    </DescriptionList>
  );
}

function renderDetail(detail: PortalRequestDetail) {
  const { request, latestRemoval } = detail;

  return (
    <>
      <FieldSet legend={titleText(request)}>
        <div className={styles.detail}>
          {detail.posterUrl && (
            <img className={styles.poster} src={detail.posterUrl} alt="" />
          )}
          <Summary detail={detail} />
        </div>
      </FieldSet>

      <FieldSet legend={translate('PortalTimeline')}>
        <TimelineTable events={detail.timeline} />
      </FieldSet>

      {latestRemoval && (
        <FieldSet legend={translate('PortalLatestRemoval')}>
          <RemovalsTable removals={[latestRemoval]} />
        </FieldSet>
      )}
    </>
  );
}

// One request: what it is, where it stands, who asked and followed, and everything that happened to it.
function RequestDetailPage() {
  const { id } = useParams<{ id: string }>();
  const result = usePortal<PortalRequestDetail>(`requests/${id}`);

  return (
    <PortalPage title={translate('PortalRequest')} result={result}>
      {renderDetail}
    </PortalPage>
  );
}

export default RequestDetailPage;
