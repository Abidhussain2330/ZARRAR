import { FC, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useConfig } from '@Hooks/useConfig'
import { useUser } from '@Hooks/useUser'
import { CyberBackdrop } from '@Components/player/CyberBackdrop'
import api from '@Api'
import classes from '@Styles/pages/Landing.module.css'

/** Counters render as an em dash until the API answers, so the strip never reflows. */
const PENDING = '—'

const formatCount = (value: number | undefined) => (value === undefined ? PENDING : value.toLocaleString())

/**
 * Fires the glitch shudder on a fixed beat. The landing used to sync this to the end of a
 * looping video; the ZARRAR landing is a still key-art plate, so a timer drives it instead.
 */
const useShudderBeat = (intervalMs = 7000) => {
  const stageRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const stage = stageRef.current
    if (!stage) return
    if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return

    const timer = window.setInterval(() => {
      stage.classList.remove(classes.shudder)
      void stage.offsetWidth // force reflow so the animation restarts cleanly
      stage.classList.add(classes.shudder)
    }, intervalMs)

    return () => window.clearInterval(timer)
  }, [intervalMs])

  return { stageRef }
}

const Landing: FC = () => {
  const { t } = useTranslation()
  const { stageRef } = useShudderBeat()
  const { user } = useUser()
  const { config } = useConfig()

  // Anonymous-safe: this is the one dashboard endpoint open to logged-out callers.
  const { data: overview } = api.dashboard.useDashboardOverview({
    refreshInterval: 0,
    shouldRetryOnError: false,
  })

  usePageTitle()

  const signedIn = !!user

  return (
    <div className={classes.stage} ref={stageRef}>
      <CyberBackdrop left="SYS://ZARRAR // ARENA GATEWAY" right="● ARENA HOT" />

      {/* The two operatives, squaring off across a crackling seam (same language as the stage). */}
      <div className={`${classes.side} ${classes.sideL}`} aria-hidden="true">
        <img src="/assets/zarrar/p1-blue.jpg" alt="" />
      </div>
      <div className={`${classes.side} ${classes.sideR}`} aria-hidden="true">
        <img src="/assets/zarrar/p2-green.jpg" alt="" />
      </div>
      <svg className={classes.seam} viewBox="0 0 40 600" preserveAspectRatio="none" aria-hidden="true">
        <polyline className={classes.bolt} points="20,0 12,70 27,140 11,215 28,290 14,360 26,430 12,500 24,560 20,600" />
        <polyline
          className={`${classes.bolt} ${classes.bolt2}`}
          points="20,0 28,80 13,150 27,225 10,300 25,370 13,440 28,505 16,565 20,600"
        />
      </svg>

      <div className={classes.hud} aria-hidden="true">
        <div className={`${classes.telemetry} ${classes.telemetryLeft}`}>
          <div className={classes.telemetryKey}>MODE</div>
          <div className={`${classes.telemetryValue} ${classes.flicker}`}>1V1&nbsp;SHOWDOWN</div>
          <div className={classes.telemetryKey}>RULE</div>
          <div className={classes.telemetryValue}>FIRST&nbsp;FLAG&nbsp;TAKES&nbsp;IT</div>
        </div>

        <div className={`${classes.telemetry} ${classes.telemetryRight}`}>
          <div className={classes.telemetryKey}>STATUS</div>
          <div className={`${classes.telemetryValue} ${classes.flicker}`}>ARENA&nbsp;HOT</div>
          <div className={classes.telemetryKey}>EVENTS</div>
          <div className={classes.telemetryValue}>{formatCount(overview?.activeGameCount)}&nbsp;LIVE</div>
        </div>
      </div>

      <div className={classes.hero} id="hero">
        <img className={classes.heroLogo} src="/assets/zarrar/zarrar-logo.png" alt="ZARRAR — 1v1 Showdown CTF" />
        <h1 className={classes.headline}>
          <span className="zr-glitch" data-text="OUTHACK">
            OUTHACK
          </span>
          <br />
          <span className={classes.headlineSmall}>YOUR</span>{' '}
          <span className={`${classes.headlineAccent} zr-glitch`} data-text="RIVAL">
            RIVAL
          </span>
        </h1>
        <p className={classes.tagline}>
          <span>1V1 SHOWDOWN CTF</span>
          <i aria-hidden="true" />
          <span>SCREENS LIVE ON THE BIG SCREEN</span>
          <i aria-hidden="true" />
          <span>BY THE ARZENS</span>
        </p>
      </div>

      <div className={classes.ctaBar}>
        <div className={classes.statStrip}>
          <div className={classes.stat}>
            <b>{formatCount(overview?.participantCount)}</b>
            <span>PLAYERS</span>
          </div>
          <div className={classes.stat}>
            <b>{formatCount(overview?.challengeCount)}</b>
            <span>CHALLENGES</span>
          </div>
          <div className={classes.stat}>
            <b>{formatCount(overview?.recentSolveCount)}</b>
            <span>SOLVES TODAY</span>
          </div>
        </div>

        <div className={classes.ctaRow}>
          {signedIn ? (
            <Link className={`${classes.btn} ${classes.btnPrimary} bqFocusRing`} to="/posts">
              Enter the arena
            </Link>
          ) : (
            <>
              <Link className={`${classes.btn} ${classes.btnGhost} bqFocusRing`} to="/account/login">
                {t('account.button.login')}
              </Link>
              {config.allowRegister !== false && (
                <Link className={`${classes.btn} ${classes.btnPrimary} bqFocusRing`} to="/account/register">
                  {t('account.button.register')}
                </Link>
              )}
            </>
          )}
        </div>
      </div>

      <div className={classes.ticker} aria-hidden="true">
        <span className={classes.tickerTag}>LIVE</span>
        <span className={classes.tickerTrack}>
          <span className={classes.tickerMove}>
            {Array.from({ length: 2 }).map((_, i) => (
              <span key={i}>
                ONE TARGET AT A TIME ◆ FIRST VALID FLAG CAPTURES IT ◆ BOTH SCREENS LIVE ON THE AUDITORIUM WALL ◆ POINTS
                DECIDE THE WINNER ◆ ZARRAR BY THE ARZENS ◆{' '}
              </span>
            ))}
          </span>
        </span>
      </div>
    </div>
  )
}

export default Landing
