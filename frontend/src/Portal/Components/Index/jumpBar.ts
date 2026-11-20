import { sortDirections } from 'Helpers/Props';
import getIndexOfFirstCharacter from 'Utilities/Array/getIndexOfFirstCharacter';

// What Servarr's PageJumpBar takes: how many items start with each character, in the order shown.
export interface JumpBarItems {
  characters?: Record<string, number>;
  order: string[];
}

// The character a title or name is filed under in the jump bar: its first letter, or # for a digit.
function characterOf(text: string) {
  const character = text.charAt(0).toUpperCase();

  return isNaN(Number(character)) ? character : '#';
}

// Sonarr main's SeriesIndex jump bar, by title (or name): only when the list is sorted by it.
export function jumpBarItems(
  texts: string[],
  isSortedByText: boolean,
  sortDirection: string
): JumpBarItems {
  if (!isSortedByText) {
    return { order: [] };
  }

  const characters = texts.reduce((counts: Record<string, number>, text) => {
    const character = characterOf(text);

    counts[character] = (counts[character] ?? 0) + 1;

    return counts;
  }, {});

  const order = Object.keys(characters).sort();

  if (sortDirection === sortDirections.DESCENDING) {
    order.reverse();
  }

  return { characters, order };
}

// Where the first title (or name) filed under character is, as Servarr's getIndexOfFirstCharacter finds it.
export function indexOfCharacter(texts: string[], character: string) {
  return getIndexOfFirstCharacter(
    texts.map((text) => ({ sortTitle: text.toUpperCase() })),
    character
  );
}
