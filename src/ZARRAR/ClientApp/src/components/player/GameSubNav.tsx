import { FC } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import classes from '@Styles/player/Challenges.module.css'

interface GameSubNavProps {
  gameId: number
  title?: string
  active: 'overview' | 'challenges' | 'scoreboard'
}

/**
 * Context bar for the pages that live inside a single event. Without it there is
 * no way back to the event from the challenge grid, and no way across to the
 * scoreboard without going through the main nav twice.
 */
export const GameSubNav: FC<GameSubNavProps> = ({ gameId, title, active }) => {
  const { t } = useTranslation()

  const tabs = [
    { key: 'overview' as const, to: `/games/${gameId}`, label: 'OVERVIEW' },
    { key: 'challenges' as const, to: `/games/${gameId}/challenges`, label: t('game.button.challenges') },
    { key: 'scoreboard' as const, to: `/games/${gameId}/scoreboard`, label: t('game.button.scoreboard') },
  ]

  return (
    <div className={classes.gameBar}>
      <div className={classes.gameBarMain}>
        <h1 className={classes.gameBarTitle}>{title ?? 'Event'}</h1>
        <p className={classes.gameBarMeta}>ZARRAR / ACTIVE MATCH</p>
      </div>

      <nav className={classes.gameTabs} aria-label="Event sections">
        {tabs.map((tab) => (
          <Link
            key={tab.key}
            to={tab.to}
            className={`${classes.gameTab} bqFocusRing ${active === tab.key ? classes.gameTabOn : ''}`}
            aria-current={active === tab.key ? 'page' : undefined}
          >
            {tab.label.toUpperCase()}
          </Link>
        ))}
      </nav>
    </div>
  )
}
