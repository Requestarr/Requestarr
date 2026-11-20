import translate from 'Utilities/String/translate';
import usePortal from './usePortal';

// Whether a removal deletes in Sonarr or Radarr on this server (Settings, Requests); taken as yes until the answer
// arrives.
function useDeletesFiles() {
  const { data } = usePortal<{ deletesFiles: boolean }>(
    'removals/deletes-files'
  );

  return data?.deletesFiles ?? true;
}

// Force removal's question, as Discord's confirmation asks it: deleting now, or, with deleting turned off, nothing
// deleted until an admin does it by hand.
export function forceRemovalMessage(deletesFiles: boolean) {
  return translate(
    deletesFiles
      ? 'PortalForceRemovalMessage'
      : 'PortalForceRemovalMessageDeletingOff'
  );
}

export default useDeletesFiles;
