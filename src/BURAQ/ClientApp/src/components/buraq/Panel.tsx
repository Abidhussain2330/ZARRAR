import { FC, ReactNode } from 'react'
import classes from '@Styles/buraq/Panel.module.css'

export interface PanelProps {
  /** Rendered in the header strip. Omit for a bare panel. */
  title?: ReactNode
  /** Small uppercase label above the title, e.g. a section number. */
  eyebrow?: ReactNode
  /** Right-aligned header content: filters, a live badge, a link. */
  actions?: ReactNode
  icon?: ReactNode
  children: ReactNode
  /**
   * `flat` for dense data, `raised` for the default panel, `focal` for the one
   * element per screen that should draw the eye. Only one focal panel per view —
   * the glow scale loses meaning if everything uses it.
   */
  variant?: 'flat' | 'raised' | 'focal'
  /** Removes body padding, for tables that manage their own gutters. */
  bare?: boolean
  className?: string
  /** Renders corner brackets, the technical framing used on key panels. */
  brackets?: boolean
  /** Fills the grid cell so panels in a row share a height. */
  fill?: boolean
}

/**
 * The base surface for every BURAQ screen.
 *
 * Exists so that panel chrome — border, radius, header rhythm, glow level and
 * corner brackets — is defined once. Screens compose Panels; they do not style
 * their own containers.
 */
export const Panel: FC<PanelProps> = ({
  title,
  eyebrow,
  actions,
  icon,
  children,
  variant = 'raised',
  bare = false,
  brackets = false,
  fill = false,
  className,
}) => {
  const hasHeader = Boolean(title || eyebrow || actions || icon)

  return (
    <section
      className={[classes.panel, classes[variant], fill ? classes.fill : '', className ?? '']
        .filter(Boolean)
        .join(' ')}
    >
      {brackets && (
        <>
          <span className={`${classes.bracket} ${classes.bracketTl}`} aria-hidden />
          <span className={`${classes.bracket} ${classes.bracketTr}`} aria-hidden />
          <span className={`${classes.bracket} ${classes.bracketBl}`} aria-hidden />
          <span className={`${classes.bracket} ${classes.bracketBr}`} aria-hidden />
        </>
      )}

      {hasHeader && (
        <header className={classes.header}>
          <div className={classes.headerMain}>
            {icon && (
              <span className={classes.icon} aria-hidden>
                {icon}
              </span>
            )}
            <div className={classes.headings}>
              {eyebrow && <span className={classes.eyebrow}>{eyebrow}</span>}
              {/*
                h2 rather than a configurable level: panels always sit under the
                page h1, so a fixed level keeps the document outline correct
                without every caller having to reason about it.
              */}
              {title && <h2 className={classes.title}>{title}</h2>}
            </div>
          </div>
          {actions && <div className={classes.actions}>{actions}</div>}
        </header>
      )}

      <div className={bare ? classes.bodyBare : classes.body}>{children}</div>
    </section>
  )
}
