/**
 * A post-login `?from=` target, only if it stays on this site.
 *
 * Accepts an app path ("/games/3/challenges"); rejects absolute URLs, protocol-relative
 * ("//evil.example") and backslash tricks ("/\evil.example"), which some browsers treat as
 * another host. Anything else falls back to `fallback`.
 */
export const safeRedirect = (from: string | null | undefined, fallback: string): string => {
  if (!from) return fallback
  if (!from.startsWith('/') || from.startsWith('//') || from.startsWith('/\\')) return fallback
  // Control characters (tabs/newlines are stripped by URL parsers) could smuggle a scheme.
  // eslint-disable-next-line no-control-regex
  if (/[\u0000-\u001f\u007f]/.test(from)) return fallback
  return from
}
