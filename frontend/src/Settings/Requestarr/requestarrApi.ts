import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';

interface FailedRequest {
  responseJSON?: { message?: string };
}

// Requestarr's settings API (/api/v1/requestarr/{path}): its answer, or why it refused, in the API's own words.
export function callRequestarrApi<T>(
  path: string,
  method = 'GET',
  body?: unknown
): Promise<T> {
  return new Promise((resolve, reject) => {
    const { request } = createAjaxRequest({
      url: `/requestarr/${path}`,
      method,
      data: body === undefined ? undefined : JSON.stringify(body),
    });

    request.done((data: T) => resolve(data));
    request.fail((xhr: FailedRequest) =>
      reject(
        new Error(xhr.responseJSON?.message ?? translate('RequestarrApiError'))
      )
    );
  });
}
