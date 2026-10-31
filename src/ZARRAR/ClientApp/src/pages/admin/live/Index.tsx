import { Badge } from '@mantine/core'
import dayjs from 'dayjs'
import { FC, useEffect, useState } from 'react'
import { Link } from 'react-router'
import { AdminLayout } from '@Components/admin/shell/AdminLayout'
import { DuelGame, zarrarApi } from '@Utils/zarrar/api'
import classes from '@Styles/zarrar/AdminLive.module.css'

/**
 * ZARRAR Live — pick the game to run. Any game can be switched into showdown mode from its page.
 */
const AdminLiveIndex: FC = () => {
  const [games, setGames] = useState<DuelGame[] | null>(null)

  useEffect(() => {
    zarrarApi
      .games()
      .then(setGames)
      .catch(() => setGames([]))
  }, [])

  return (
    <AdminLayout
      title="ZARRAR Live"
      description="Run a 1v1 showdown: release challenges, pause, end, and see exactly what the auditorium sees."
      isLoading={!games}
    >
      <div className={classes.list}>
        {(games ?? []).map((g) => (
          <Link key={g.id} to={`/admin/live/${g.id}`} className={classes.gameCard}>
            <div className={classes.row} style={{ borderBottom: 'none', padding: 0 }}>
              <div className={classes.rowMain}>
                <div className={classes.rowTitle}>{g.title}</div>
                <div className={classes.rowSub}>
                  {g.players} player{g.players === 1 ? '' : 's'} · {dayjs(g.startUtc).format('D MMM YYYY, HH:mm')}
                </div>
              </div>
              {g.duel ? (
                <Badge color={g.ended ? 'gray' : g.paused ? 'yellow' : 'red'} variant="filled">
                  {g.ended ? 'Over' : g.paused ? 'Paused' : 'Showdown'}
                </Badge>
              ) : (
                <Badge color="gray" variant="light">
                  Jeopardy
                </Badge>
              )}
            </div>
          </Link>
        ))}
      </div>
    </AdminLayout>
  )
}

export default AdminLiveIndex
