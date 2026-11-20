import React, { ElementType, ReactNode } from 'react';
import Link from 'Components/Link/Link';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import FollowerStack from '../Components/FollowerStack';
import MemberLink from '../Components/MemberLink';
import { contentTypeText, seasonsText, titleText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import { OpenInLink } from './RequestMedia';
import RequestPoster from './RequestPoster';
import RequestStatusLabel from './RequestStatusLabel';
import styles from '../Portal.css';
import viewStyles from './requestCell.css';

function TitleContent({ request }: { request: PortalRequest }) {
  const seasons = seasonsText(request.seasons);

  return (
    <div>
      <Link to={`/requests/${request.id}`}>{titleText(request)}</Link>
      {seasons && <div className={styles.muted}>{seasons}</div>}
      <OpenInLink media={request.media} />
    </div>
  );
}

// What a column shows for a request, apart from its dates.
function contentOf(name: string, request: PortalRequest): ReactNode {
  switch (name) {
    case 'poster':
      return (
        <RequestPoster
          className={viewStyles.tablePoster}
          url={request.media?.poster}
        />
      );
    case 'title':
      return <TitleContent request={request} />;
    case 'type':
      return contentTypeText(request.contentType);
    case 'status':
      return <RequestStatusLabel request={request} />;
    case 'requester':
      return <MemberLink person={request.requester} size="small" />;
    case 'followers':
      return (
        <FollowerStack requestId={request.id} followers={request.followers} />
      );
    case 'instance':
      return request.instance ?? '';
    default:
      return null;
  }
}

const DATES = ['requested', 'decided', 'fulfilled'] as const;

type DateColumn = (typeof DATES)[number];

// One cell of a request's row, by its column's name, as Sonarr main's SeriesIndexRow draws them: in a table's cell
// (TableRowCell) or, in the Requests page's table that draws only the rows on screen, a VirtualTableRowCell with the
// column's width.
function requestCell(
  name: string,
  request: PortalRequest,
  cell: ElementType,
  className?: string
): ReactNode {
  if ((DATES as readonly string[]).includes(name)) {
    return (
      <RelativeDateCell
        key={name}
        className={className}
        date={request[name as DateColumn] ?? ''}
        component={cell}
      />
    );
  }

  const Cell = cell;

  return (
    <Cell key={name} className={className}>
      {contentOf(name, request)}
    </Cell>
  );
}

export default requestCell;
