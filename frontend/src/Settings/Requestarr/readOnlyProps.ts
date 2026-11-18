// Locks an input that can't be changed here. FormInputGroup hands these to its input, but its prop types don't list
// them, so they go in as a spread, the way provisionedSetting.js passes them.
function readOnlyProps(isReadOnly: boolean) {
  return { readOnly: isReadOnly, isDisabled: isReadOnly };
}

export default readOnlyProps;
