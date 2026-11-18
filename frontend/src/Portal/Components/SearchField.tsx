import React from 'react';
import Alert from 'Components/Alert';
import FormInputGroup from 'Components/Form/FormInputGroup';
import Button from 'Components/Link/Button';
import { inputTypes, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from '../Portal.css';

interface SearchFieldProps {
  term: string;
  helpText: string;
  found?: unknown[];
  problem?: string;
  onTermChange: ({ value }: { value: string }) => void;
  onSearch: () => void;
}

// A dialog's search box and button, then why the search failed or that it found nothing; the dialog lists what it
// found.
function SearchField({
  term,
  helpText,
  found,
  problem,
  onTermChange,
  onSearch,
}: SearchFieldProps) {
  return (
    <>
      <div className={styles.search}>
        <FormInputGroup
          type={inputTypes.TEXT}
          name="term"
          value={term}
          helpText={helpText}
          onChange={onTermChange}
        />
        <Button onPress={onSearch}>{translate('Search')}</Button>
      </div>
      {problem && <Alert kind={kinds.DANGER}>{problem}</Alert>}
      {found?.length === 0 && (
        <div className={styles.empty}>{translate('PortalNothingFound')}</div>
      )}
    </>
  );
}

export default SearchField;
