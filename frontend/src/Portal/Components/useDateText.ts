import moment from 'moment';
import { useCallback } from 'react';
import { useSelector } from 'react-redux';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import formatTime from 'Utilities/Date/formatTime';

// The portal's dates for each long date order under Settings, then UI: "November 17, 2026" or "17 November 2026",
// without the weekday, and with the comma Servarr's own format leaves out.
const DATE_FORMATS: Record<string, string> = {
  'dddd, MMMM D YYYY': 'MMMM D, YYYY',
  'dddd, D MMMM YYYY': 'D MMMM YYYY',
};

// Dates outside a table cell: "November 17, 2026, 1:28am", in the order and time format chosen under Settings, then UI.
function useDateText() {
  const { longDateFormat, timeFormat } = useSelector(
    createUISettingsSelector()
  );

  return useCallback(
    (date: string | null) => {
      if (!date) {
        return '';
      }

      const day = moment(date).format(
        DATE_FORMATS[longDateFormat] ?? longDateFormat
      );
      return `${day}, ${formatTime(date, timeFormat, {
        includeMinuteZero: true,
      })}`;
    },
    [longDateFormat, timeFormat]
  );
}

export default useDateText;
