import { ActionIcon, Tooltip } from '@mantine/core'
import { mdiDeleteOutline, mdiPencilOutline, mdiPinOffOutline, mdiPinOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC } from 'react'
import { useTranslation } from 'react-i18next'
import { AdminCellActions } from '@Components/admin/shell'
import { PostInfoModel } from '@Api'
import { isSponsorPost } from '@Utils/player/posts'
import classes from '@Styles/admin/AdminNotices.module.css'

export interface NoticeRowProps {
  post: PostInfoModel
  disabled?: boolean
  onEdit: () => void
  onTogglePin: () => void
  onDelete: () => void
}

/**
 * One row in the admin Notices list.
 *
 * Sponsor styling is the same tag convention the player feed uses
 * (`isSponsorPost`, `@Utils/player/posts`) so the two can't drift apart on
 * what counts as a sponsor post.
 */
export const NoticeRow: FC<NoticeRowProps> = ({ post, disabled, onEdit, onTogglePin, onDelete }) => {
  const { t } = useTranslation()
  const sponsor = isSponsorPost(post.tags)

  return (
    <div className={`${classes.row} ${post.isPinned ? classes.pinned : ''} ${sponsor ? classes.sponsor : ''}`}>
      <Tooltip label={post.isPinned ? t('post.button.unpin', { defaultValue: 'Unpin' }) : t('post.button.pin', { defaultValue: 'Pin' })} withArrow>
        <ActionIcon variant="subtle" color={post.isPinned ? 'red' : 'gray'} disabled={disabled} aria-label="Toggle pinned" onClick={onTogglePin}>
          <Icon path={post.isPinned ? mdiPinOutline : mdiPinOffOutline} size={0.85} />
        </ActionIcon>
      </Tooltip>

      <div className={classes.rowBody}>
        <p className={classes.rowTitle}>{post.title}</p>
        <div className={classes.rowMeta}>
          <span>{new Date(post.time).toLocaleString()}</span>
          {(post.tags ?? []).map((tag) => (
            <span key={tag} className={`${classes.tag} ${sponsor && isSponsorPost([tag]) ? classes.sponsor : ''}`}>
              {tag}
            </span>
          ))}
        </div>
        <p className={classes.rowSummary}>{post.summary}</p>
      </div>

      <AdminCellActions>
        <Tooltip label={t('post.button.edit')} withArrow>
          <ActionIcon variant="subtle" color="blue" disabled={disabled} aria-label={t('post.button.edit')} onClick={onEdit}>
            <Icon path={mdiPencilOutline} size={0.85} />
          </ActionIcon>
        </Tooltip>
        <Tooltip label={t('post.button.delete')} withArrow>
          <ActionIcon variant="subtle" color="red" disabled={disabled} aria-label={t('post.button.delete')} onClick={onDelete}>
            <Icon path={mdiDeleteOutline} size={0.85} />
          </ActionIcon>
        </Tooltip>
      </AdminCellActions>
    </div>
  )
}
