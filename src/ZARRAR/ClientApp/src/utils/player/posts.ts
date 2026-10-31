/**
 * Shared post classification.
 *
 * Sponsors are identified by tag rather than a dedicated column: the Post entity has no such
 * field and adding one needs an EF migration. Kept here — not duplicated in the feed and the
 * detail page — so the two can't drift and disagree about what counts as a sponsor.
 */
export const SPONSOR_TAGS = ['sponsor', 'sponsors', 'partner', 'partners']

export const isSponsorPost = (tags?: string[] | null) =>
  (tags ?? []).some((tag) => SPONSOR_TAGS.includes(tag.trim().toLowerCase()))
