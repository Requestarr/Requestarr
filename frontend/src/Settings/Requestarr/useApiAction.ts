import { useCallback, useState } from 'react';

// A call the page makes on a button press: busy while it runs, and the API's reason when it refuses.
function useApiAction() {
  const [isBusy, setIsBusy] = useState(false);
  const [problem, setProblem] = useState<string>();

  const run = useCallback(
    <T>(call: () => Promise<T>, onDone: (result: T) => void) => {
      setIsBusy(true);
      setProblem(undefined);

      call().then(
        (result) => {
          setIsBusy(false);
          onDone(result);
        },
        (failure: Error) => {
          setIsBusy(false);
          setProblem(failure.message);
        }
      );
    },
    []
  );

  return { isBusy, problem, setProblem, run };
}

export default useApiAction;
