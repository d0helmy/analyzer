// Slice 008 — content-app tab rendered against every content node.
// Reads the active document's unique GUID via the workspace context,
// fetches the aggregate snapshot from the management endpoint, and
// renders one of four states: loading / populated / empty / error.
//
// Loading state uses skeleton placeholders + `aria-busy="true"`
// (Spec Clarifications §5 + FR-RPT-013). Reduced-motion supported in
// the skeleton element.

import {
  LitElement,
  css,
  customElement,
  html,
  state,
} from "@umbraco-cms/backoffice/external/lit";
import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
import { UMB_WORKSPACE_CONTEXT } from "@umbraco-cms/backoffice/workspace";
import "./skeleton.element";
import { fetchContentAnalytics } from "./content-analytics.repository";
import { formatDurationSeconds, formatNumber } from "./formatters";
import type {
  ContentAnalyticsError,
  ContentAnalyticsSnapshot,
} from "./types";

type LoadState = "loading" | "populated" | "empty" | "error";

@customElement("analyzer-content-analytics-app")
export class AnalyzerContentAnalyticsAppElement extends UmbElementMixin(LitElement) {
  @state() private _state: LoadState = "loading";
  @state() private _snapshot: ContentAnalyticsSnapshot | null = null;
  @state() private _error: ContentAnalyticsError | null = null;

  private _contentKey: string | undefined;
  private _abortController: AbortController | null = null;

  // Test seam — production code goes through the network fetch
  // helper. Vitest tests inject a stub that resolves / rejects
  // deterministically.
  protected _fetcher: (contentKey: string) => Promise<ContentAnalyticsSnapshot> =
    fetchContentAnalytics;

  override connectedCallback(): void {
    super.connectedCallback();
    this.consumeContext(UMB_WORKSPACE_CONTEXT, (context) => {
      if (!context) return;
      const ctx = context as unknown as { getUnique?: () => string | undefined };
      const key = ctx.getUnique?.();
      if (key && key !== this._contentKey) {
        this._contentKey = key;
        void this._load();
      }
    });
  }

  override disconnectedCallback(): void {
    this._abortController?.abort();
    this._abortController = null;
    super.disconnectedCallback();
  }

  private async _load(): Promise<void> {
    if (!this._contentKey) {
      return;
    }
    this._state = "loading";
    this._error = null;
    const controller = new AbortController();
    this._abortController = controller;
    try {
      const snapshot = await this._fetcher(this._contentKey);
      if (controller.signal.aborted) {
        return;
      }
      this._snapshot = snapshot;
      this._state = isEmptySnapshot(snapshot) ? "empty" : "populated";
    } catch (err) {
      if (controller.signal.aborted) {
        return;
      }
      this._error = asError(err);
      this._state = "error";
    }
  }

  private _onRetry = (): void => {
    void this._load();
  };

  protected override render() {
    if (this._state === "loading") {
      return this._renderLoading();
    }
    if (this._state === "error") {
      return this._renderError();
    }
    return this._renderSnapshot();
  }

  private _renderLoading() {
    return html`
      <section aria-busy="true" data-state="loading">
        <analyzer-content-analytics-skeleton></analyzer-content-analytics-skeleton>
      </section>
    `;
  }

  private _renderError() {
    const status = this._error?.status ?? 0;
    const title = this._error?.title ?? "Couldn't load analytics for this content.";
    return html`
      <section aria-busy="false" data-state="error">
        <h3>${title}</h3>
        <p>HTTP ${status}</p>
        <uui-button
          look="secondary"
          label="Retry"
          data-testid="retry-button"
          @click=${this._onRetry}
        ></uui-button>
      </section>
    `;
  }

  private _renderSnapshot() {
    const snapshot = this._snapshot!;
    const isEmpty = this._state === "empty";
    return html`
      <section aria-busy="false" data-state=${this._state}>
        ${snapshot.isContentCurrentlyTombstoned
          ? html`<uui-tag color="warning" data-testid="tombstone-banner"
              >This content is in the recycle bin or unpublished. Historical
              analytics are still available below.</uui-tag
            >`
          : ""}
        ${isEmpty
          ? html`<header data-testid="empty-state">
              <h3>No activity in the last 30 days.</h3>
              <p>
                Once visitors view this page, their pageviews, unique visitors,
                and average time on page appear here.
              </p>
            </header>`
          : ""}
        <dl class="metrics" data-testid="metric-grid">
          <div class="metric metric--pageviews">
            <dt>Pageviews</dt>
            <dd>
              <span>
                <strong data-testid="pageviews-24h"
                  >${formatNumber(snapshot.pageviews24h)}</strong
                >
                <small>24h</small>
              </span>
              <span>
                <strong data-testid="pageviews-7d"
                  >${formatNumber(snapshot.pageviews7d)}</strong
                >
                <small>7d</small>
              </span>
              <span>
                <strong data-testid="pageviews-30d"
                  >${formatNumber(snapshot.pageviews30d)}</strong
                >
                <small>30d</small>
              </span>
            </dd>
          </div>
          <div class="metric">
            <dt>Unique visitors</dt>
            <dd>
              <strong data-testid="unique-visitors-30d"
                >${formatNumber(snapshot.uniqueVisitors30d)}</strong
              >
              <small>30d</small>
            </dd>
          </div>
          <div class="metric">
            <dt>Avg time on page</dt>
            <dd>
              <strong data-testid="avg-time-on-page-30d"
                >${formatDurationSeconds(snapshot.avgTimeOnPageSeconds30d)}</strong
              >
              <small>30d</small>
            </dd>
          </div>
        </dl>
      </section>
    `;
  }

  static override styles = css`
    :host {
      display: block;
      padding: var(--uui-size-space-4, 1rem);
    }
    .metrics {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
      gap: var(--uui-size-space-4, 1rem);
      margin: 0;
    }
    .metric {
      background: var(--uui-color-surface-alt, #f7f7f7);
      border-radius: 4px;
      padding: var(--uui-size-space-4, 1rem);
    }
    .metric dt {
      font-size: 0.85rem;
      color: var(--uui-color-text-alt, #666);
      margin-bottom: var(--uui-size-space-2, 0.5rem);
    }
    .metric dd {
      margin: 0;
      font-size: 1.5rem;
      font-weight: 600;
      display: flex;
      gap: var(--uui-size-space-3, 0.75rem);
      align-items: baseline;
    }
    .metric--pageviews dd span {
      display: flex;
      flex-direction: column;
    }
    .metric--pageviews small,
    .metric small {
      font-size: 0.75rem;
      font-weight: 400;
      color: var(--uui-color-text-alt, #666);
    }
    header h3 {
      margin: 0 0 var(--uui-size-space-2, 0.5rem) 0;
    }
    header p {
      margin: 0 0 var(--uui-size-space-4, 1rem) 0;
      color: var(--uui-color-text-alt, #666);
    }
    [data-testid="tombstone-banner"] {
      display: inline-block;
      margin-bottom: var(--uui-size-space-4, 1rem);
    }
  `;
}

function isEmptySnapshot(snapshot: ContentAnalyticsSnapshot): boolean {
  return (
    snapshot.pageviews24h === 0 &&
    snapshot.pageviews7d === 0 &&
    snapshot.pageviews30d === 0 &&
    snapshot.uniqueVisitors30d === 0 &&
    !snapshot.isContentCurrentlyTombstoned
  );
}

function asError(err: unknown): ContentAnalyticsError {
  if (err && typeof err === "object" && "status" in err) {
    const status = (err as { status: unknown }).status;
    const title = "title" in err ? (err as { title?: string }).title : undefined;
    return {
      status: typeof status === "number" ? status : 0,
      title,
    };
  }
  return { status: 0 };
}

declare global {
  interface HTMLElementTagNameMap {
    "analyzer-content-analytics-app": AnalyzerContentAnalyticsAppElement;
  }
}
