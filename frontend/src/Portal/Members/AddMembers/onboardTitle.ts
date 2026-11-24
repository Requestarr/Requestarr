import translate from 'Utilities/String/translate';
import { Who } from './useStartRun';

// The Onboard window's heading: who it's for, or how many.
function onboardTitle(people: Who[]) {
  return translate('PortalOnboardWho', {
    who:
      people.length === 1
        ? people[0].name
        : translate('PortalOnboardPeople', { count: people.length }),
  });
}

export default onboardTitle;
