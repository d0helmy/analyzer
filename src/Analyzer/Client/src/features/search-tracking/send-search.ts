// Analyzer slice-007 — public helper attached to
// `window.analyzer.sendSearch(query, resultCount, options?)`. Validates
// client-side, resolves pageviewKey from one of three sources, POSTs
// via the dispatcher, and maps 202 → resolve / non-202 → reject.
//
// US2 (T042) layers the per-call opt-out check at the top of the body.
// Until then, the `{ skipped: true }` branch declared in the return
// type is unreachable — it's wired into the type so US2 wiring is
// purely additive.

import { isDocumentOptedOut } from "../../shared/opt-out-attribute";
import { dispatchSearchEvent } from "./search-event-dispatcher";
import type {
  SearchEventError,
  SearchEventResponse,
  SearchEventResult,
} from "./payload";

export interface SendSearchOptions {
  pageviewKey?: string;
}

interface AnalyzerGlobals {
  pageviewKey?: string;
}

const EMPTY_GUID = "00000000-0000-0000-0000-000000000000";

export async function sendSearch(
  query: string,
  resultCount: number,
  options?: SendSearchOptions,
): Promise<SearchEventResult> {
  // US2 / T042 — per-call opt-out check. Evaluated on every invocation
  // (differing from slice-006's init-only read) because there is no
  // long-lived listener to mute; setting the attribute live MUST stop
  // the next sendSearch call from POSTing.
  if (isDocumentOptedOut()) {
    return { skipped: true };
  }

  if (typeof query !== "string") {
    throw asError(400, "query required");
  }
  const trimmedQuery = query.trim();
  if (trimmedQuery.length === 0) {
    throw asError(400, "query required");
  }
  if (trimmedQuery.length > 256) {
    throw asError(400, "query too long");
  }
  if (
    typeof resultCount !== "number"
    || !Number.isFinite(resultCount)
    || !Number.isInteger(resultCount)
    || resultCount < 0
  ) {
    throw asError(400, "resultCount must be a non-negative integer");
  }

  const pageviewKey = resolvePageviewKey(options);
  if (pageviewKey === null) {
    throw asError(400, "pageviewKey unavailable");
  }

  const outcome = await dispatchSearchEvent({
    pageviewKey,
    query: trimmedQuery,
    resultCount,
  });

  if (outcome.status === 202) {
    const body = outcome.body as SearchEventResponse | null;
    if (body !== null && typeof body.eventKey === "string") {
      return { eventKey: body.eventKey };
    }
    throw asError(202, "missing eventKey in response");
  }

  throw asError(outcome.status, extractErrorMessage(outcome.body));
}

function resolvePageviewKey(options?: SendSearchOptions): string | null {
  const fromOptions = options?.pageviewKey;
  if (isUsableGuid(fromOptions)) {
    return fromOptions;
  }

  if (typeof window !== "undefined") {
    const globalAny = globalThis as Record<string, unknown>;
    const ns = globalAny.analyzer as AnalyzerGlobals | undefined;
    if (isUsableGuid(ns?.pageviewKey)) {
      return ns.pageviewKey as string;
    }
  }

  if (typeof document !== "undefined") {
    const meta = document.querySelector(
      'meta[name="analyzer-pageview-key"]',
    ) as HTMLMetaElement | null;
    if (meta !== null && isUsableGuid(meta.content)) {
      return meta.content;
    }
  }

  return null;
}

function isUsableGuid(value: unknown): value is string {
  return typeof value === "string"
    && value.length > 0
    && value !== EMPTY_GUID;
}

function extractErrorMessage(body: unknown): string {
  if (body === null || typeof body !== "object") {
    return "request failed";
  }
  const errors = (body as { errors?: unknown }).errors;
  if (Array.isArray(errors) && errors.length > 0) {
    const first = errors[0] as { detail?: unknown };
    if (typeof first.detail === "string") {
      return first.detail;
    }
  }
  const detail = (body as { detail?: unknown }).detail;
  if (typeof detail === "string") {
    return detail;
  }
  const message = (body as { message?: unknown }).message;
  if (typeof message === "string") {
    return message;
  }
  return "request failed";
}

function asError(status: number, message: string): SearchEventError & Error {
  return Object.assign(new Error(message), { status, message });
}
