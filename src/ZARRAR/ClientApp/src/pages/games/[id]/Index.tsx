import { useModals } from '@mantine/modals'
import { showNotification } from '@mantine/notifications'
import { mdiCheck } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, ReactNode, useEffect, useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router'
import { GameJoinModal } from '@Components/GameJoinModal'
import { Markdown } from '@Components/MarkdownRenderer'
import { Badge, Loading, arena } from '@Components/player/Arena'
import { PlayerShell } from '@Components/player/PlayerShell'
import { useLanguage } from '@Utils/I18n'
import { showErrorMsg } from '@Utils/Shared'
import { useIsMobile } from '@Utils/ThemeOverride'
import { GameStatus, getGameStatus, toLimitTag, useGame } from '@Hooks/useGame'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useTeams, useUser } from '@Hooks/useUser'
import api, { GameJoinModel, ParticipationStatus } from '@Api'
import classes from '@Styles/player/GameDetail.module.css'

type NoticeTone = 'info' | 'warn' | 'bad' | 'ok'

/**
 * Ticks once a second toward a target instant.
 *
 * Stops scheduling once the target passes so a finished event doesn't keep re-rendering the
 * page forever in a background tab.
 */
const useCountdown = (target: dayjs.Dayjs | null) => {
  const [, force] = useState(0)

  useEffect(() => {
    if (!target) return
    if (dayjs().isAfter(target)) return
    const timer = window.setInterval(() => force((n) => n + 1), 1000)
    return () => window.clearInterval(timer)
  }, [target])

  if (!target) return null

  const diff = target.diff(dayjs(), 'second')
  if (diff <= 0) return null

  return {
    days: Math.floor(diff / 86400),
    hours: Math.floor((diff % 86400) / 3600),
    minutes: Math.floor((diff % 3600) / 60),
    seconds: diff % 60,
  }
}

const Countdown: FC<{ label: string; target: dayjs.Dayjs }> = ({ label, target }) => {
  const left = useCountdown(target)
  if (!left) return null

  const cells: [number, string][] = [
    [left.days, 'DAYS'],
    [left.hours, 'HRS'],
    [left.minutes, 'MIN'],
    [left.seconds, 'SEC'],
  ]

  return (
    <div className={classes.countdown}>
      <span className={classes.countdownLabel}>{label}</span>
      <div className={classes.countdownCells}>
        {cells.map(([value, unit]) => (
          <span key={unit} className={classes.countdownCell}>
            <span className={classes.countdownValue}>{String(value).padStart(2, '0')}</span>
            <span className={classes.countdownUnit}>{unit}</span>
          </span>
        ))}
      </div>
    </div>
  )
}

const Notice: FC<{ tone: NoticeTone; title: string; children?: ReactNode }> = ({ tone, title, children }) => (
  <div className={`${classes.notice} ${classes[tone]}`} role="status">
    <p className={classes.noticeTitle}>{title}</p>
    {children && <div className={classes.noticeBody}>{children}</div>}
  </div>
)

const GameDetail: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')
  const navigate = useNavigate()

  const { game, error, mutate, status } = useGame(numId)
  const { startTime, endTime, finished, started, progress, status: timeStatus } = getGameStatus(game)

  const { locale } = useLanguage()
  const { user } = useUser()
  const { teams } = useTeams()

  const modals = useModals()
  const isMobile = useIsMobile()
  const { t } = useTranslation()

  const [joinModalOpen, setJoinModalOpen] = useState(false)

  usePageTitle(game?.title)

  useEffect(() => {
    if (error) {
      showErrorMsg(error, t)
      navigate('/games')
    }
  }, [error, navigate, t])

  const GameActionMap = new Map([
    [ParticipationStatus.Pending, t('game.participation.actions.pending')],
    [ParticipationStatus.Accepted, t('game.participation.actions.accepted')],
    [ParticipationStatus.Rejected, t('game.participation.actions.rejected')],
    [ParticipationStatus.Suspended, t('game.participation.actions.suspended')],
    [ParticipationStatus.Unsubmitted, t('game.participation.actions.unsubmitted')],
  ])

  const onSubmitJoin = async (info: GameJoinModel) => {
    try {
      if (!numId) return
      await api.game.gameJoinGame(numId, info)
      showNotification({
        color: 'teal',
        message: t('game.notification.joined'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      mutate()
    } catch (err) {
      return showErrorMsg(err, t)
    }
  }

  const onSubmitLeave = async () => {
    try {
      if (!numId) return
      await api.game.gameLeaveGame(numId)
      showNotification({
        color: 'teal',
        message: t('game.notification.left'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      mutate()
    } catch (err) {
      return showErrorMsg(err, t)
    }
  }

  // Practice mode keeps a finished game joinable, so "finished" alone can't gate this.
  const isGameOpenForJoin = !finished || game?.practiceMode

  const canSubmit =
    (status === ParticipationStatus.Unsubmitted || status === ParticipationStatus.Rejected) &&
    isGameOpenForJoin &&
    user &&
    teams &&
    teams.length > 0

  const teamRequire =
    user && status === ParticipationStatus.Unsubmitted && isGameOpenForJoin && teams && teams.length === 0

  const onJoin = () =>
    modals.openConfirmModal({
      title: t('game.content.join.confirm'),
      children: (
        <div className={classes.modalCopy}>
          <p>{t('game.content.join.content.0')}</p>
          <p>
            <Trans i18nKey="game.content.join.content.1" />
          </p>
          <p>
            <Trans i18nKey="game.content.join.content.2" />
          </p>
        </div>
      ),
      onConfirm: () => setJoinModalOpen(true),
    })

  const onLeave = () =>
    modals.openConfirmModal({
      title: t('game.content.leave.confirm'),
      children: (
        <div className={classes.modalCopy}>
          <p>{t('game.content.leave.content.0')}</p>
          <p>{t('game.content.leave.content.1')}</p>
        </div>
      ),
      onConfirm: onSubmitLeave,
    })

  if (!game) {
    return (
      <PlayerShell>
        <Loading label="Loading event…" />
      </PlayerShell>
    )
  }

  const statusColor =
    timeStatus === GameStatus.OnGoing
      ? 'var(--bq-live)'
      : timeStatus === GameStatus.Coming
        ? 'var(--bq-solved)'
        : 'var(--bq-locked)'

  const statusLabel =
    timeStatus === GameStatus.OnGoing ? 'LIVE' : timeStatus === GameStatus.Coming ? 'UPCOMING' : 'ENDED'

  return (
    <PlayerShell>
      <Link to="/games" className={`${classes.back} bqFocusRing`}>
        ← All events
      </Link>

      <section className={classes.hero}>
        <div className={classes.heroMain}>
          <div className={classes.badges}>
            <Badge color={statusColor} live={timeStatus === GameStatus.OnGoing}>
              {statusLabel}
            </Badge>
            <span className={classes.metaTag}>{toLimitTag(t, game.limit)}</span>
            {game.hidden && <span className={classes.metaTag}>{t('game.tag.hidden')}</span>}
            {game.practiceMode && <span className={classes.metaTag}>PRACTICE</span>}
          </div>

          <h1 className={classes.title}>{game.title}</h1>

          <p className={classes.joined}>
            <Trans i18nKey="game.content.joined_status" values={{ count: game.teamCount ?? 0 }} />
          </p>

          <div className={classes.timing}>
            <div>
              <p className={classes.timingLabel}>{t('game.content.start_time')}</p>
              <p className={classes.timingValue}>{startTime.locale(locale).format('LLL')}</p>
            </div>
            <div>
              <p className={classes.timingLabel}>{t('game.content.end_time')}</p>
              <p className={classes.timingValue}>{endTime.locale(locale).format('LLL')}</p>
            </div>
          </div>

          {!finished && (
            <Countdown label={started ? 'ENDS IN' : 'STARTS IN'} target={started ? endTime : startTime} />
          )}

          <div className={classes.progressTrack} aria-hidden="true">
            <span className={classes.progressFill} style={{ width: `${Math.min(100, progress)}%` }} />
          </div>

          <div className={classes.actions}>
            <button
              type="button"
              className={`${arena.btn} ${arena.btnPrimary} bqFocusRing`}
              disabled={!canSubmit}
              onClick={onJoin}
            >
              {!isGameOpenForJoin
                ? t('game.button.finished')
                : !user
                  ? t('game.button.login_required')
                  : GameActionMap.get(status)}
            </button>

            {started && (
              <Link
                to={`/games/${numId}/scoreboard`}
                className={`${arena.btn} ${arena.btnGhost} bqFocusRing`}
              >
                {t('game.button.scoreboard')}
              </Link>
            )}

            {status === ParticipationStatus.Accepted && started && (!finished || game.practiceMode) && (
              <Link
                to={`/games/${numId}/challenges`}
                className={`${arena.btn} ${arena.btnPrimary} bqFocusRing`}
              >
                {t('game.button.challenges')}
              </Link>
            )}

            {(status === ParticipationStatus.Pending || status === ParticipationStatus.Rejected) && (
              <button type="button" className={`${arena.btn} ${arena.btnDanger} bqFocusRing`} onClick={onLeave}>
                {t('game.button.leave')}
              </button>
            )}
          </div>
        </div>

        <div className={classes.heroPoster}>
          {game.poster ? (
            <img src={game.poster} alt="" />
          ) : (
            <span className={classes.posterFallback} aria-hidden="true">
              {(game.title ?? '?').slice(0, 2).toUpperCase()}
            </span>
          )}
        </div>
      </section>

      <div className={classes.columns}>
        <div className={classes.panel}>
          <h2 className={classes.panelTitle}>About this event</h2>
          <div className={classes.markdown}>
            <Markdown source={game.content ?? ''} />
          </div>
        </div>

        <aside className={classes.side}>
          <div className={classes.panel}>
            <h2 className={classes.panelTitle}>Your status</h2>
            <div className={classes.noticeStack}>
              {status === ParticipationStatus.Pending && (
                <Notice tone="warn" title={t('game.participation.alert.pending.title', { team: game.teamName ?? '' })}>
                  {t('game.participation.alert.pending.content')}
                </Notice>
              )}
              {status === ParticipationStatus.Rejected && (
                <Notice tone="bad" title={t('game.participation.alert.rejected.title')}>
                  {t('game.participation.alert.rejected.content')}
                </Notice>
              )}
              {status === ParticipationStatus.Suspended && (
                <Notice
                  tone="bad"
                  title={t('game.participation.alert.suspended.title', { team: game.teamName ?? '' })}
                >
                  {t('game.participation.alert.suspended.content')}
                </Notice>
              )}
              {teamRequire && (
                <Notice tone="warn" title={t('game.participation.alert.team_required.title')}>
                  <Trans i18nKey="game.participation.alert.team_required.content">
                    _
                    <Link to="/teams" className={classes.noticeLink}>
                      _
                    </Link>
                    _
                  </Trans>
                </Notice>
              )}
              {status === ParticipationStatus.Accepted && !started && (
                <Notice tone="ok" title={t('game.participation.alert.not_started.title')}>
                  {t('game.participation.alert.not_started.content', { team: game.teamName ?? '' })}
                  {isMobile && t('game.participation.alert.not_started.mobile')}
                </Notice>
              )}
              {status === ParticipationStatus.Accepted && started && (
                <Notice tone="ok" title="You're in">
                  Competing as {game.teamName}. Head to the challenges to start solving.
                </Notice>
              )}
              {status === ParticipationStatus.Unsubmitted && !teamRequire && (
                <Notice tone="info" title="Not registered yet">
                  Join this event to unlock its challenges.
                </Notice>
              )}
            </div>
          </div>
        </aside>
      </div>

      <GameJoinModal
        title={t('game.content.join.title')}
        opened={joinModalOpen}
        withCloseButton={false}
        onClose={() => setJoinModalOpen(false)}
        onSubmitJoin={onSubmitJoin}
      />
    </PlayerShell>
  )
}

export default GameDetail
