import { IconDefinition } from '@fortawesome/fontawesome-svg-core';
import React from 'react';
import { useSelector } from 'react-redux';
import { RequestOverviewOptions } from 'App/State/PortalAppState';
import Icon from 'Components/Icon';
import { icons } from 'Helpers/Props';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import dimensions from 'Styles/Variables/dimensions';
import formatDateTime from 'Utilities/Date/formatDateTime';
import translate from 'Utilities/String/translate';
import relativeDate from '../../Components/relativeDate';
import { contentTypeText, seasonsText } from '../../portalText';
import { PortalRequest } from '../../PortalTypes';
import { releaseText } from '../RequestMedia';
import styles from './RequestIndexOverviewInfo.css';

const infoRowHeight = parseInt(dimensions.requestIndexOverviewInfoRowHeight);

// Each row's margins above and below (RequestIndexOverviewInfo.css .infoRow), added to its height.
const ROW_MARGINS = 4;

interface InfoRow {
  name: string;
  title: string;
  iconName: IconDefinition;
  label: string;
}

type UiSettings = ReturnType<ReturnType<typeof createUISettingsSelector>>;

function requestedRow(request: PortalRequest, ui: UiSettings): InfoRow {
  return {
    name: 'requested',
    title: `${translate('PortalRequested')}: ${formatDateTime(
      request.requested,
      ui.longDateFormat,
      ui.timeFormat
    )}`,
    iconName: icons.ADD,
    label: relativeDate(
      request.requested,
      ui.shortDateFormat,
      ui.showRelativeDates,
      { timeFormat: ui.timeFormat, timeForToday: true }
    ),
  };
}

// The rows the overview options turn on, in Sonarr's order, each only when the request has something to show.
function rowsOf(
  request: PortalRequest,
  options: RequestOverviewOptions,
  ui: UiSettings
): InfoRow[] {
  const releases = releaseText(request.media);
  const followers = request.followers.length;
  const rows: (InfoRow | false)[] = [
    options.showType && {
      name: 'type',
      title: translate('PortalType'),
      iconName: request.contentType === 'tv' ? icons.TV : icons.FILM,
      label: [
        contentTypeText(request.contentType),
        seasonsText(request.seasons),
      ]
        .filter(Boolean)
        .join(' · '),
    },
    options.showRequester && {
      name: 'requester',
      title: translate('PortalRequester'),
      iconName: icons.GROUP,
      label: request.requester.name ?? translate('PortalUnknownMember'),
    },
    options.showFollowers &&
      followers > 0 && {
        name: 'followers',
        title: translate('PortalFollowers'),
        iconName: icons.HEART,
        label: translate('PortalCountFollowing', { count: followers }),
      },
    options.showRequested && requestedRow(request, ui),
    options.showInstance &&
      !!request.instance && {
        name: 'instance',
        title: translate('PortalInstance'),
        iconName: icons.DRIVE,
        label: request.instance,
      },
    options.showReleases &&
      !!releases && {
        name: 'releases',
        title: releases,
        iconName: icons.CALENDAR,
        label: releases,
      },
  ];

  return rows.filter((row): row is InfoRow => !!row);
}

interface RequestIndexOverviewInfoProps {
  request: PortalRequest;
  options: RequestOverviewOptions;
  height: number;
}

// The rows beside a request's overview, as Sonarr main's SeriesIndexOverviewInfo lists the network, the date added and
// so on: an icon and a line each, as many as fit beside the poster.
function RequestIndexOverviewInfo(props: RequestIndexOverviewInfoProps) {
  const { request, options, height } = props;
  const ui = useSelector(createUISettingsSelector());
  const maxRows = Math.floor(height / (infoRowHeight + ROW_MARGINS));

  return (
    <div className={styles.infos}>
      {rowsOf(request, options, ui)
        .slice(0, maxRows)
        .map((row) => (
          <div key={row.name} className={styles.infoRow} title={row.title}>
            <Icon className={styles.icon} name={row.iconName} size={14} />
            {row.label}
          </div>
        ))}
    </div>
  );
}

export default RequestIndexOverviewInfo;
