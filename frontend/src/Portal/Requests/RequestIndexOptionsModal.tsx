import React, { useCallback } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import {
  setRequestOverviewOption,
  setRequestPosterOption,
} from 'Store/Actions/requestIndexActions';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import IndexOptionsModal, { IndexOption } from './IndexOptionsModal';

function posterOptions(): IndexOption[] {
  return [
    {
      name: 'detailedProgressBar',
      label: translate('DetailedProgressBar'),
      helpText: translate('PortalDetailedProgressBarHelpText'),
    },
    {
      name: 'showTitle',
      label: translate('ShowTitle'),
      helpText: translate('PortalShowTitleHelpText'),
    },
    {
      name: 'showRequester',
      label: translate('PortalShowRequester'),
      helpText: translate('PortalShowRequesterHelpText'),
    },
    {
      name: 'showFollowers',
      label: translate('PortalShowFollowers'),
      helpText: translate('PortalShowFollowersHelpText'),
    },
  ];
}

function overviewOptions(): IndexOption[] {
  return [
    {
      name: 'detailedProgressBar',
      label: translate('DetailedProgressBar'),
      helpText: translate('PortalDetailedProgressBarHelpText'),
    },
    { name: 'showType', label: translate('PortalShowType') },
    { name: 'showRequester', label: translate('PortalShowRequester') },
    { name: 'showFollowers', label: translate('PortalShowFollowers') },
    { name: 'showRequested', label: translate('PortalShowRequested') },
    { name: 'showInstance', label: translate('PortalShowInstance') },
    { name: 'showReleases', label: translate('PortalShowReleases') },
  ];
}

interface RequestIndexOptionsModalProps {
  view: string;
  isOpen: boolean;
  onModalClose: () => void;
}

// The Options of the Posters or Overview view, as Sonarr main's SeriesIndex opens SeriesIndexPosterOptionsModal or
// SeriesIndexOverviewOptionsModal; the Table view's are Table Options.
function RequestIndexOptionsModal(props: RequestIndexOptionsModalProps) {
  const { view, isOpen, onModalClose } = props;
  const isPosters = view === 'posters';
  const values = useSelector((state: AppState) =>
    isPosters
      ? state.requestIndex.posterOptions
      : state.requestIndex.overviewOptions
  );
  const dispatch = useDispatch();
  const onOptionChange = useCallback(
    ({ name, value }: InputChanged) => {
      const setOption = isPosters
        ? setRequestPosterOption
        : setRequestOverviewOption;

      dispatch(setOption({ [name]: value }));
    },
    [isPosters, dispatch]
  );

  return (
    <IndexOptionsModal
      isOpen={isOpen}
      title={translate(isPosters ? 'PosterOptions' : 'OverviewOptions')}
      sizeLabel={translate('PosterSize')}
      values={{ ...values }}
      options={isPosters ? posterOptions() : overviewOptions()}
      onOptionChange={onOptionChange}
      onModalClose={onModalClose}
    />
  );
}

export default RequestIndexOptionsModal;
