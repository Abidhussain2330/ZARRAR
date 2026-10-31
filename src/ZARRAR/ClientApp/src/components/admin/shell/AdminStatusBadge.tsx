import { FC, ReactNode } from 'react'
import classes from '@Styles/admin/AdminControls.module.css'

export type AdminTone = 'neutral' | 'accent' | 'success' | 'warning' | 'critical' | 'info'

export interface AdminStatusBadgeProps {
  label: ReactNode
  tone?: AdminTone
  /** Tints the background as well as the border. Use for the row's headline state. */
  solid?: boolean
  /**
   * Leading glyph. Defaults to a per-tone mark so state survives greyscale,
   * colour blindness and forced-colours mode — the badge never relies on hue.
   */
  glyph?: ReactNode
  title?: string
}

const TONE_GLYPH: Record<AdminTone, string> = {
  neutral: '•',
  accent: '◆',
  success: '✓',
  warning: '!',
  critical: '✕',
  info: 'i',
}

/** The one status chip used across every admin table, card and header. */
export const AdminStatusBadge: FC<AdminStatusBadgeProps> = ({
  label,
  tone = 'neutral',
  solid = false,
  glyph,
  title,
}) => (
  <span className={classes.badge} data-tone={tone} data-solid={solid || undefined} title={title}>
    <span className={classes.badgeGlyph} aria-hidden>
      {glyph ?? TONE_GLYPH[tone]}
    </span>
    <span className={classes.badgeLabel}>{label}</span>
  </span>
)
