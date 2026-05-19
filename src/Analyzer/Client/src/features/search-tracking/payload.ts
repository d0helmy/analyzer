// Analyzer slice-007 — shared payload + response types for the
// search-event POST module. Consumed by both the dispatcher and the
// `sendSearch` public helper; the field discipline mirrors the server
// payload contract (controller-level reject pass on unknown fields).

export interface SearchEventPayload {
  pageviewKey: string;
  query: string;
  resultCount: number;
}

export interface SearchEventResponse {
  eventKey: string;
}

export interface SearchEventError {
  status: number;
  message: string;
}

export type SearchEventResult =
  | { eventKey: string }
  | { skipped: true };
