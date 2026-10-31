import { FC, ReactNode } from 'react'
import classes from '@Styles/zarrar/States.module.css'

/**
 * Panel already fixes its own title at h2, so a state rendered in a panel body
 * is an h3. Callers placing one directly under the page h1 pass 2.
 */
export type ZarrarHeadingLevel = 2 | 3 | 4 | 5 | 6

export interface StateShellProps {
  icon: ReactNode
  title: ReactNode
  description?: ReactNode
  /** Buttons or links. Rendered after the description, in the tab order. */
  action?: ReactNode
  /** Technical detail — a request id, a required role, a stack. */
  detail?: ReactNode
  headingLevel?: ZarrarHeadingLevel
  tone?: 'neutral' | 'critical' | 'warning'
  /** `alert` for errors, `status` for transient conditions, omitted otherwise. */
  role?: 'alert' | 'status'
  ariaLive?: 'polite' | 'assertive'
  compact?: boolean
  className?: string
}

/**
 * Shared layout for every terminal state. Exists so the five states cannot
 * drift apart in spacing, heading semantics or focus behaviour.
 */
export const StateShell: FC<StateShellProps> = ({
  icon,
  title,
  description,
  action,
  detail,
  headingLevel = 3,
  tone = 'neutral',
  role,
  ariaLive,
  compact = false,
  className,
}) => {
  const Heading = `h${headingLevel}` as 'h2' | 'h3' | 'h4' | 'h5' | 'h6'

  return (
    <div
      className={[classes.state, className ?? ''].filter(Boolean).join(' ')}
      data-tone={tone}
      data-compact={compact || undefined}
      role={role}
      aria-live={ariaLive}
    >
      <span className={classes.stateIcon} aria-hidden>
        {icon}
      </span>
      <Heading className={classes.stateTitle}>{title}</Heading>
      {description && <p className={classes.stateDescription}>{description}</p>}
      {detail && <p className={classes.stateDetail}>{detail}</p>}
      {action && <div className={classes.stateActions}>{action}</div>}
    </div>
  )
}
