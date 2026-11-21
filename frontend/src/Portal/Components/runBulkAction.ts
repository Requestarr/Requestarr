import { callApi } from 'Settings/Requestarr/requestarrApi';
import translate from 'Utilities/String/translate';
import { ActionOutcome } from '../showOutcome';

interface BulkAnswer {
  done: number;
  total: number;
  failed: { id: number; message: string }[];
}

interface BulkAction<T> {
  // The bulk address: /portal/requests/bulk or /portal/members/bulk.
  path: string;

  // The action's name and its choices (a reason, the seasons, the access changes).
  body: object;

  // The action's words, as its button says them.
  label: string;

  // What the outcome calls each one ("Dune (2021)", "BELLA").
  nameOf: (item: T) => string;
}

// An action on every item picked in one call, as Servarr's bulk edits send the IDs picked; the server acts on each as
// if pressed on its row, and the outcome names each one refused and why.
function runBulkAction<T extends { id: number }>(
  items: T[],
  action: BulkAction<T>
): Promise<ActionOutcome> {
  const nameOf = (id: number) => {
    const item = items.find((candidate) => candidate.id === id);

    return item ? action.nameOf(item) : String(id);
  };

  return callApi<BulkAnswer>(action.path, 'POST', {
    ...action.body,
    ids: items.map((item) => item.id),
  }).then(
    (answer) => ({
      isDone: !answer.failed.length,
      message: [
        translate('PortalBulkResult', {
          action: action.label,
          done: answer.done,
          total: answer.total,
        }),
        ...answer.failed.map(
          (failure) => `${nameOf(failure.id)}: ${failure.message}`
        ),
      ].join(' '),
    }),
    (failure: Error) => ({ isDone: false, message: failure.message })
  );
}

export default runBulkAction;
