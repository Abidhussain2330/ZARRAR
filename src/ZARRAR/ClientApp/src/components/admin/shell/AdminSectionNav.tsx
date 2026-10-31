import { Icon } from '@mdi/react'
import { FC } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { useAdminNavigation } from '@Components/admin/shell/AdminNavigation'
import classes from '@Styles/admin/AdminShell.module.css'

export interface AdminSectionNavProps {
  /** Rendered inside the mobile drawer rather than as the sticky rail. */
  inDrawer?: boolean
  /** Closes the drawer once a destination is chosen. */
  onNavigate?: () => void
}

/**
 * The Admin Command Center section list.
 *
 * Two presentations from one source of truth, so they can never list
 * different sections: an icon-only rail with hover tooltips at ≥75em (a
 * console nav, not a labelled sidebar — deliberate, per the ops-console
 * redesign), and a labelled list inside the mobile/tablet drawer, where
 * icon-only navigation isn't usable without hover.
 */
export const AdminSectionNav: FC<AdminSectionNavProps> = ({ inDrawer = false, onNavigate }) => {
  const { t } = useTranslation()
  const { groups, activeKey } = useAdminNavigation()

  if (inDrawer) {
    return (
      <nav
        className={`${classes.rail} ${classes.railInDrawer}`}
        aria-label={t('admin.nav.aria', { defaultValue: 'Administration sections' })}
      >
        {groups.map((group) => (
          <div key={group.key} className={classes.group}>
            <h2 className={classes.railHeading} id={`admin-nav-${group.key}`}>
              {t(group.label, { defaultValue: group.fallback })}
            </h2>
            <ul className={classes.groupList} aria-labelledby={`admin-nav-${group.key}`}>
              {group.items.map((item) => {
                const active = item.key === activeKey
                return (
                  <li key={item.key}>
                    <Link
                      to={item.link}
                      className={classes.navLink}
                      data-active={active || undefined}
                      aria-current={active ? 'page' : undefined}
                      onClick={onNavigate}
                    >
                      <span className={classes.navIcon} aria-hidden>
                        <Icon path={item.icon} size={0.8} />
                      </span>
                      <span className={classes.navLabel}>{t(item.label, { defaultValue: item.fallback })}</span>
                    </Link>
                  </li>
                )
              })}
            </ul>
          </div>
        ))}
      </nav>
    )
  }

  return (
    <nav className={classes.iconRail} aria-label={t('admin.nav.aria', { defaultValue: 'Administration sections' })}>
      {groups.map((group) => (
        <div key={group.key} className={classes.iconGroup} role="group" aria-label={t(group.label, { defaultValue: group.fallback })}>
          {group.items.map((item) => {
            const active = item.key === activeKey
            const label = t(item.label, { defaultValue: item.fallback })
            return (
              <Link
                key={item.key}
                to={item.link}
                className={classes.iconLink}
                data-active={active || undefined}
                aria-current={active ? 'page' : undefined}
                aria-label={label}
              >
                <Icon path={item.icon} size={0.78} aria-hidden />
                <span className={classes.iconTip} aria-hidden>
                  {label}
                </span>
              </Link>
            )
          })}
        </div>
      ))}
    </nav>
  )
}
