import getRelativeDate from 'Utilities/Date/getRelativeDate';

interface RelativeDateOptions {
  timeFormat: string;
  timeForToday?: boolean;
}

// Servarr's getRelativeDate ("Today", "Yesterday", or the short date) with its options typed: the time format is one of
// them, which the JavaScript original's inferred type leaves out.
const relativeDate = getRelativeDate as (
  date: string,
  shortDateFormat: string,
  showRelativeDates: boolean,
  options: RelativeDateOptions
) => string;

export default relativeDate;
