import { FC, ReactNode, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation, useNavigate } from 'react-router'
import { RequireRole } from '@Components/WithRole'
import { CyberBackdrop } from '@Components/player/CyberBackdrop'
import { showErrorMsg } from '@Utils/Shared'
import { usePublisher } from '@Utils/zarrar/live'
import { useUser } from '@Hooks/useUser'
import api, { Role } from '@Api'
import classes from '@Styles/player/Shell.module.css'

interface NavItem {
  to: string
  label: string
  /** Marks the item active for the route itself and anything nested under it. */
  match: (pathname: string) => boolean
}

/**
 * Two groups, rendered with a divider between them.
 *
 * Primary is the player journey, in order: land in POSTS after login (sponsors and
 * announcements) → TEAMS to create a team and hand out invite codes → EVENTS to read the
 * timeline and join one → ABOUT for platform background. Teams precedes Events deliberately:
 * you need a team before joining an event is possible.
 *
 * Account sits in its own group because it isn't part of that journey — separating it keeps
 * the six items from reading as one undifferentiated row. These also remain in the avatar
 * menu, which is where people habitually look for them.
 */
const NAV_PRIMARY: NavItem[] = [
  { to: '/dashboard', label: 'HQ', match: (p) => p.startsWith('/dashboard') },
  { to: '/posts', label: 'POSTS', match: (p) => p === '/posts' || p.startsWith('/posts/') },
  { to: '/teams', label: 'TEAMS', match: (p) => p.startsWith('/teams') },
  { to: '/games', label: 'EVENTS', match: (p) => p === '/games' || p.startsWith('/games/') },
  { to: '/about', label: 'ABOUT', match: (p) => p.startsWith('/about') },
]

const NAV_ACCOUNT: NavItem[] = [
  { to: '/account/profile', label: 'PROFILE', match: (p) => p.startsWith('/account/profile') },
  { to: '/account/settings', label: 'SETTINGS', match: (p) => p.startsWith('/account/settings') },
]


interface PlayerShellProps {
  children: ReactNode
  /** Widens the content column for the scoreboard, which is a wide table. */
  wide?: boolean
  /** Points shown in the header pill. Only games know this, so it's opt-in. */
  score?: number
  /** Allows an administrator to remain on a deliberately exposed player-facing page. */
  allowAdmin?: boolean
}

/**
 * Chrome for every signed-in player page.
 *
 * Deliberately separate from BuraqShell: the administration keeps using that one,
 * so nothing here can regress the admin layout.
 *
 * The route guard mirrors WithRole's contract — an unauthenticated visitor is sent
 * to login carrying `from`, so they return to the page they wanted. Server-side
 * authorisation is unchanged and remains the real gate; this is navigation only.
 */
export const PlayerShell: FC<PlayerShellProps> = ({ children, wide, score, allowAdmin = false }) => {
  const { user, error, mutate } = useUser()
  const navigate = useNavigate()
  const location = useLocation()
  const { t } = useTranslation()

  const [menuOpen, setMenuOpen] = useState(false)
  const [clock, setClock] = useState(() => new Date())
  // While this player's screen is being streamed, keep the backdrop still (see CyberBackdrop).
  const publisher = usePublisher()
  const sharing = publisher.status === 'live' || publisher.status === 'connecting' || publisher.status === 'reconnecting'
  const [drawerOpen, setDrawerOpen] = useState(false)
  const userWrapRef = useRef<HTMLDivElement>(null)

  // Retheme Mantine for the player surface. Set on <body> because Mantine portals
  // modals, menus and popovers there, outside this component's tree — a wrapper
  // class would style the page and miss every overlay. Removed on unmount so the
  // admin pages, which never mount this shell, keep stock Mantine.
  useEffect(() => {
    document.body.classList.add('bq-player')
    return () => document.body.classList.remove('bq-player')
  }, [])

  // Status-strip clock. One state update a second, only in this small subtree.
  useEffect(() => {
    const timer = window.setInterval(() => setClock(new Date()), 1000)
    return () => window.clearInterval(timer)
  }, [])

  useEffect(() => {
    if (error?.status === 401) {
      navigate(`/account/login?from=${encodeURIComponent(location.pathname)}`, { replace: true })
    }
  }, [error, navigate, location.pathname])

  // Admin accounts don't belong in the player journey. `/account/**` stays reachable —
  // there's no admin-side equivalent for profile/password/API tokens yet — and a
  // navigation can opt out via `state: { adminPreview: true }` for cases like previewing
  // a notice exactly as players see it, where landing here is the point of the click.
  useEffect(() => {
    const isAdminUser = RequireRole(Role.Admin, user?.role)
    const exempt =
      allowAdmin ||
      location.pathname.startsWith('/account/') ||
      (location.state as { adminPreview?: boolean } | null)?.adminPreview

    if (isAdminUser && !exempt) {
      navigate('/admin', { replace: true })
    }
  }, [user?.role, location.pathname, location.state, navigate, allowAdmin])

  // Close both overlays on navigation, otherwise the menu survives the route change.
  useEffect(() => {
    setMenuOpen(false)
    setDrawerOpen(false)
  }, [location.pathname])

  useEffect(() => {
    if (!menuOpen) return

    const onPointerDown = (e: MouseEvent) => {
      if (!userWrapRef.current?.contains(e.target as Node)) setMenuOpen(false)
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setMenuOpen(false)
    }

    document.addEventListener('mousedown', onPointerDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onPointerDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [menuOpen])

  const onLogout = async () => {
    try {
      await api.account.accountLogOut()
      // Drop the cached profile before navigating, so the landing page doesn't
      // render its signed-in state for a frame on the way out.
      await mutate(undefined, false)
      navigate('/')
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const initials = (user?.userName ?? '0x').slice(0, 2).toUpperCase()
  const isAdmin = RequireRole(Role.Admin, user?.role)

  const renderLinks = (items: NavItem[]) =>
    items.map((item) => (
      <Link
        key={item.to}
        to={item.to}
        className={`${classes.navLink} bqFocusRing ${item.match(location.pathname) ? classes.navLinkActive : ''}`}
        aria-current={item.match(location.pathname) ? 'page' : undefined}
      >
        {item.label}
      </Link>
    ))

  const navLinks = (
    <>
      {renderLinks(NAV_PRIMARY)}
      <span className={classes.navDivider} aria-hidden="true" />
      {renderLinks(NAV_ACCOUNT)}
    </>
  )

  return (
    <div className={classes.root}>
      <CyberBackdrop
        calm={sharing}
        right={sharing ? '● SCREEN SHARED' : `● ${clock.toLocaleTimeString([], { hour12: false })}`}
      />

      <header className={classes.bar}>
        <Link to="/posts" className={`${classes.logo} bqFocusRing`} aria-label="ZARRAR home">
          <img className={classes.logoImg} src="/assets/zarrar/zarrar-logo-sm.png" alt="ZARRAR" />
        </Link>

        <nav className={classes.nav} aria-label="Main">
          {navLinks}
        </nav>

        <div className={classes.spacer} />

        <div className={classes.barRight}>
          {score !== undefined && (
            <span className={classes.scorePill}>
              {score.toLocaleString()} <span>PTS</span>
            </span>
          )}

          <button
            type="button"
            className={`${classes.burger} bqFocusRing`}
            aria-label="Menu"
            aria-expanded={drawerOpen}
            onClick={() => setDrawerOpen((v) => !v)}
          >
            ☰
          </button>

          <div className={classes.userWrap} ref={userWrapRef}>
            <button
              type="button"
              className={`${classes.avatar} bqFocusRing`}
              aria-haspopup="menu"
              aria-expanded={menuOpen}
              aria-label="Account menu"
              onClick={() => setMenuOpen((v) => !v)}
            >
              {user?.avatar ? (
                <img className={classes.avatarImg} src={user.avatar} alt="" />
              ) : (
                initials
              )}
            </button>

            {menuOpen && (
              <div className={classes.menu} role="menu">
                <div className={classes.menuHead}>
                  <p className={classes.menuName}>{user?.userName ?? 'Player'}</p>
                  {user?.email && <p className={classes.menuMeta}>{user.email}</p>}
                </div>
                <Link className={classes.menuItem} to="/account/profile" role="menuitem">
                  Profile
                </Link>
                <Link className={classes.menuItem} to="/account/settings" role="menuitem">
                  Settings
                </Link>
                {isAdmin && (
                  <Link className={classes.menuItem} to="/admin" role="menuitem">
                    Administration
                  </Link>
                )}
                <button
                  type="button"
                  className={`${classes.menuItem} ${classes.menuDanger}`}
                  role="menuitem"
                  onClick={onLogout}
                >
                  Sign out
                </button>
              </div>
            )}
          </div>
        </div>
      </header>

      <div className={classes.status} aria-hidden="true">
        <span className={classes.statusLive}>
          <i /> UPLINK SECURE
        </span>
        <span className={classes.statusTrack}>
          <span className={classes.statusMove}>
            {Array.from({ length: 2 }).map((_, i) => (
              <span key={i}>
                OPERATOR <b>{(user?.userName ?? 'unknown').toUpperCase()}</b> // NODE ZARRAR-01 // ENCRYPTION AES-256 //
                FIRST FLAG TAKES THE TARGET // SHARE YOUR SCREEN, OUTHACK YOUR RIVAL // THE ARZENS //{' '}
              </span>
            ))}
          </span>
        </span>
        <span className={classes.statusClock}>{clock.toLocaleTimeString([], { hour12: false })}</span>
      </div>

      <nav
        className={`${classes.drawer} ${drawerOpen ? classes.drawerOpen : ''}`}
        aria-label="Main"
        aria-hidden={!drawerOpen}
      >
        {navLinks}
      </nav>

      <main className={`${classes.main} ${wide ? classes.mainWide : ''}`}>{children}</main>

      <footer className={classes.footer}>
        <span className={classes.footerMark} aria-hidden="true" />
        ZARRAR // 1V1 SHOWDOWN CTF // BY THE ARZENS
      </footer>
    </div>
  )
}
