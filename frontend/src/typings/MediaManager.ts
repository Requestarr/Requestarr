import ModelBase from 'App/ModelBase';

// One of a provider's settings as Servarr's schema sends it (Requestarr.Http/ClientSchema/Field.cs).
export interface ProviderField {
  name: string;
  label: string;
  helpText?: string;
  value: unknown;
  type: string;
  advanced: boolean;
  section?: string;
  hidden?: string;
  privacy?: string;
  selectOptionsProviderAction?: string;
}

// A Sonarr or Radarr instance, as the media managers' provider API sends it. Connected and problem: its last
// connection check.
interface MediaManager extends ModelBase {
  name: string;
  enable: boolean;
  implementation: string;
  implementationName: string;
  configContract: string;
  infoLink: string;
  fields: ProviderField[];
  tags: number[];
  connected: boolean;
  problem?: string;
}

// What Test All found for one instance: its validation failures, none when it passed.
export interface TestAllResult {
  id: number;
  isValid: boolean;
  validationFailures: { propertyName: string; errorMessage: string }[];
}

export default MediaManager;
