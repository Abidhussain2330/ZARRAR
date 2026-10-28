import { Drawer, LoadingOverlay } from '@mantine/core'
import { mdiMenu } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, Fragment, PropsWithChildren, ReactNode, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation } from 'react-router'
import { AdminSectionNav } from '@Components/admin/shell/AdminSectionNav'
import { SocAlertWatcher } from '@Components/admin/SocAlertWatcher'
import { PermissionDenied } from '@Components/buraq'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { DEFAULT_LOADING_OVERLAY } from '@Utils/Shared'
import { usePageTitle } from '@Hooks/usePageTitle'
import { Role } from '@Api'
import classes from '@Styles/admin/AdminShell.module.css'

export interface AdminCrumb {
  label: string
  /** Omit on the final crumb — the current page is not a link to itself. */
  to?: string
}

export interface AdminLayoutProps extends PropsWithChildren {
  /** Page `h1`. Also the browser tab title unless `documentTitle` overrides it. */
  title: string
  documentTitle?: string
  description?: ReactNode
  /** Ancestors only; the current page is appended from `title` automatically. */
  breadcrumbs?: AdminCrumb[]
  /** Primary page actions — create, import, save, delete. */
  actions?: ReactNode
  /**
   * Backend-enforced minimum role for this screen. Defaults to Admin; the
   * monitor screens pass Monitor. This mirrors the controller attribute — it
   * does not replace it.
   */
  requiredRole?: Role
  /** Blocks the body while the page's first payload is in flight. */
  isLoading?: boolean
  /** Rendered between the header and the body: sub-tabs, an outage notice. */
  subnav?: ReactNode
}

/**
 * The Admin Command Center frame.
 *
 * Replaces the old `AdminPage` + `WithAdminTab` pair. Two differences matter:
 *
 *  - `minWidth={0}`. The previous shell passed `minWidth={1080}`, so every
 *    admin screen showed a "wider screen required" placeholder on a tablet or
 *    phone. Administration now works at every breakpoint.
 *  - Sections live in a rail (or drawer) rather than a single row of tabs,
 *    which stopped scaling once there were more than six of them.
 */
export const AdminLayout: FC<AdminLayoutProps> = ({
  title,
  documentTitle,
  description,
  breadcrumbs,
  actions,
  requiredRole = Role.Admin,
  isLoading = false,
  subnav,
  children,
}) => {
  const { t } = useTranslation()
  const location = useLocation()
  const [navOpened, setNavOpened] = useState(false)

  usePageTitle(documentTitle ?? title)

  // A drawer left open across a route change would cover the page it opened.
  useEffect(() => setNavOpened(false), [location.pathname])

  const rootCrumb: AdminCrumb = {
    label: t('admin.nav.root', { defaultValue: 'Administration' }),
    to: '/admin',
  }
  const crumbs = [rootCrumb, ...(breadcrumbs ?? [])]

  return (
    <WithNavBar width="100%" minWidth={0}>
      <WithRole
        requiredRole={requiredRole}
        fallback={
          <div className={classes.root}>
            <div className={classes.column}>
              <PermissionDenied
                required={t('admin.state.required_role', {
                  defaultValue: 'Required role: {{role}}',
                  role: requiredRole,
                })}
                action={
                  <Link to="/games" className={classes.crumbLink}>
                    {t('common.tab.home')}
                  </Link>
                }
              />
            </div>
          </div>
        }
      >
        <div className={classes.root}>
          {/* High/Critical SOC alerts reach the admin on every admin page (S1). */}
          <SocAlertWatcher />
          <AdminSectionNav />

          <Drawer
            opened={navOpened}
            onClose={() => setNavOpened(false)}
            title={t('admin.nav.root', { defaultValue: 'Administration' })}
            size="17rem"
            padding="md"
            zIndex={250}
          >
            <AdminSectionNav inDrawer onNavigate={() => setNavOpened(false)} />
          </Drawer>

          <div className={classes.column}>
            <header className={classes.header}>
              <div className={classes.headerTop}>
                <button
                  type="button"
                  className={classes.railTrigger}
                  onClick={() => setNavOpened(true)}
                  aria-haspopup="dialog"
                  aria-expanded={navOpened}
                >
                  <Icon path={mdiMenu} size={0.7} aria-hidden />
                  {t('admin.nav.sections', { defaultValue: 'Sections' })}
                </button>

                <nav aria-label={t('admin.nav.breadcrumbs', { defaultValue: 'Breadcrumb' })}>
                  <ol className={classes.crumbs}>
                    {crumbs.map((crumb, index) => (
                      <Fragment key={`${crumb.label}-${index}`}>
                        <li className={classes.crumb}>
                          {crumb.to ? (
                            <Link to={crumb.to} className={classes.crumbLink}>
                              {crumb.label}
                            </Link>
                          ) : (
                            <span className={classes.crumbCurrent}>{crumb.label}</span>
                          )}
                        </li>
                        <li className={classes.crumbSep} aria-hidden>
                          /
                        </li>
                      </Fragment>
                    ))}
                    <li className={classes.crumb}>
                      <span className={classes.crumbCurrent} aria-current="page">
                        {title}
                      </span>
                    </li>
                  </ol>
                </nav>
              </div>

              <div className={classes.headerMain}>
                <div className={classes.headings}>
                  <h1 className={classes.title}>{title}</h1>
                  {description && <p className={classes.description}>{description}</p>}
                </div>
                {actions && <div className={classes.actions}>{actions}</div>}
              </div>
            </header>

            {subnav}

            <div className={classes.body} aria-busy={isLoading || undefined}>
              <LoadingOverlay visible={isLoading} overlayProps={DEFAULT_LOADING_OVERLAY} />
              {children}
            </div>
          </div>
        </div>
      </WithRole>
    </WithNavBar>
  )
}
