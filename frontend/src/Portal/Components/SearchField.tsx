import React from 'react';
import FormInputGroup from 'Components/Form/FormInputGroup';
import Button from 'Components/Link/Button';
import { inputTypes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from '../Portal.css';

interface SearchFieldProps {
  term: string;
  helpText: string;
  onTermChange: ({ value }: { value: string }) => void;
  onSearch: () => void;
}

function SearchField({
  term,
  helpText,
  onTermChange,
  onSearch,
}: SearchFieldProps) {
  return (
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
  );
}

export default SearchField;
