// Analyzer slice-007 — POST helper for the search-event management
// endpoint. Threads the Umbraco anti-forgery cookie/header pair so
// the Principle-VII gate accepts the request. Returns a discriminated
// object carrying status + body so callers can map to the public
// `Promise<{ eventKey } | { skipped: true }>` rejection shape.

import type { SearchEventPayload } from "./payload";

export const SEARCH_EVENT_ENDPOINT =
  "/umbraco/management/api/v1/analyzer/search-event";

const XSRF_COOKIE_NAME = "UMB-XSRF-TOKEN";
const XSRF_HEADER_NAME = "X-UMB-XSRF-TOKEN";

export interface DispatchOutcome {
  status: number;
  body: unknown;
}

export async function dispatchSearchEvent(
  payload: SearchEventPayload,
): Promise<DispatchOutcome> {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    Accept: "application/json",
  };
  const xsrf = readCookie(XSRF_COOKIE_NAME);
  if (xsrf !== null) {
    headers[XSRF_HEADER_NAME] = xsrf;
  }

  try {
    const response = await fetch(SEARCH_EVENT_ENDPOINT, {
      method: "POST",
      credentials: "same-origin",
      keepalive: true,
      headers,
      body: JSON.stringify(payload),
    });

    let body: unknown = null;
    try {
      body = await response.json();
    } catch {
      body = null;
    }
    return { status: response.status, body };
  } catch (err) {
    // Network failure — surface as a synthetic 0 status. The caller
    // maps this to a rejected Promise per the public contract.
    return {
      status: 0,
      body: err instanceof Error ? err.message : String(err),
    };
  }
}

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
