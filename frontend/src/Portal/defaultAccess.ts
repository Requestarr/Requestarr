import { AccessState, ContentType, PortalInstance } from './PortalTypes';

// What a new member gets for a type, as the server gives it: the default access of the type's instance for everything
// (or its first one turned on), off when the type has none.
function defaultAccess(
  type: ContentType,
  instances: PortalInstance[]
): AccessState {
  const turnedOn = instances.filter(
    (one) => one.enabled && one.contentType === type
  );
  const instance =
    turnedOn.find((one) => one.purpose === 'everything') ?? turnedOn[0];

  return instance?.defaultAccess ?? 'off';
}

export default defaultAccess;
