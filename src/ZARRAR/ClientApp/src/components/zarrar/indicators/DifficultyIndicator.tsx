import { FC } from 'react'
import { useTranslation } from 'react-i18next'
import classes from '@Styles/zarrar/Indicators.module.css'

export type ZarrarDifficulty = 'easy' | 'medium' | 'hard' | 'expert' | 'insane'

/** Ascending order — index doubles as the rank used to fill the segments. */
export const ZARRAR_DIFFICULTIES: readonly ZarrarDifficulty[] = ['easy', 'medium', 'hard', 'expert', 'insane']

const DIFFICULTY_LABEL_DEFAULTS: Record<ZarrarDifficulty, string> = {
  easy: 'Easy',
  medium: 'Medium',
  hard: 'Hard',
  expert: 'Expert',
  insane: 'Insane',
}

export interface DifficultyIndicatorProps {
  level: ZarrarDifficulty
  /** Replaces the built-in English word. The label itself can never be hidden. */
  label?: string
  size?: 'sm' | 'md'
  /** Read before the label so a bare 'Hard' has context out of the page flow. */
  srPrefix?: string
  className?: string
}

/**
 * Segmented difficulty meter.
 *
 * The word is always rendered: the bars alone would make difficulty a
 * colour-and-count signal, which fails for colour-blind and low-vision users.
 */
export const DifficultyIndicator: FC<DifficultyIndicatorProps> = ({ level, label, size = 'md', srPrefix, className }) => {
  const { t } = useTranslation()
  const rank = ZARRAR_DIFFICULTIES.indexOf(level)
  const resolvedSrPrefix = srPrefix ?? t('common.indicator.difficulty.prefix', { defaultValue: 'Difficulty' })
  const resolvedLabel =
    label ??
    t(`common.indicator.difficulty.${level}`, { defaultValue: DIFFICULTY_LABEL_DEFAULTS[level] })

  return (
    <span
      className={[classes.difficulty, className ?? ''].filter(Boolean).join(' ')}
      data-level={level}
      data-size={size}
    >
      <span className={classes.difficultyBars} aria-hidden>
        {ZARRAR_DIFFICULTIES.map((step, index) => (
          <span key={step} className={classes.difficultyBar} data-filled={index <= rank || undefined} />
        ))}
      </span>
      <span className={classes.srOnly}>{`${resolvedSrPrefix}: `}</span>
      <span className={classes.difficultyLabel}>{resolvedLabel}</span>
    </span>
  )
}
