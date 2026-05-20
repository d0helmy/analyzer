// Slice 008 — number + duration formatters used by the content-app
// element. Locale-aware thousands separator via `Intl.NumberFormat`;
// duration is rendered as `Xm Ys` once it crosses one minute.

const numberFormatter = new Intl.NumberFormat();

export function formatNumber(n: number): string {
  return numberFormatter.format(n);
}

export function formatDurationSeconds(seconds: number | null): string {
  if (seconds === null) {
    return "—";
  }
  if (seconds < 60) {
    return `${seconds}s`;
  }
  const minutes = Math.floor(seconds / 60);
  const remainder = seconds % 60;
  return `${minutes}m ${remainder}s`;
}
