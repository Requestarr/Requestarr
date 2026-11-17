import { useEffect, useState } from 'react';
import createAjaxRequest from 'Utilities/createAjaxRequest';

export interface PortalResult<T> {
  data?: T;
  isLoading: boolean;
  isNotFound: boolean;
  hasError: boolean;
}

interface Answer<T> extends PortalResult<T> {
  path: string;
}

interface FailedRequest {
  aborted: boolean;
  status: number;
}

const LOADING = { isLoading: true, isNotFound: false, hasError: false };
const HTTP_NOT_FOUND = 404;

// One of the portal's read views (/api/v1/portal/{path}), fetched when the page opens and again when the path changes.
// The answer remembers its path, so a page that moves on shows loading until its own answer arrives.
function usePortal<T>(path: string): PortalResult<T> {
  const [answer, setAnswer] = useState<Answer<T>>();

  useEffect(() => {
    const { request, abortRequest } = createAjaxRequest({
      url: `/portal/${path}`,
    });

    request.done((data: T) => {
      setAnswer({
        path,
        data,
        isLoading: false,
        isNotFound: false,
        hasError: false,
      });
    });

    request.fail((xhr: FailedRequest) => {
      if (!xhr.aborted) {
        const isNotFound = xhr.status === HTTP_NOT_FOUND;
        setAnswer({
          path,
          isLoading: false,
          isNotFound,
          hasError: !isNotFound,
        });
      }
    });

    return abortRequest;
  }, [path]);

  return answer?.path === path ? answer : LOADING;
}

export default usePortal;
