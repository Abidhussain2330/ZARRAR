/** Format a nullable SOC CPU sample without assuming the host can provide it. */
export const formatSocPercent = (value?: number | null): string =>
  value == null ? '—' : `${value.toFixed(1)}%`

/** Format nullable byte metrics in the compact unit used by the team view. */
export const formatSocMegabytes = (value?: number | null): string =>
  value == null ? '—' : `${Math.round(value / 1024 ** 2)} MB`
