import { FC } from 'react'
import { useParams } from 'react-router'
import { MonitorGuard } from '@Components/zarrar/MonitorGuard'
import { Stage } from '@Components/zarrar/Stage'
import { MonitorTagDrawer } from '@Components/zarrar/TagRoster'
import { usePageTitle } from '@Hooks/usePageTitle'
import classes from '@Styles/zarrar/Monitor.module.css'

/**
 * The auditorium screen. Open this on the monitor laptop, press "Enter the arena" (full screen +
 * sound) and share/extend that display to the projector. Everything on it is driven from the
 * admin's ZARRAR Live page.
 *
 * Tag team: press T (or the TAG button that shows when the mouse moves) to pick which member of
 * each team is on the keyboard. The drawer is hidden from the audience otherwise.
 */
const MonitorStage: FC = () => {
  const { id } = useParams()
  const gameId = Number.parseInt(id ?? '-1', 10)

  usePageTitle('ZARRAR Stage')

  return (
    <MonitorGuard>
      <div className={classes.stageWrap}>
        <Stage gameId={gameId} />
        <MonitorTagDrawer gameId={gameId} />
      </div>
    </MonitorGuard>
  )
}

export default MonitorStage
