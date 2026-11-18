import React, { useCallback, useEffect, useRef, useState } from 'react';
import { useHistory } from 'react-router-dom';
import Icon from 'Components/Icon';
import { shortcuts } from 'Components/keyboardShortcuts';
import { icons } from 'Helpers/Props';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import translate from 'Utilities/String/translate';
import PortalSearchResults from './PortalSearchResults';
import usePortalSearch, { searchItems } from './usePortalSearch';
import styles from './PortalSearch.css';

// Long enough for a click on a result to land before the results close.
const BLUR_DELAY_MS = 200;

// Where a key press is typing, not a shortcut.
const TYPING = ['INPUT', 'TEXTAREA', 'SELECT'];

// "/" anywhere outside a text field puts the cursor in the search, as in Sonarr, Radarr and Lidarr.
function useFocusShortcut(input: React.RefObject<HTMLInputElement>) {
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement;

      if (
        event.key !== shortcuts.FOCUS_SEARCH_BOX.key ||
        TYPING.includes(target.tagName) ||
        target.isContentEditable
      ) {
        return;
      }

      event.preventDefault();
      input.current?.focus();
    };

    document.addEventListener('keydown', onKeyDown);

    return () => document.removeEventListener('keydown', onKeyDown);
  }, [input]);
}

// The header's search: requests by title and members by name or Discord username, each opening its page. It only
// finds what Requestarr has; it never looks up or requests a new title.
function PortalSearch() {
  const history = useHistory();
  const input = useRef<HTMLInputElement>(null);
  const [term, setTerm] = useState('');
  const [highlight, setHighlight] = useState(0);
  const [isOpen, setIsOpen] = useState(false);
  const results = usePortalSearch(term);
  const items = searchItems(results);

  useFocusShortcut(input);

  const onOpen = useCallback(() => {
    setTerm('');
    input.current?.blur();
  }, []);

  // Closed when the box loses focus, a moment later so a click on a result still opens it.
  const onFocus = useCallback(() => setIsOpen(true), []);
  const onBlur = useCallback(() => {
    setTimeout(() => setIsOpen(false), BLUR_DELAY_MS);
  }, []);

  const onChange = useCallback((event: React.ChangeEvent<HTMLInputElement>) => {
    setTerm(event.target.value);
    setHighlight(0);
  }, []);

  const onKeyDown = useCallback(
    (event: React.KeyboardEvent<HTMLInputElement>) => {
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        const step = event.key === 'ArrowDown' ? 1 : -1;
        setHighlight((current) =>
          Math.min(Math.max(current + step, 0), Math.max(items.length - 1, 0))
        );
      } else if (event.key === 'Enter' && items[highlight]) {
        history.push(getPathWithUrlBase(items[highlight].path));
        onOpen();
      } else if (event.key === 'Escape') {
        onOpen();
      }
    },
    [history, items, highlight, onOpen]
  );

  return (
    <div className={styles.search}>
      <Icon className={styles.icon} name={icons.SEARCH} />
      <input
        ref={input}
        className={styles.input}
        placeholder={translate('PortalSearchPlaceholder')}
        value={term}
        onChange={onChange}
        onKeyDown={onKeyDown}
        onFocus={onFocus}
        onBlur={onBlur}
      />
      {isOpen && results && (
        <PortalSearchResults
          items={items}
          highlight={highlight}
          onOpen={onOpen}
        />
      )}
    </div>
  );
}

export default PortalSearch;
