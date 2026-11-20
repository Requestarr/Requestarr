import React from 'react';
import CheckInput from 'Components/Form/CheckInput';
import translate from 'Utilities/String/translate';
import { Selection } from './useSelection';
import styles from './PageCheckbox.css';

// What the page's box shows: ticked when all of the page is picked, empty when none is, a dash in between.
function pageValue(selection: Selection) {
  if (selection.allOnPage) {
    return true;
  }

  return selection.noneOnPage ? false : null;
}

// The box that picks or clears every row on the page shown, in the selection row of a list that isn't a table (a table
// has it in its header).
function PageCheckbox({ selection }: { selection: Selection }) {
  return (
    <span title={translate('PortalSelectPage')}>
      <CheckInput
        containerClassName={styles.box}
        name="selectPage"
        value={pageValue(selection)}
        onChange={selection.onPageChange}
      />
    </span>
  );
}

export default PageCheckbox;
