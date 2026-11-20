import { AccessState, ContentType, PortalInstance } from './PortalTypes';

// What a new member gets for a type, as the server gives it: the default access of the instance they'll use (the one
// picked, or else the type's default), off when the type has no instance turned on.
function defaultAccess(
  type: ContentType,
  instances: PortalInstance[],
  instanceId: number | null = null
): AccessState {
  const instance = instances.find(
    (one) =>
      one.enabled &&
      one.contentType === type &&
      (instanceId ? one.id === instanceId : one.isDefault)
  );

  return instance?.defaultAccess ?? 'off';
}

export default defaultAccess;
