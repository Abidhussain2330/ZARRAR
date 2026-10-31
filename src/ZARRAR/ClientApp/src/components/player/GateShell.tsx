import { FC, InputHTMLAttributes, ReactNode, useEffect, useId, useState } from 'react'
import { Link } from 'react-router'
import { CyberBackdrop } from '@Components/player/CyberBackdrop'
import classes from '@Styles/player/Gate.module.css'

interface GateShellProps {
  /** Left column. The form, or the outcome copy on status pages. */
  children: ReactNode
  /** Hides the artwork column and narrows the frame — for short status pages. */
  slim?: boolean
  /** Shown above the heading. Omitted on the login page, which is the site entry point. */
  backTo?: string
  backLabel?: string
}

/**
 * The frame every unauthenticated page sits in: blurred hero loop, grid field,
 * HUD corners, pulsing rails, and the ZARRAR shield emblem.
 *
 * The background video is decorative and muted, so it is safe to autoplay. It is
 * skipped entirely under prefers-reduced-motion — the gradient field underneath
 * carries the look on its own.
 */
export const GateShell: FC<GateShellProps> = ({ children, slim, backTo, backLabel }) => {
  // Same body scope the arena uses, so Mantine overlays raised from the gateway
  // (the password-change confirm, notifications) match rather than reverting to
  // stock Mantine. See styles/player/mantine.css for why it lives on <body>.
  useEffect(() => {
    document.body.classList.add('bq-player')
    return () => document.body.classList.remove('bq-player')
  }, [])

  return (
    <div className={classes.page}>
      <CyberBackdrop left="SYS://ZARRAR // ACCESS GATE" right="● AWAITING CREDENTIALS" />

      <div className={`${classes.frame} ${slim ? classes.frameSlim : ''}`}>
        <div className={classes.railTop} aria-hidden="true" />
        <div className={classes.railBottom} aria-hidden="true" />
        <div className={`${classes.corner} ${classes.cTl}`} aria-hidden="true" />
        <div className={`${classes.corner} ${classes.cTr}`} aria-hidden="true" />
        <div className={`${classes.corner} ${classes.cBl}`} aria-hidden="true" />
        <div className={`${classes.corner} ${classes.cBr}`} aria-hidden="true" />

        <div className={`${classes.split} ${slim ? classes.splitSingle : ''}`}>
          <div className={classes.formCol}>
            {backTo && (
              <Link className={`${classes.backLink} bqFocusRing`} to={backTo}>
                ← {backLabel ?? 'Back'}
              </Link>
            )}
            {children}
          </div>

          {!slim && (
            <div className={classes.artCol} aria-hidden="true">
              <div className={classes.artGrid} />
              <div className={classes.artRing} />
              <div className={`${classes.artRing} ${classes.artRing2}`} />
              <div className={classes.artHex} />
              <img className={classes.artMark} src="/assets/zarrar/zarrar-emblem.png" alt="" />
              <div className={classes.artScan} />
              <div className={classes.artTint} />
              <div className={classes.artFade} />
              <div className={classes.artReadout}>
                <span>ACCESS TERMINAL</span>
                <span>ZARRAR // 1V1 SHOWDOWN</span>
                <span className={classes.artBlink}>BIOMETRIC LINK ▮</span>
              </div>
            </div>
          )}
        </div>
      </div>

      <p className={classes.byline}>
        <span className={classes.bylineDiamond} aria-hidden="true" />
        A product of <span className={classes.bylineName}>THE ARZENS</span>
      </p>
    </div>
  )
}

interface GateHeadingProps {
  title: string
  lede?: string
}

export const GateHeading: FC<GateHeadingProps> = ({ title, lede }) => (
  <div className={classes.heading}>
    <span className={`${classes.kicker} zr-cursor`}>// SECURE CHANNEL</span>
    <h1 className={classes.title}>
      <span className="zr-glitch" data-text={title}>
        {title}
      </span>
    </h1>
    {lede && <p className={classes.lede}>{lede}</p>}
  </div>
)

interface GateFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'className'> {
  label: string
  /** Single glyph in the input's left gutter. Decorative. */
  glyph?: string
  /** Adds a show/hide toggle and starts the input masked. */
  revealable?: boolean
  /** Inline message shown under the field; also marks the input invalid. */
  error?: string
}

export const GateField: FC<GateFieldProps> = ({ label, glyph = '●', revealable, error, id, ...inputProps }) => {
  const generatedId = useId()
  const fieldId = id ?? generatedId
  const errorId = `${fieldId}-error`
  const [revealed, setRevealed] = useState(false)

  const type = revealable ? (revealed ? 'text' : 'password') : (inputProps.type ?? 'text')

  return (
    <div className={classes.field}>
      <label htmlFor={fieldId}>{label}</label>
      <span className={classes.inputWrap}>
        <span className={classes.inputIcon} aria-hidden="true">
          {glyph}
        </span>
        <input
          {...inputProps}
          id={fieldId}
          type={type}
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? errorId : undefined}
          className={`${classes.input} ${revealable ? classes.inputHasReveal : ''} ${error ? classes.inputInvalid : ''}`}
        />
        {revealable && (
          <button
            type="button"
            className={`${classes.reveal} bqFocusRing`}
            aria-pressed={revealed}
            aria-label={revealed ? 'Hide password' : 'Show password'}
            onClick={() => setRevealed((v) => !v)}
          >
            {revealed ? 'HIDE' : 'SHOW'}
          </button>
        )}
      </span>
      {error && (
        <span className={classes.fieldError} id={errorId} role="alert">
          {error}
        </span>
      )}
    </div>
  )
}

/**
 * Password strength, scored on length and character variety rather than length
 * alone. The mockup used length only, which rated "aaaaaaaaaaaa" as strong.
 */
export const scorePassword = (pw: string) => {
  if (!pw) return { width: '0%', color: 'var(--bq-ink-5)', label: 'EMPTY' }

  let score = 0
  if (pw.length >= 10) score++
  if (pw.length >= 12) score++
  if (/[a-z]/.test(pw) && /[A-Z]/.test(pw)) score++
  if (/\d/.test(pw)) score++
  if (/[^\w\s]/.test(pw)) score++

  if (pw.length < 10) return { width: '25%', color: 'var(--bq-crit)', label: 'TOO SHORT' }
  if (score <= 2) return { width: '40%', color: 'var(--bq-crit)', label: 'WEAK' }
  if (score === 3) return { width: '65%', color: 'var(--bq-warn)', label: 'FAIR' }
  if (score === 4) return { width: '85%', color: '#9ade6a', label: 'GOOD' }
  return { width: '100%', color: 'var(--bq-live)', label: 'STRONG' }
}

interface StrengthMeterProps {
  password: string
}

export const StrengthMeter: FC<StrengthMeterProps> = ({ password }) => {
  const { width, color, label } = scorePassword(password)

  return (
    <div className={classes.strengthRow}>
      <span className={classes.strengthTrack}>
        <span className={classes.strengthFill} style={{ width, backgroundColor: color }} />
      </span>
      <span className={classes.strengthLabel} style={{ color }} aria-live="polite">
        {label}
      </span>
    </div>
  )
}

interface GateCheckboxProps {
  checked: boolean
  onToggle: () => void
  children: ReactNode
  id?: string
}

export const GateCheckbox: FC<GateCheckboxProps> = ({ checked, onToggle, children, id }) => (
  <button
    type="button"
    id={id}
    role="checkbox"
    aria-checked={checked}
    onClick={onToggle}
    className={`${classes.checkRow} bqFocusRing`}
  >
    <span className={`${classes.checkBox} ${checked ? classes.checkBoxOn : ''}`} aria-hidden="true">
      {checked ? '✓' : ''}
    </span>
    <span>{children}</span>
  </button>
)

export { classes as gateClasses }
