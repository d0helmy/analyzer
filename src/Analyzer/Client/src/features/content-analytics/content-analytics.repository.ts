// Slice 008 — minimal `fetch`-based client for the per-content-node
// Analytics management endpoint. Mirrors slice 004-007 dispatch
// helpers: relative URL, credentials included, anti-forgery token
// threaded through the conventional `UMB-XSRF-TOKEN` cookie/header
// pair.

import type { ContentAnalyticsError, ContentAnalyticsSnapshot } from "./types";

const ENDPOINT_PREFIX = "/umbraco/management/api/v1/analyzer/content-analytics";

const XSRF_COOKIE_NAME = "UMB-XSRF-TOKEN";
const XSRF_HEADER_NAME = "X-UMB-XSRF-TOKEN";

export async function fetchContentAnalytics(
  contentKey: string,
): Promise<ContentAnalyticsSnapshot> {
  const xsrfToken = readCookie(XSRF_COOKIE_NAME);
  const headers: Record<string, string> = {
    Accept: "application/json",
  };
  if (xsrfToken !== undefined) {
    headers[XSRF_HEADER_NAME] = xsrfToken;
  }

  const response = await fetch(`${ENDPOINT_PREFIX}/${encodeURIComponent(contentKey)}`, {
    method: "GET",
    credentials: "include",
    headers,
  });

  if (!response.ok) {
    const err = await readError(response);
    throw err;
  }

  return (await response.json()) as ContentAnalyticsSnapshot;
}

async function readError(response: Response): Promise<ContentAnalyticsError> {
  let title: string | undefined;
  try {
    const body = (await response.json()) as { title?: string } | undefined;
    title = body?.title;
  } catch {
    // Non-JSON response — fall back to status-only error.
  }
  return { status: response.status, title };
}

function readCookie(name: string): string | undefined {
  if (typeof document === "undefined") {
    return undefined;
  }
  const pairs = document.cookie.split(/;\s*/);
  for (const pair of pairs) {
    const [key, ...rest] = pair.split("=");
    if (key === name) {
      return decodeURIComponent(rest.join("="));
    }
  }
  return undefined;
}
