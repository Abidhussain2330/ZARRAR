import dayjs from 'dayjs'
import { FC, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { MonitorGuard } from '@Components/zarrar/MonitorGuard'
import { DuelGame, zarrarApi } from '@Utils/zarrar/api'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useUser } from '@Hooks/useUser'
import api from '@Api'
import classes from '@Styles/zarrar/Monitor.module.css'

const statusOf = (g: DuelGame, now: number) => {
  if (g.ended || now > g.endUtc) return { label: 'OVER', cls: classes.badgeOver }
  if (g.paused) return { label: 'PAUSED', cls: classes.badgePaused }
  if (now >= g.startUtc) return { label: 'LIVE', cls: classes.badgeLive }
  return { label: 'UPCOMING', cls: '' }
}

const Picker: FC = () => {
  const [games, setGames] = useState<DuelGame[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const { user, mutate } = useUser()
  const navigate = useNavigate()
  const now = Date.now()

  useEffect(() => {
    const load = () =>
      zarrarApi
        .games()
        .then((data) => {
          setGames(data)
          setError(null)
        })
        .catch((e: Error) => setError(e.message))
    load()
    const timer = window.setInterval(load, 10000)
    return () => window.clearInterval(timer)
  }, [])

  const signOut = async () => {
    await api.account.accountLogOut()
    await mutate(undefined, false)
    navigate('/monitor/login')
  }

  const duels = (games ?? []).filter((g) => g.duel)
  const others = (games ?? []).filter((g) => !g.duel)

  return (
    <>
      <div className={classes.pickerHead}>
        <h1 className={classes.pickerTitle}>Choose the match to put on the big screen</h1>
        <div className={classes.who}>
          <span>{user?.userName}</span>
          <button type="button" className={classes.ghost} onClick={signOut}>
            SIGN OUT
          </button>
        </div>
      </div>

      {error && <p className={classes.empty}>Could not load matches: {error}</p>}

      {games && duels.length === 0 && (
        <p className={classes.empty}>
          No showdown is set up yet. An admin turns a game into a showdown from Admin → ZARRAR Live.
        </p>
      )}

      <div className={classes.cards}>
        {[...duels, ...others].map((g) => {
          const status = statusOf(g, now)
          return (
            <Link key={g.id} to={`/monitor/${g.id}`} className={`${classes.card} ${g.duel ? classes.cardDuel : ''}`}>
              <div className={classes.cardTop}>
                <h2 className={classes.cardTitle}>{g.title}</h2>
                <span className={`${classes.badge} ${status.cls}`}>{status.label}</span>
              </div>
              <div className={classes.cardMeta}>
                <span>
                  <b>{g.players}</b> player{g.players === 1 ? '' : 's'}
                </span>
                <span>{dayjs(g.startUtc).format('D MMM, HH:mm')}</span>
                {!g.duel && <span>Not a showdown</span>}
              </div>
              <span className={classes.cardCta}>OPEN STAGE</span>
            </Link>
          )
        })}
      </div>
    </>
  )
}

const MonitorIndex: FC = () => {
  usePageTitle('Monitor')

  return (
    <div className={classes.page}>
      <div className={classes.bg} style={{ backgroundImage: "url('/assets/zarrar/hacker-bg-red.jpg')" }} />
      <div className={classes.bgTint} />
      <div className={classes.grid} />
      <div className={classes.scan} />
      <main className={classes.content}>
        <img className={classes.logo} src="/assets/zarrar/zarrar-logo.png" alt="ZARRAR — 1v1 Showdown CTF" />
        <div className={classes.role}>MONITOR CONSOLE</div>
        <MonitorGuard>
          <Picker />
        </MonitorGuard>
      </main>
    </div>
  )
}

export default MonitorIndex
