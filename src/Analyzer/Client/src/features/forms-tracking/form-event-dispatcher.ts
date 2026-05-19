// Analyzer slice-005 — form-event POST dispatcher.
//
// Best-effort POST helper for the lifecycle management endpoint at
// `/umbraco/management/api/v1/analyzer/form-event/lifecycle`. Threads
// the Umbraco anti-forgery cookie/header pair so the
// Principle-VII gate accepts the request; surfaces non-2xx errors via
// `console.warn` (capture is fire-and-forget — never break the host
// page on a captured-event failure).
//
// Field-level POSTs land in `/form-event/field` in slice-005 US2.

const LIFECYCLE_ENDPOINT =
  "/umbraco/management/api/v1/analyzer/form-event/lifecycle";

const XSRF_COOKIE_NAME = "UMB-XSRF-TOKEN";
const XSRF_HEADER_NAME = "X-UMB-XSRF-TOKEN";

export const FormEventType = {
  Impression: 0,
  Start: 1,
  Success: 2,
  // Abandon (3) is sweeper-materialised server-side; never dispatched.
} as const;

export type FormEventType = (typeof FormEventType)[keyof typeof FormEventType];

export interface LifecyclePayload {
  formKey: string;
  contentKey: string;
  eventType: FormEventType;
  elapsedMsFromImpression?: number | null;
  elapsedMsFromStart?: number | null;
}

export interface LifecycleResponse {
  eventKey: string;
}

export async function dispatchLifecycle(
  payload: LifecyclePayload,
): Promise<LifecycleResponse | null> {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    Accept: "application/json",
  };
  const xsrf = readCookie(XSRF_COOKIE_NAME);
  if (xsrf !== null) {
    headers[XSRF_HEADER_NAME] = xsrf;
  }

  try {
    const response = await fetch(LIFECYCLE_ENDPOINT, {
      method: "POST",
      credentials: "same-origin",
      headers,
      body: JSON.stringify(payload),
    });

    if (response.status === 202) {
      return (await response.json()) as LifecycleResponse;
    }

    // Fire-and-forget: don't reject the dispatcher promise — log + continue.
    // The host page must not break because Analyzer's capture failed.
    console.warn(
      `[analyzer] form-event/lifecycle returned ${response.status} (eventType=${payload.eventType}; formKey=${payload.formKey})`,
    );
    return null;
  } catch (err) {
    console.warn("[analyzer] form-event/lifecycle dispatch failed", err);
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
