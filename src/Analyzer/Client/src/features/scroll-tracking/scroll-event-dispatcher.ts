// Analyzer slice-006 — scroll-milestone POST dispatcher.
//
// Best-effort POST helper for the milestone management endpoint at
// `/umbraco/management/api/v1/analyzer/scroll-event/milestone`.
// Threads the Umbraco anti-forgery cookie/header pair so the
// Principle-VII gate accepts the request; surfaces non-2xx errors via
// `console.warn` (capture is fire-and-forget — never break the host
// page on a captured-event failure). 409 responses (DB-enforced
// idempotency rejection) are silent at warn level; they indicate the
// server already has this (pageview, bucket) row.

export const ScrollBucket = {
  Quarter: 25,
  Half: 50,
  ThreeQuarters: 75,
  Full: 100,
} as const;

export type ScrollBucket = (typeof ScrollBucket)[keyof typeof ScrollBucket];

const MILESTONE_ENDPOINT =
  "/umbraco/management/api/v1/analyzer/scroll-event/milestone";

const XSRF_COOKIE_NAME = "UMB-XSRF-TOKEN";
const XSRF_HEADER_NAME = "X-UMB-XSRF-TOKEN";

export interface MilestonePayload {
  pageviewKey: string;
  contentKey: string;
  bucket: ScrollBucket;
}

export interface MilestoneResponse {
  eventKey: string;
}

export async function dispatchMilestone(
  payload: MilestonePayload,
): Promise<MilestoneResponse | null> {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    Accept: "application/json",
  };
  const xsrf = readCookie(XSRF_COOKIE_NAME);
  if (xsrf !== null) {
    headers[XSRF_HEADER_NAME] = xsrf;
  }

  try {
    const response = await fetch(MILESTONE_ENDPOINT, {
      method: "POST",
      credentials: "same-origin",
      keepalive: true,
      headers,
      body: JSON.stringify(payload),
    });

    if (response.status === 202) {
      return (await response.json()) as MilestoneResponse;
    }

    // 409: DB unique-index rejected a same-tuple replay. Operationally
    // OK — the row is already on the server; do not raise client noise.
    if (response.status === 409) {
      return null;
    }

    console.warn(
      `[analyzer] scroll-event/milestone returned ${response.status}`,
    );
    return null;
  } catch (err) {
    console.warn("[analyzer] scroll-event/milestone dispatch failed", err);
    return null;
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
