// Slice 008 — minimal loading-state skeleton for the content-app
// tab. Five placeholder rectangles in a CSS grid laid out to match
// the metric blocks. Shimmer animation honours
// `prefers-reduced-motion`.

import { LitElement, html, css, customElement } from "@umbraco-cms/backoffice/external/lit";

@customElement("analyzer-content-analytics-skeleton")
export class AnalyzerContentAnalyticsSkeleton extends LitElement {
  static override styles = css`
    :host {
      display: block;
    }

    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
      gap: 12px;
    }

    .block {
      height: 64px;
      border-radius: 4px;
      background: linear-gradient(
        90deg,
        rgba(0, 0, 0, 0.06) 0%,
        rgba(0, 0, 0, 0.12) 50%,
        rgba(0, 0, 0, 0.06) 100%
      );
      background-size: 200% 100%;
      animation: shimmer-pulse 1.2s ease-in-out infinite;
    }

    @keyframes shimmer-pulse {
      0% { background-position: 200% 0; }
      100% { background-position: -200% 0; }
    }

    @media (prefers-reduced-motion: reduce) {
      .block {
        animation: none;
        background: rgba(0, 0, 0, 0.08);
      }
    }
  `;

  protected override render() {
    return html`
      <div class="grid" data-testid="skeleton-grid">
        <div class="block"></div>
        <div class="block"></div>
        <div class="block"></div>
        <div class="block"></div>
        <div class="block"></div>
      </div>
    `;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    "analyzer-content-analytics-skeleton": AnalyzerContentAnalyticsSkeleton;
  }
}
