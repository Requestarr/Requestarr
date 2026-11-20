import { RefObject, useEffect } from 'react';
import { indexOfCharacter } from './jumpBar';

interface JumpToCharacter {
  // Each item's title (or name), in the order shown.
  texts: string[];
  jumpToCharacter?: string;
  scrollerRef: RefObject<HTMLElement>;

  // How far down the item at an index is.
  scrollTopOf: (index: number) => number;

  // Moves the view's list there.
  onScrollTop: (scrollTop: number) => void;
}

// When the jump bar is pressed, scrolls the page and its list to the first title (or name) filed under that
// character, as each of Sonarr main's index views does.
function useJumpToCharacter(jump: JumpToCharacter) {
  const { texts, jumpToCharacter, scrollerRef, scrollTopOf, onScrollTop } =
    jump;

  useEffect(() => {
    if (!jumpToCharacter) {
      return;
    }

    const index = indexOfCharacter(texts, jumpToCharacter);

    if (index >= 0) {
      const scrollTop = scrollTopOf(index);

      onScrollTop(scrollTop);
      scrollerRef.current?.scrollTo(0, scrollTop);
    }
  }, [jumpToCharacter, texts, scrollerRef, scrollTopOf, onScrollTop]);
}

export default useJumpToCharacter;
