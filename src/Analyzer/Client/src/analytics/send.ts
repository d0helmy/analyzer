// Analyzer slice-004 client API entrypoint.
//
// Exposes `window.analyzer.send("event", category, action, label?, value?)`
// for in-page custom-event capture. POSTs the payload to the management
// endpoint at `/umbraco/management/api/v1/analyzer/custom-event`.
// Anti-forgery cookie/header pair is threaded automatically; auth comes
// from the standard Umbraco backoffice cookie.
//
// Returns Promise<{ eventKey }> on HTTP 202; rejects with an Error
// carrying { status, message } on 4xx/5xx (per spec Clarification §2).

const ENDPOINT_PATH =
  "/umbraco/management/api/v1/analyzer/custom-event";

const XSRF_COOKIE_NAME = "UMB-XSRF-TOKEN";
const XSRF_HEADER_NAME = "X-UMB-XSRF-TOKEN";

export interface CustomEventResponse {
  eventKey: string;
}

export interface CustomEventError extends Error {
  status: number;
  message: string;
}

/**
 * Capture one custom event. `kind` is currently fixed to "event"; slice
 * 006+ will accept "video", "scroll", etc. and dispatch to dedicated
 * endpoints under the same `window.analyzer` namespace.
 */
export async function send(
  kind: "event",
  category: string,
  action: string,
  label?: string,
  value?: number,
): Promise<CustomEventResponse> {
  if (kind !== "event") {
    throw Object.assign(new Error(`Unsupported send kind: ${kind}`), {
      status: 0,
      message: `Unsupported send kind: ${kind}`,
    });
  }

  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    Accept: "application/json",
  };
  const xsrf = readCookie(XSRF_COOKIE_NAME);
  if (xsrf !== null) {
    headers[XSRF_HEADER_NAME] = xsrf;
  }

  const body = JSON.stringify({ category, action, label, value });

  const response = await fetch(ENDPOINT_PATH, {
    method: "POST",
    credentials: "same-origin",
    headers,
    body,
  });

  if (response.status !== 202) {
    let message: string;
    try {
      message = await response.text();
    } catch {
      message = response.statusText;
    }
    const err: CustomEventError = Object.assign(new Error(message), {
      status: response.status,
      message,
    });
    throw err;
  }

  return (await response.json()) as CustomEventResponse;
}

/**
 * Read a cookie value by name from `document.cookie`. Returns null when
 * the cookie isn't set; tolerates SSR-style environments where
 * `document` is undefined (returns null).
 */
function readCookie(name: string): string | null {
  if (typeof document === "undefined" || !document.cookie) {
    return null;
  }
  const prefix = `${name}=`;
  for (const segment of document.cookie.split(";")) {
    const trimmed = segment.trim();
    if (trimmed.startsWith(prefix)) {
      return decodeURIComponent(trimmed.slice(prefix.length));
    }
  }
  return null;
}
