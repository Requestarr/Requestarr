import Column from 'Components/Table/Column';
import translate from 'Utilities/String/translate';

// A table's columns, each named by its heading's translation key and always shown.
function columnsOf(labels: string[]): Column[] {
  return labels.map((label) => ({
    name: label,
    label: () => translate(label),
    isVisible: true,
  }));
}

export default columnsOf;
