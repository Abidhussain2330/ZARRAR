import { Icon } from '@mdi/react'
import { FC } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import classes from '@Styles/admin/AdminTabs.module.css'

export interface AdminTab {
  key: string
  label: string
  icon: string
  to: string
}

export interface AdminTabStripProps {
  tabs: AdminTab[]
  activeKey?: string
  /** Accessible name — "Game sections", "Monitor views". */
  label?: string
}

/**
 * Horizontal, sideways-scrolling sub-navigation.
 *
 * Rendered as links rather than buttons so a tab can be opened in a new tab,
 * bookmarked, and reached by a screen reader as the navigation it actually is.
 */
export const AdminTabStrip: FC<AdminTabStripProps> = ({ tabs, activeKey, label }) => {
  const { t } = useTranslation()

  return (
    <nav aria-label={label ?? t('admin.nav.subsections', { defaultValue: 'Sub-sections' })}>
      <div className={classes.strip}>
        {tabs.map((tab) => {
          const active = tab.key === activeKey

          return (
            <Link
              key={tab.key}
              to={tab.to}
              className={classes.tab}
              data-active={active || undefined}
              aria-current={active ? 'page' : undefined}
            >
              <span className={classes.tabIcon} aria-hidden>
                <Icon path={tab.icon} size={0.75} />
              </span>
              {tab.label}
            </Link>
          )
        })}
      </div>
    </nav>
  )
}
