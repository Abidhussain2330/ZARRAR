import { Button } from '@mantine/core'
import { mdiPlus } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { NoticeRow } from '@Components/admin/NoticeRow'
import { AdminLayout, openAdminConfirm, showAdminSuccess } from '@Components/admin/shell'
import { EmptyState, ErrorState, LoadingState, Panel } from '@Components/zarrar'
import { showErrorMsg } from '@Utils/Shared'
import { isSponsorPost } from '@Utils/player/posts'
import api, { PostInfoModel } from '@Api'
import classes from '@Styles/admin/AdminNotices.module.css'

/**
 * Landing page for the admin rail's "Notices" entry (`/admin/notices`).
 *
 * Authoring happens at `/admin/notices/:postId` (`new` for a fresh notice), so
 * the whole create/edit/pin/delete flow stays inside the admin console.
 */
const AdminNotices: FC = () => {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [busyId, setBusyId] = useState<string | null>(null)

  const { data: posts, error, isLoading, mutate } = api.info.useInfoGetPosts()

  const { pinned, sponsors, rest } = useMemo(() => {
    const all = posts ?? []
    const pinned = all.filter((p) => p.isPinned)
    const sponsors = all.filter((p) => !p.isPinned && isSponsorPost(p.tags))
    const rest = all.filter((p) => !p.isPinned && !isSponsorPost(p.tags))
    return { pinned, sponsors, rest }
  }, [posts])

  const refresh = (updated?: PostInfoModel[]) => {
    mutate(updated, { revalidate: !updated })
    api.info.mutateInfoGetLatestPosts()
  }

  const onTogglePin = async (post: PostInfoModel) => {
    setBusyId(post.id)
    try {
      // Only `isPinned` in the body — the backend treats that as a
      // pin-only update and leaves title/summary/content/tags untouched.
      await api.edit.editUpdatePost(post.id, { isPinned: !post.isPinned })
      refresh((posts ?? []).map((p) => (p.id === post.id ? { ...p, isPinned: !p.isPinned } : p)))
      showAdminSuccess(post.isPinned ? t('post.notification.unpinned', { defaultValue: 'Notice unpinned' }) : t('post.notification.pinned', { defaultValue: 'Notice pinned' }))
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setBusyId(null)
    }
  }

  const onDelete = (post: PostInfoModel) => {
    openAdminConfirm({
      t,
      title: t('post.button.delete'),
      message: t('post.content.delete', { title: post.title }),
      subject: post.title,
      onConfirm: async () => {
        setBusyId(post.id)
        try {
          await api.edit.editDeletePost(post.id)
          refresh((posts ?? []).filter((p) => p.id !== post.id))
          showAdminSuccess(t('post.notification.deleted', { defaultValue: 'Notice deleted' }))
        } catch (err) {
          showErrorMsg(err, t)
        } finally {
          setBusyId(null)
        }
      },
    })
  }

  const renderGroup = (label: string, group: PostInfoModel[]) =>
    group.length > 0 && (
      <>
        <div className={classes.groupHeading}>{label}</div>
        <div className={classes.list}>
          {group.map((post) => (
            <NoticeRow
              key={post.id}
              post={post}
              disabled={busyId === post.id}
              onEdit={() => navigate(`/admin/notices/${post.id}`)}
              onTogglePin={() => onTogglePin(post)}
              onDelete={() => onDelete(post)}
            />
          ))}
        </div>
      </>
    )

  return (
    <AdminLayout
      title={t('admin.nav.posts', { defaultValue: 'Notices' })}
      description={t('admin.content.notices.description', {
        defaultValue: 'Platform-wide posts shown on the player home page — separate from the player feed.',
      })}
      actions={
        <Button leftSection={<Icon path={mdiPlus} size={0.8} />} onClick={() => navigate('/admin/notices/new')}>
          {t('post.button.new')}
        </Button>
      }
    >
      <Panel>
        {isLoading && <LoadingState variant="card-grid" count={4} />}
        {error && <ErrorState message={t('admin.error.notices.load', { defaultValue: "Couldn't load notices." })} onRetry={() => mutate()} />}
        {!isLoading && !error && (posts?.length ?? 0) === 0 && (
          <EmptyState
            title={t('admin.empty.notices.title', { defaultValue: 'No notices yet' })}
            description={t('admin.empty.notices.description', {
              defaultValue: 'Notices appear on the player home page. Create the first one to get started.',
            })}
            action={
              <Button leftSection={<Icon path={mdiPlus} size={0.8} />} onClick={() => navigate('/admin/notices/new')}>
                {t('post.button.new')}
              </Button>
            }
          />
        )}
        {!isLoading && !error && (posts?.length ?? 0) > 0 && (
          <>
            {renderGroup(t('admin.section.notices.pinned', { defaultValue: 'Pinned' }), pinned)}
            {renderGroup(t('admin.section.notices.sponsors', { defaultValue: 'Sponsors' }), sponsors)}
            {renderGroup(t('admin.section.notices.all', { defaultValue: 'All notices' }), rest)}
          </>
        )}
      </Panel>
    </AdminLayout>
  )
}

export default AdminNotices
