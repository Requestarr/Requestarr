import translate from 'Utilities/String/translate';
import { ActionOutcome } from '../usePortalAction';

interface EachAction<T> {
  // The action's words, as its button says them.
  label: string;
  // What the outcome calls each one ("Dune (2021)", "BELLA").
  nameOf: (item: T) => string;
  call: (item: T) => Promise<unknown>;
}

// One item's part: null when it worked, or what went wrong, by its name.
function runOne<T>(item: T, action: EachAction<T>) {
  return action.call(item).then(
    () => null,
    (failure: Error) => `${action.nameOf(item)}: ${failure.message}`
  );
}

// An action on each item picked, one after another as if pressed on each row; a refusal doesn't stop the rest, and the
// outcome names each one refused and why.
function runEach<T>(items: T[], action: EachAction<T>): Promise<ActionOutcome> {
  const failures = items.reduce<Promise<string[]>>(
    (soFar, item) =>
      soFar.then((failed) =>
        runOne(item, action).then((failure) =>
          failure ? [...failed, failure] : failed
        )
      ),
    Promise.resolve([])
  );

  return failures.then((failed) => ({
    isDone: !failed.length,
    message: [
      translate('PortalBulkResult', {
        action: action.label,
        done: items.length - failed.length,
        total: items.length,
      }),
      ...failed,
    ].join(' '),
  }));
}

export default runEach;
