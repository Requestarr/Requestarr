import { useCallback } from 'react';
import { useSelector } from 'react-redux';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import formatDateTime from 'Utilities/Date/formatDateTime';

// Dates outside a table cell, in the date and time format chosen under Settings, then UI.
function useDateText() {
  const { longDateFormat, timeFormat } = useSelector(
    createUISettingsSelector()
  );

  return useCallback(
    (date: string | null) =>
      date ? formatDateTime(date, longDateFormat, timeFormat) : '',
    [longDateFormat, timeFormat]
  );
}

export default useDateText;
