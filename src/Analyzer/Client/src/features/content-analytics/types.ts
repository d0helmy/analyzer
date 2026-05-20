// Slice 008 — TypeScript mirror of the server-side
// `Analyzer.Reporting.ContentAnalytics.ContentAnalyticsSnapshot` DTO.
// Field names mirror the JSON wire shape (camelCase via the default
// management-API serialiser).

export interface ContentAnalyticsSnapshot {
  contentKey: string;
  windowEndUtc: string;
  pageviews24h: number;
  pageviews7d: number;
  pageviews30d: number;
  uniqueVisitors30d: number;
  avgTimeOnPageSeconds30d: number | null;
  isContentCurrentlyTombstoned: boolean;
  topReferrers30d: string[];
}

export interface ContentAnalyticsError {
  status: number;
  title?: string;
}
