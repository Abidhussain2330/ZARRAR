import { FC } from 'react'

/*
 * ZARRAR's temporary mark.
 *
 * A vector reconstruction of the supplied logo: a double-outlined padlock whose
 * shackle frames a spider, with the letters TAS set into a keyhole in the body.
 * This is placeholder artwork, not the official ZARRAR logo — see below.
 *
 * Vector rather than raster because the mark has to render at 24px in the
 * collapsed sidebar and at 96px in the hero without a separate asset for each
 * size, and because the stroke colour has to follow the theme.
 *
 * NOT OFFICIAL ARTWORK — REPLACE WHEN AVAILABLE
 * This glyph is a stand-in and must not be presented as ZARRAR's official logo.
 * Swap the contents of <ZarrarGlyph> for the official path data, keep the
 * 0 0 100 100 viewBox and keep using `currentColor` for strokes. Every other
 * brand component composes this one, so nothing else needs to change.
 * The packaged brand assets and README define the distributable identity.
 */

export interface ZarrarGlyphProps {
  /** Rendered size in pixels. The mark is square. */
  size?: number
  /** Adds the cyan halo. Suppressed automatically in low-energy mode. */
  glow?: boolean
  title?: string
  className?: string
}

const ZarrarGlyph: FC<{ gradientId: string }> = ({ gradientId }) => (
  <g fill="none" stroke={`url(#${gradientId})`} strokeLinecap="round" strokeLinejoin="round">
    {/* Outer shackle and body — the outer of the two padlock outlines */}
    <path d="M28 44V30a22 22 0 0 1 44 0v14" strokeWidth={3.5} />
    <rect x="14" y="44" width="72" height="46" rx="10" strokeWidth={3.5} />

    {/* Inner shackle and body — the concentric second outline */}
    <path d="M37 44V31a13 13 0 0 1 26 0v13" strokeWidth={2.5} />
    <rect x="23" y="52" width="54" height="30" rx="6" strokeWidth={2.5} />

    {/* Spider suspended inside the shackle */}
    <g strokeWidth={1.6}>
      <ellipse cx="50" cy="30" rx="4" ry="6" fill="currentColor" stroke="none" />
      <circle cx="50" cy="22.5" r="2.6" fill="currentColor" stroke="none" />
      <path d="M46 26 39 21M46 30 38 30M46 34 39 39M54 26 61 21M54 30 62 30M54 34 61 39" />
      <path d="M39 21 36 17M38 30 34 30M39 39 36 43M61 21 64 17M62 30 66 30M61 39 64 43" />
    </g>
  </g>
)

/** Keyhole silhouette carrying the TAS letterform, set into the lock body. */
const ZarrarKeyhole: FC<{ gradientId: string }> = ({ gradientId }) => (
  <g>
    <path
      d="M50 56a5.5 5.5 0 0 0-3.4 9.8L42.5 78h15l-4.1-12.2A5.5 5.5 0 0 0 50 56Z"
      fill="none"
      stroke={`url(#${gradientId})`}
      strokeWidth={2.2}
      strokeLinejoin="round"
    />
    <text
      x="50"
      y="74"
      textAnchor="middle"
      fontFamily="var(--zarrar-font-display, sans-serif)"
      fontSize="11"
      fontWeight={700}
      letterSpacing="0.5"
      fill="var(--zarrar-text-primary, #e8eefc)"
    >
      TAS
    </text>
  </g>
)

/**
 * ZARRAR's lock mark on its own, without the wordmark. Temporary artwork — see
 * the file header; not the official ZARRAR logo.
 * Use in the collapsed sidebar, the favicon, avatars and loading states.
 */
export const ZarrarMark: FC<ZarrarGlyphProps> = ({ size = 40, glow = false, title, className }) => (
  // ZARRAR: the red Z shield emblem replaces the temporary ZARRAR lock glyph. The SVG
  // glyph components above are kept (unused) so the original artwork is easy to restore.
  <img
    src="/assets/zarrar/zarrar-emblem-sm.png"
    width={size}
    height={size}
    className={className}
    alt={title ?? ''}
    aria-hidden={title ? undefined : true}
    style={{
      objectFit: 'contain',
      filter: glow ? 'drop-shadow(0 0 10px rgb(255 42 61 / 60%))' : undefined,
    }}
  />
)

export interface ZarrarLogoProps extends ZarrarGlyphProps {
  /** `full` stacks the wordmark under the mark; `horizontal` sets it beside. */
  variant?: 'full' | 'horizontal'
  /** Drops the gradient and renders in a single colour, for print and dense UI. */
  monochrome?: boolean
  /**
   * Shows the small "1v1 Showdown CTF" tagline beneath the wordmark. Only
   * meaningful with `variant="full"` — ignored in `horizontal`, which is used
   * in compact chrome (sidebar, top bar, mobile nav) where a second line does
   * not fit. Callers that already render their own tagline alongside a
   * `horizontal` logo (see `ZarrarFooter`) should leave this off to avoid
   * showing it twice.
   */
  withAttribution?: boolean
}

/**
 * The complete lockup: mark plus the ZARRAR wordmark, with the "Unified CTF
 * Platform" tagline available as a small secondary line (`withAttribution`,
 * `full` variant only). Use in the expanded sidebar, the login screen and the
 * footer.
 */
export const ZarrarLogo: FC<ZarrarLogoProps> = ({
  size = 56,
  glow = false,
  variant = 'full',
  monochrome = false,
  withAttribution = false,
  title = 'ZARRAR',
  className,
}) => {
  const horizontal = variant === 'horizontal'
  const wordSize = Math.max(11, Math.round(size * (horizontal ? 0.26 : 0.22)))
  const showAttribution = withAttribution && !horizontal

  return (
    <span
      className={className}
      role="img"
      aria-label={title}
      style={{
        display: 'inline-flex',
        flexDirection: horizontal ? 'row' : 'column',
        alignItems: 'center',
        gap: horizontal ? size * 0.22 : size * 0.14,
        color: monochrome ? 'var(--zarrar-text-primary)' : undefined,
      }}
    >
      <ZarrarMark size={size} glow={glow} />
      <span
        aria-hidden
        style={{
          display: 'inline-flex',
          flexDirection: 'column',
          alignItems: horizontal ? 'flex-start' : 'center',
          gap: showAttribution ? size * 0.05 : 0,
        }}
      >
        <span
          style={{
            fontFamily: 'var(--zarrar-font-display, sans-serif)',
            fontSize: wordSize,
            fontWeight: 700,
            letterSpacing: 'var(--zarrar-tracking-wide, 0.06em)',
            lineHeight: 1,
            whiteSpace: 'nowrap',
            color: monochrome ? 'inherit' : 'var(--zarrar-text-primary, #e8eefc)',
          }}
        >
          ZARRAR
          <sup
            style={{
              fontSize: '0.42em',
              marginInlineStart: '0.18em',
              verticalAlign: 'super',
              fontWeight: 500,
              color: 'var(--zarrar-text-muted, #6b7a99)',
            }}
          >
            ™
          </sup>
        </span>
        {showAttribution && (
          <span
            style={{
              fontFamily: 'var(--zarrar-font-sans, sans-serif)',
              fontSize: Math.max(9, Math.round(wordSize * 0.4)),
              fontWeight: 500,
              letterSpacing: 'var(--zarrar-tracking-wide, 0.06em)',
              lineHeight: 1,
              whiteSpace: 'nowrap',
              color: monochrome ? 'inherit' : 'var(--zarrar-text-muted, #6b7a99)',
            }}
          >
            1v1 Showdown CTF
          </span>
        )}
      </span>
    </span>
  )
}
