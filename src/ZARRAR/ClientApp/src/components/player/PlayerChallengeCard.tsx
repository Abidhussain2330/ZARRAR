import dayjs from 'dayjs'
import { FC } from 'react'
import { categoryColor } from '@Components/player/Arena'
import { ChallengeInfo, ChallengeType } from '@Api'
import classes from '@Styles/player/Challenges.module.css'

export type ViewMode = 'grid' | 'list'
export type Difficulty = 'easy' | 'medium' | 'hard' | 'expert' | 'insane'

/**
 * The backend's difficulty is a coefficient, not a tier. This mirrors the mapping
 * the existing ChallengeCard used, so a challenge doesn't change difficulty label
 * depending on which page you're looking at.
 */
export const toDifficulty = (value: number): Difficulty => {
  if (value <= 3) return 'easy'
  if (value <= 4) return 'medium'
  if (value <= 5) return 'hard'
  if (value <= 6) return 'expert'
  return 'insane'
}

/**
 * Five tiers, not the mockup's three: the backend distinguishes expert and insane,
 * and collapsing them would hide a real difference between challenges.
 */
export const DIFFICULTY: Record<Difficulty, { bars: number; color: string; label: string }> = {
  easy: { bars: 1, color: 'var(--bq-diff-easy)', label: 'EASY' },
  medium: { bars: 2, color: 'var(--bq-diff-medium)', label: 'MEDIUM' },
  hard: { bars: 3, color: 'var(--bq-diff-hard)', label: 'HARD' },
  expert: { bars: 4, color: '#f97316', label: 'EXPERT' },
  insane: { bars: 5, color: '#d946ef', label: 'INSANE' },
}

const TYPE_LABEL: Record<ChallengeType, string | null> = {
  [ChallengeType.StaticAttachment]: 'FILE',
  [ChallengeType.DynamicAttachment]: 'FILE',
  [ChallengeType.StaticContainer]: 'CONTAINER',
  [ChallengeType.DynamicContainer]: 'CONTAINER',
}

const BLOOD_COLORS = ['#ffd166', '#c7ccd8', '#cd7f4e']

/**
 * Formats the time left before a challenge closes.
 *
 * Deliberately not dayjs `.fromNow()`: that needs the relativeTime plugin, which
 * this app doesn't load, and calling dayjs.extend here would mutate the shared
 * instance the admin pages also use.
 */
const timeLeft = (deadline: dayjs.Dayjs) => {
  const minutes = deadline.diff(dayjs(), 'minute')
  if (minutes <= 0) return 'closing now'
  if (minutes < 60) return `in ${minutes}m`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `in ${hours}h ${minutes % 60}m`
  return `in ${Math.floor(hours / 24)}d`
}

interface ChallengeCardProps {
  challenge: ChallengeInfo
  solved: boolean
  locked: boolean
  onClick: () => void
}

export const PlayerChallengeCard: FC<ChallengeCardProps> = ({ challenge, solved, locked, onClick }) => {
  const accent = categoryColor(challenge.category)
  const difficulty = DIFFICULTY[toDifficulty(challenge.difficulty)]
  const typeLabel = TYPE_LABEL[challenge.type]

  const status = solved ? 'SOLVED' : locked ? 'LOCKED' : 'AVAILABLE'
  const statusColor = solved ? 'var(--bq-solved)' : locked ? 'var(--bq-locked)' : 'var(--bq-live)'

  // Deadlines are per-challenge and independent of the game clock, so a player can
  // lose access to one challenge while the event runs on.
  const deadline = challenge.deadline ? dayjs(challenge.deadline) : null
  const showDeadline = deadline && !locked && deadline.diff(dayjs(), 'day') < 3

  const bloods = (challenge.bloods ?? []).slice(0, 3)

  // ChallengeInfo carries no description or image today; both are read defensively so the card
  // picks them up automatically once the backend provides them.
  const extra = challenge as unknown as { blurb?: string | null; image?: string | null }
  const blurb = extra.blurb ?? null
  const image = extra.image ?? null

  return (
    <button
      type="button"
      onClick={onClick}
      disabled={locked}
      aria-label={`${challenge.title}, ${challenge.score} points, ${difficulty.label.toLowerCase()}, ${status.toLowerCase()}`}
      className={`${classes.card} bqFocusRing ${locked ? classes.cardLocked : ''} ${solved ? classes.cardSolved : ''}`}
    >
      <span className={classes.accent} style={{ background: accent }} aria-hidden="true" />

      <div className={classes.cardHead}>
        <span className={classes.category} style={{ color: accent }}>
          <span className={classes.categorySquare} style={{ background: accent }} aria-hidden="true" />
          {challenge.category}
        </span>
        {typeLabel && <span className={classes.typeTag}>{typeLabel}</span>}
      </div>

      <div className={classes.cardBody}>
        <div className={classes.cardCopy}>
          <h2 className={classes.cardTitle}>{challenge.title}</h2>
          {blurb && <p className={classes.cardBlurb}>{blurb}</p>}

          {bloods.length > 0 && !challenge.disableBloodBonus && (
            <div className={classes.bloods}>
              <span className={classes.bloodLabel}>First</span>
              {bloods.map((blood, i) => (
                <span
                  key={blood.id}
                  className={classes.bloodDot}
                  style={{ background: BLOOD_COLORS[i] }}
                  title={blood.name}
                >
                  {blood.avatar ? <img src={blood.avatar} alt="" /> : blood.name.slice(0, 1).toUpperCase()}
                </span>
              ))}
            </div>
          )}
        </div>

        {/* Challenge artwork. The backend has no image field yet (Challenge.ContainerImage is a
            Docker tag, not a picture), so this falls back to a category-tinted plate carrying the
            category name — which is what the approved mockup showed. When an image field lands,
            only `image` below needs pointing at it. */}
        <div
          className={classes.art}
          style={image ? undefined : { background: `linear-gradient(160deg, ${accent}33, ${accent}0d)` }}
        >
          {image ? (
            <img src={image} alt="" loading="lazy" />
          ) : (
            <span className={classes.artLabel}>{(challenge.category ?? 'CHALLENGE').toUpperCase()}</span>
          )}
        </div>
      </div>

      {showDeadline && <p className={classes.deadline}>Closes {timeLeft(deadline)}</p>}

      <div className={classes.cardFoot}>
        <span className={classes.footStat}>
          <span className={classes.footValue}>{challenge.score}</span>
          <span className={classes.footLabel}>POINTS</span>
        </span>

        <span className={classes.footStat}>
          <span className={classes.footValue}>{challenge.solved.toLocaleString()}</span>
          <span className={classes.footLabel}>SOLVES</span>
        </span>

        <span className={classes.diffGroup}>
          <span className={classes.bars} aria-hidden="true">
            {Array.from({ length: 5 }, (_, i) => (
              <span
                key={i}
                className={classes.bar}
                style={{ background: i < difficulty.bars ? difficulty.color : 'rgb(120 140 190 / 18%)' }}
              />
            ))}
          </span>
          <span className={classes.diffLabel} style={{ color: difficulty.color }}>
            {difficulty.label}
          </span>
        </span>

        <span
          className={classes.statusPill}
          style={{
            color: statusColor,
            borderColor: `color-mix(in srgb, ${statusColor} 32%, transparent)`,
            background: `color-mix(in srgb, ${statusColor} 10%, transparent)`,
          }}
        >
          {status}
          <span className={classes.statusDot} style={{ background: statusColor }} aria-hidden="true" />
        </span>
      </div>
    </button>
  )
}
