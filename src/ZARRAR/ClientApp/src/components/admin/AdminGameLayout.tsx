import {
  mdiAccountGroupOutline,
  mdiBroadcast,
  mdiBullhornOutline,
  mdiChartLine,
  mdiFileDocumentCheckOutline,
  mdiFlagOutline,
  mdiTagOutline,
  mdiTextBoxOutline,
} from '@mdi/js'
import { FC, PropsWithChildren, ReactNode, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { AdminLayout } from '@Components/admin/shell/AdminLayout'
import { AdminTab, AdminTabStrip } from '@Components/admin/shell/AdminTabStrip'
import { useAdminGame } from '@Hooks/useGame'

export interface AdminGameLayoutProps extends PropsWithChildren {
  /** The active editor tab. Also names the page for the breadcrumb trail. */
  tab: 'info' | 'notices' | 'challenges' | 'divisions' | 'review' | 'writeups'
  description?: ReactNode
  actions?: ReactNode
  isLoading?: boolean
  /** Overrides the heading — the challenge editor titles itself by challenge. */
  title?: string
  /** Rendered under the tab strip, above the body. */
  subnav?: ReactNode
}

/**
 * The frame every game editor screen sits in.
 *
 * Replaces `WithGameEditTab`. The game's own title becomes the page heading and
 * a breadcrumb entry, so an operator deep in a challenge's flag editor can
 * still see — and click back to — which event they are editing. That context
 * was previously carried only by a "Back" button.
 */
export const AdminGameLayout: FC<AdminGameLayoutProps> = ({
  tab,
  title,
  description,
  actions,
  isLoading,
  subnav,
  children,
}) => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1', 10)
  const { game } = useAdminGame(numId)
  const { t } = useTranslation()

  const tabs: AdminTab[] = useMemo(
    () => [
      { key: 'info', icon: mdiTextBoxOutline, label: t('admin.tab.games.info'), to: `/admin/games/${id}/info` },
      {
        key: 'challenges',
        icon: mdiFlagOutline,
        label: t('admin.tab.games.challenges'),
        to: `/admin/games/${id}/challenges`,
      },
      {
        key: 'notices',
        icon: mdiBullhornOutline,
        label: t('admin.tab.games.notices'),
        to: `/admin/games/${id}/notices`,
      },
      {
        key: 'divisions',
        icon: mdiTagOutline,
        label: t('admin.tab.games.divisions'),
        to: `/admin/games/${id}/divisions`,
      },
      {
        key: 'review',
        icon: mdiAccountGroupOutline,
        label: t('admin.tab.games.review'),
        to: `/admin/games/${id}/review`,
      },
      {
        key: 'writeups',
        icon: mdiFileDocumentCheckOutline,
        label: t('admin.tab.games.writeups'),
        to: `/admin/games/${id}/writeups`,
      },
      {
        key: 'scoreboard',
        icon: mdiChartLine,
        label: t('game.tab.scoreboard'),
        to: `/games/${id}/scoreboard`,
      },
      {
        // ZARRAR: the live showdown section — the monitor's stage, synced, plus match controls.
        key: 'live',
        icon: mdiBroadcast,
        label: 'LIVE',
        to: `/admin/live/${id}`,
      },
    ],
    [id, t]
  )

  const tabLabel = tabs.find((item) => item.key === tab)?.label ?? tab
  const gameTitle = game?.title ?? t('admin.placeholder.games.title', { defaultValue: 'Event' })

  /*
   * The challenge editor lives two levels below the game: the challenge takes
   * the heading and the game becomes a breadcrumb link back to its own editor.
   * Everywhere else the game title is the heading, and the active tab strip —
   * not a breadcrumb entry — says which section is open.
   */
  const isNested = Boolean(title)

  return (
    <AdminLayout
      title={title ?? gameTitle}
      documentTitle={`${tabLabel} · ${gameTitle}`}
      description={description}
      actions={actions}
      isLoading={isLoading}
      breadcrumbs={
        isNested
          ? [
              { label: t('admin.tab.games.index'), to: '/admin/games' },
              { label: gameTitle, to: `/admin/games/${id}/info` },
              { label: tabLabel, to: `/admin/games/${id}/${tab}` },
            ]
          : [{ label: t('admin.tab.games.index'), to: '/admin/games' }]
      }
      subnav={
        <>
          <AdminTabStrip
            tabs={tabs}
            activeKey={tab}
            label={t('admin.nav.game_sections', { defaultValue: 'Event sections' })}
          />
          {subnav}
        </>
      }
    >
      {children}
    </AdminLayout>
  )
}
