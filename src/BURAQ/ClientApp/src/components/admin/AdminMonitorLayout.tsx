import { Button } from '@mantine/core'
import { mdiExclamationThick, mdiFlag, mdiLightningBolt, mdiPackageVariant, mdiTableArrowDown } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, PropsWithChildren, ReactNode, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { AdminLayout } from '@Components/admin/shell/AdminLayout'
import { AdminTab, AdminTabStrip } from '@Components/admin/shell/AdminTabStrip'
import { downloadBlob } from '@Utils/ApiHelper'
import { useGame } from '@Hooks/useGame'
import api, { Role } from '@Api'

export interface AdminMonitorLayoutProps extends PropsWithChildren {
  tab: 'events' | 'submissions' | 'cheatinfo' | 'traffic'
  description?: ReactNode
  actions?: ReactNode
  isLoading?: boolean
}

/**
 * Frame for the four live game-monitor screens.
 *
 * These routes live under `/games/:id/monitor` rather than `/admin` — the URLs
 * are unchanged, because monitor links are shared during an event and breaking
 * them mid-competition is not worth the tidier path. They are administrative
 * surfaces all the same, so they render in the Command Center chrome and the
 * "Game monitor" rail entry stays lit while one is open.
 *
 * `requiredRole` is Monitor, mirroring `[RequireMonitor]` on the endpoints
 * behind them: a Monitor can watch an event without being able to administer
 * the platform. The backend remains the enforcement point.
 */
export const AdminMonitorLayout: FC<AdminMonitorLayoutProps> = ({
  tab,
  description,
  actions,
  isLoading,
  children,
}) => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1', 10)
  const { game } = useGame(numId)
  const { t } = useTranslation()
  const [downloading, setDownloading] = useState(false)

  const tabs: AdminTab[] = useMemo(
    () => [
      { key: 'events', icon: mdiLightningBolt, label: t('game.tab.monitor.events'), to: `/games/${id}/monitor/events` },
      {
        key: 'submissions',
        icon: mdiFlag,
        label: t('game.tab.monitor.submissions'),
        to: `/games/${id}/monitor/submissions`,
      },
      {
        key: 'cheatinfo',
        icon: mdiExclamationThick,
        label: t('game.tab.monitor.cheatinfo'),
        to: `/games/${id}/monitor/cheatinfo`,
      },
      {
        key: 'traffic',
        icon: mdiPackageVariant,
        label: t('game.tab.monitor.traffic'),
        to: `/games/${id}/monitor/traffic`,
      },
    ],
    [id, t]
  )

  const tabLabel = tabs.find((item) => item.key === tab)?.label ?? tab
  const gameTitle = game?.title ?? t('admin.placeholder.games.title', { defaultValue: 'Event' })

  const onDownloadScoreboard = () =>
    downloadBlob(
      api.game.gameScoreboardSheet(numId, { format: 'blob' }),
      setDownloading,
      t,
      `Scoreboard_${numId}_${Date.now()}.xlsx`
    )

  return (
    <AdminLayout
      requiredRole={Role.Monitor}
      title={tabLabel}
      documentTitle={`${tabLabel} · ${gameTitle}`}
      description={description}
      isLoading={isLoading}
      breadcrumbs={[
        { label: t('admin.nav.monitor', { defaultValue: 'Game monitor' }) },
        { label: gameTitle, to: `/games/${id}` },
      ]}
      actions={
        <>
          {actions}
          <Button
            variant="default"
            disabled={downloading}
            leftSection={<Icon path={mdiTableArrowDown} size={0.8} />}
            onClick={onDownloadScoreboard}
          >
            {t('game.button.download.scoreboard')}
          </Button>
        </>
      }
      subnav={
        <AdminTabStrip
          tabs={tabs}
          activeKey={tab}
          label={t('admin.nav.monitor_sections', { defaultValue: 'Monitor views' })}
        />
      }
    >
      {children}
    </AdminLayout>
  )
}
