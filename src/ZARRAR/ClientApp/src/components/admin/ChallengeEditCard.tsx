import { ActionIcon, Switch, Tooltip } from '@mantine/core'
import { mdiClockOutline, mdiDatabaseEditOutline, mdiPackageVariantClosed, mdiPaperclip, mdiPuzzleEditOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { CSSProperties, Dispatch, FC, SetStateAction, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router'
import { CategoryBadge, resolveCategoryToken } from '@Components/zarrar'
import { useChallengeCategoryLabelMap, useChallengeTypeLabelMap } from '@Utils/Shared'
import { ChallengeCategory, ChallengeInfoModel, ChallengeType } from '@Api'
import classes from '@Styles/admin/AdminChallengeCard.module.css'

interface ChallengeEditCardProps {
  challenge: ChallengeInfoModel
  onToggle: (challenge: ChallengeInfoModel, setDisabled: Dispatch<SetStateAction<boolean>>) => void
}

const CONTAINER_TYPES: (ChallengeType | undefined)[] = [ChallengeType.StaticContainer, ChallengeType.DynamicContainer]

export const ChallengeEditCard: FC<ChallengeEditCardProps> = ({ challenge, onToggle }) => {
  const categoryLabelMap = useChallengeCategoryLabelMap()
  const typeLabelMap = useChallengeTypeLabelMap()
  const categoryData = categoryLabelMap.get(challenge.category as ChallengeCategory)
  const typeData = challenge.type ? typeLabelMap.get(challenge.type) : undefined
  const { id } = useParams()
  const { t } = useTranslation()

  const [disabled, setDisabled] = useState(false)

  const min = challenge.minScore ?? 0
  const cur = challenge.score ?? challenge.originalScore ?? 0
  const tot = challenge.originalScore ?? cur ?? 1
  const floorRate = tot > 0 ? Math.min(100, (min / tot) * 100) : 0
  const curRate = tot > 0 ? Math.min(100, (cur / tot) * 100) : 0

  const accentStyle = {
    '--zarrar-badge-color': `var(--zarrar-cat-${resolveCategoryToken(challenge.category ?? 'misc')})`,
  } as CSSProperties

  const isContainer = CONTAINER_TYPES.includes(challenge.type)

  return (
    <div className={classes.card} data-off={!challenge.isEnabled} style={accentStyle}>
      <div className={classes.head}>
        <div className={classes.headLeft}>
          <CategoryBadge
            category={challenge.category ?? 'misc'}
            size="sm"
            icon={categoryData?.icon ? <Icon path={categoryData.icon} size={0.5} /> : undefined}
          />
          {typeData && (
            <Tooltip label={typeData.name} withArrow>
              <span className={classes.typeIcon}>
                <Icon path={isContainer ? mdiPackageVariantClosed : mdiPaperclip} size={0.55} />
              </span>
            </Tooltip>
          )}
        </div>
        <Switch
          disabled={disabled}
          checked={challenge.isEnabled}
          aria-label={t('admin.action.challenge_toggle_named', {
            defaultValue: '{{action}} {{name}}',
            action: challenge.isEnabled
              ? t('admin.button.challenges.disable')
              : t('admin.button.challenges.enable'),
            name: challenge.title,
          })}
          onChange={() => onToggle(challenge, setDisabled)}
        />
      </div>

      <p className={classes.title} title={challenge.title}>
        {challenge.title}
      </p>

      {challenge.deadlineUtc && (
        <p className={classes.deadline}>
          <Icon path={mdiClockOutline} size={0.5} />
          {dayjs(challenge.deadlineUtc).format('MMM D, HH:mm')}
        </p>
      )}

      <div className={classes.scoreRow}>
        <div className={classes.bar}>
          <div className={classes.barFloor} style={{ width: `${floorRate}%` }} />
          <div className={classes.barCurrent} style={{ width: `${Math.max(0, curRate - floorRate)}%` }} />
        </div>
        <span className={classes.score}>
          <b>{cur}</b> / {tot} {t('challenge.label.points', { defaultValue: 'pts' })}
        </span>
      </div>

      <div className={classes.footer}>
        <span className={classes.status}>
          {challenge.isEnabled ? t('admin.label.enabled', { defaultValue: 'Enabled' }) : t('admin.label.disabled', { defaultValue: 'Disabled' })}
        </span>
        <div className={classes.actions}>
          <Tooltip label={t('admin.button.challenges.edit')} withArrow>
            <ActionIcon variant="subtle" color="gray" component={Link} to={`/admin/games/${id}/challenges/${challenge.id}`}>
              <Icon path={mdiPuzzleEditOutline} size={0.8} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label={t('admin.button.challenges.edit_more')} withArrow>
            <ActionIcon variant="subtle" color="gray" component={Link} to={`/admin/games/${id}/challenges/${challenge.id}/flags`}>
              <Icon path={mdiDatabaseEditOutline} size={0.8} />
            </ActionIcon>
          </Tooltip>
        </div>
      </div>
    </div>
  )
}
