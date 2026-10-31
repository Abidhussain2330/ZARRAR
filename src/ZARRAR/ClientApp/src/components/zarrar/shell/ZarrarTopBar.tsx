import { AppShell, Avatar, Menu, MenuDivider, Popover, Tooltip, useMantineColorScheme } from '@mantine/core'
import {
  mdiAccountCircleOutline,
  mdiCached,
  mdiFlashOutline,
  mdiLightningBolt,
  mdiLogin,
  mdiLogout,
  mdiPalette,
  mdiTransitConnectionVariant,
  mdiTuneVariant,
  mdiTranslate,
  mdiWeatherNight,
  mdiWeatherSunny,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation } from 'react-router'
import { WsrxManager } from '@Components/WsrxManager'
import { useAdminNavigation } from '@Components/admin/shell/AdminNavigation'
import { clearLocalCache } from '@Utils/Cache'
import { LanguageMap, SupportedLanguages, useLanguage } from '@Utils/I18n'
import { useZarrarPreferences } from '@Hooks/zarrar/useZarrarPreferences'
import { useConfig } from '@Hooks/useConfig'
import { useLogOut, useUser } from '@Hooks/useUser'
import api, { ContainerPortMappingType } from '@Api'
import classes from '@Styles/zarrar/ZarrarTopBar.module.css'

/** UTC wall clock used by the event shell. */
const ZarrarClock: FC = () => {
  const { t } = useTranslation()
  const [now, setNow] = useState(() => new Date())

  useEffect(() => {
    const id = setInterval(() => setNow(new Date()), 1000)
    return () => clearInterval(id)
  }, [])

  const iso = now.toISOString()

  return (
    <time
      className={classes.clock}
      dateTime={iso}
      title={t('common.shell.clock.label', { defaultValue: 'Coordinated Universal Time' })}
    >
      <span>{iso.slice(11, 16)}</span>
      <span className={classes.clockSeconds}>{iso.slice(16, 19)}</span>
      <span className={classes.clockZone}>UTC</span>
    </time>
  )
}

export interface ZarrarTopBarProps {
  openColorModal: () => void
  compact: boolean
}

/**
 * Admin console top bar: section breadcrumb, live platform vitals, clock and
 * the account menu. Section navigation itself lives in `AdminLayout` (rail on
 * desktop, "Sections" drawer on mobile).
 */
export const ZarrarTopBar: FC<ZarrarTopBarProps> = ({ openColorModal, compact }) => {
  const { t } = useTranslation()
  const location = useLocation()
  const { groups: adminGroups, activeKey: adminActiveKey } = useAdminNavigation()
  const { config } = useConfig()
  const { user, error: userError } = useUser()
  const { lowEnergy, setEnergy } = useZarrarPreferences()
  const { setLanguage, supportedLanguages } = useLanguage()
  const logout = useLogOut()

  // Anonymous-safe aggregate counters for the vitals strip.
  const { data: overview } = api.dashboard.useDashboardOverview({
    refreshInterval: 15_000,
    revalidateOnFocus: true,
    shouldRetryOnError: false,
  })

  const loggedIn = user && !userError
  const userMenuLabel = t('common.shell.user.menu', { defaultValue: 'Account menu' })
  const energyLabel = lowEnergy
    ? t('common.shell.energy.disable', { defaultValue: 'Disable low-energy mode' })
    : t('common.shell.energy.enable', { defaultValue: 'Enable low-energy mode' })

  const activeSection = adminGroups.flatMap((g) => g.items).find((i) => i.key === adminActiveKey)
  const sectionLabel = activeSection
    ? t(activeSection.label, { defaultValue: activeSection.fallback })
    : t('admin.nav.root', { defaultValue: 'Administration' })

  return (
    <AppShell.Header className={`${classes.header} zarrar-shell-topbar`}>
      <div className={classes.inner}>
        <div className={classes.left}>
          <div className={classes.consoleCrumb}>
            <span>{t('admin.nav.root', { defaultValue: 'Admin' })}</span>
            <span className={classes.consoleCrumbSep}>/</span>
            <strong>{sectionLabel}</strong>
          </div>

          {!compact && (
            <div className={classes.vitals} role="status" aria-label={t('admin.vitals.aria', { defaultValue: 'Platform vitals' })}>
              <span className={classes.vital}>
                <span className={`${classes.vitalDot} ${overview?.activeGameCount ? classes.vitalDotLive : classes.vitalDotIdle}`} />
                {t('admin.label.active_games', { defaultValue: 'Active' })} <strong>{overview?.activeGameCount ?? '—'}</strong>
              </span>
              <span className={classes.vital}>
                <span className={`${classes.vitalDot} ${classes.vitalDotIdle}`} />
                {t('admin.label.upcoming_games', { defaultValue: 'Upcoming' })} <strong>{overview?.upcomingGameCount ?? '—'}</strong>
              </span>
              <span className={classes.vital}>
                <span className={`${classes.vitalDot} ${overview?.recentSolveCount ? classes.vitalDotLive : classes.vitalDotIdle}`} />
                {t('admin.label.solves_24h', { defaultValue: 'Solves/24h' })} <strong>{overview?.recentSolveCount ?? '—'}</strong>
              </span>
            </div>
          )}
        </div>

        <div className={classes.right}>
          <ZarrarClock />

          <Tooltip label={energyLabel} position="bottom" withArrow>
            <button
              type="button"
              className={`${classes.iconButton} zarrar-shell-icon-btn`}
              data-on={lowEnergy || undefined}
              onClick={() => setEnergy(lowEnergy ? 'normal' : 'low')}
              aria-pressed={lowEnergy}
              aria-label={energyLabel}
            >
              <Icon path={lowEnergy ? mdiFlashOutline : mdiLightningBolt} size={0.9} />
            </button>
          </Tooltip>

          {!compact && config.portMapping === ContainerPortMappingType.PlatformProxy && (
            <Popover position="bottom-end" offset={12} width={320}>
              <Popover.Target>
                <button type="button" className={`${classes.iconButton} zarrar-shell-icon-btn`} aria-label="WebSocket Reflector X">
                  <Icon path={mdiTransitConnectionVariant} size={0.9} />
                </button>
              </Popover.Target>
              <Popover.Dropdown>
                <WsrxManager />
              </Popover.Dropdown>
            </Popover>
          )}

          {!compact && <ZarrarLanguageMenu setLanguage={setLanguage} supportedLanguages={supportedLanguages} />}
          {!compact && <ZarrarColorSchemeButton />}

          <Menu position="bottom-end" offset={12} width={220}>
            <Menu.Target>
              <button type="button" className={`${classes.identity} zarrar-shell-user-menu`} aria-label={userMenuLabel}>
                <Avatar alt="" src={user?.avatar ?? undefined} radius="sm" size={30} className={classes.avatar}>
                  {user?.userName?.slice(0, 1) ?? <Icon path={mdiAccountCircleOutline} size={0.9} />}
                </Avatar>
                {loggedIn && (
                  <span className={classes.identityText}>
                    <span className={classes.identityName}>{user.userName}</span>
                    {user.role && <span className={classes.identityRole}>{user.role}</span>}
                  </span>
                )}
              </button>
            </Menu.Target>
            <Menu.Dropdown>
              {loggedIn && (
                <>
                  <Menu.Label>{user.userName}</Menu.Label>
                  <Menu.Item component={Link} to="/account/profile" leftSection={<Icon path={mdiAccountCircleOutline} size={0.9} />}>
                    {t('common.tab.account.profile')}
                  </Menu.Item>
                  <Menu.Item component={Link} to="/account/settings" leftSection={<Icon path={mdiTuneVariant} size={0.9} />}>
                    Preferences &amp; API access
                  </Menu.Item>
                </>
              )}
              <Menu.Item onClick={clearLocalCache} leftSection={<Icon path={mdiCached} size={0.9} />}>
                {t('common.tab.account.clean_cache')}
              </Menu.Item>
              <Menu.Item onClick={openColorModal} leftSection={<Icon path={mdiPalette} size={0.9} />}>
                {t('common.content.color.title')}
              </Menu.Item>
              <MenuDivider />
              {loggedIn ? (
                <Menu.Item color="red" onClick={logout} leftSection={<Icon path={mdiLogout} size={0.9} />}>
                  {t('common.tab.account.logout')}
                </Menu.Item>
              ) : (
                <Menu.Item component={Link} to={`/account/login?from=${location.pathname}`} leftSection={<Icon path={mdiLogin} size={0.9} />}>
                  {t('common.tab.account.login')}
                </Menu.Item>
              )}
            </Menu.Dropdown>
          </Menu>
        </div>
      </div>
    </AppShell.Header>
  )
}

export interface ZarrarLanguageMenuProps {
  setLanguage: (lang: SupportedLanguages) => void
  supportedLanguages: SupportedLanguages[]
}

export const ZarrarLanguageMenu: FC<ZarrarLanguageMenuProps> = ({ setLanguage, supportedLanguages }) => (
  <Menu position="bottom-end" offset={12} width={170}>
    <Menu.Target>
      <button type="button" className={`${classes.iconButton} zarrar-shell-icon-btn`} aria-label="Language">
        <Icon path={mdiTranslate} size={0.9} />
      </button>
    </Menu.Target>
    <Menu.Dropdown>
      {supportedLanguages.map((lang) => (
        <Menu.Item key={lang} fw={500} onClick={() => setLanguage(lang)}>
          {LanguageMap[lang] ?? lang}
        </Menu.Item>
      ))}
    </Menu.Dropdown>
  </Menu>
)

export const ZarrarColorSchemeButton: FC = () => {
  const { t } = useTranslation()
  const { colorScheme, toggleColorScheme } = useMantineColorScheme()

  const label = t('common.tab.theme.switch_to', {
    theme: colorScheme === 'dark' ? t('common.tab.theme.light') : t('common.tab.theme.dark'),
  })

  return (
    <Tooltip label={label} position="bottom" withArrow>
      <button type="button" className={`${classes.iconButton} zarrar-shell-icon-btn`} onClick={() => toggleColorScheme()} aria-label={label}>
        <Icon path={colorScheme === 'dark' ? mdiWeatherSunny : mdiWeatherNight} size={0.9} />
      </button>
    </Tooltip>
  )
}
