import {
  mdiAccountCogOutline,
  mdiAccountGroupOutline,
  mdiChartTimelineVariant,
  mdiFileDocumentOutline,
  mdiFlagOutline,
  mdiFolderOutline,
  mdiNoteTextOutline,
  mdiPackageVariantClosed,
  mdiSitemapOutline,
  mdiShieldSearch,
  mdiViewDashboardOutline,
  mdiBroadcast,
} from '@mdi/js'
import { useMemo } from 'react'
import { useLocation } from 'react-router'
import { RequireRole } from '@Components/WithRole'
import { useUserRole } from '@Hooks/useUser'
import { Role } from '@Api'

export interface AdminNavItem {
  /** Stable identity — also the React key and the `data-nav` test hook. */
  key: string
  /** i18n key; the caller resolves it so labels follow the active language. */
  label: string
  /** English text used when a translation is missing. */
  fallback: string
  icon: string
  link: string
  /** Minimum role the backend enforces for the section's endpoints. */
  role: Role
  /**
   * Extra prefixes that should keep this entry lit. `/admin/games` owns every
   * game editor route, and the game monitor lives outside `/admin` entirely.
   */
  matches?: string[]
}

export interface AdminNavGroup {
  key: string
  label: string
  fallback: string
  items: AdminNavItem[]
}

/**
 * Every destination in the Admin Command Center.
 *
 * Ordered by how often an operator reaches for it during a live event, not
 * alphabetically. Adding a section here is the only place it needs adding — the
 * rail, the mobile drawer and the breadcrumb root all read this list.
 *
 * `role` mirrors the attribute on the controller behind each section
 * (`[RequireAdmin]` / `[RequireMonitor]`). It hides entries the caller cannot
 * use; it is *not* the access control. `WithRole` still guards the route and
 * the backend still rejects the request.
 */
export const ADMIN_NAV_GROUPS: AdminNavGroup[] = [
  {
    key: 'overview',
    label: 'admin.nav.group.overview',
    fallback: 'Overview',
    items: [
      {
        key: 'dashboard',
        label: 'admin.nav.dashboard',
        fallback: 'Dashboard',
        icon: mdiViewDashboardOutline,
        link: '/admin',
        role: Role.Admin,
      },
    ],
  },
  {
    key: 'competition',
    label: 'admin.nav.group.competition',
    fallback: 'Competition',
    items: [
      {
        key: 'games',
        label: 'admin.tab.games.index',
        fallback: 'Events',
        icon: mdiFlagOutline,
        link: '/admin/games',
        role: Role.Admin,
      },
      {
        // ZARRAR 1v1 showdown control room: synced stage preview + match controls.
        key: 'zarrar-live',
        label: 'admin.nav.zarrar_live',
        fallback: 'ZARRAR Live',
        icon: mdiBroadcast,
        link: '/admin/live',
        role: Role.Admin,
      },
      {
        key: 'posts',
        label: 'admin.nav.posts',
        fallback: 'Notices',
        icon: mdiNoteTextOutline,
        link: '/admin/notices',
        role: Role.Admin,
        // The list is `/admin/notices`; the editor is `/admin/notices/:postId`,
        // which the prefix match below already covers.
      },
    ],
  },
  {
    key: 'people',
    label: 'admin.nav.group.people',
    fallback: 'People',
    items: [
      {
        key: 'users',
        label: 'admin.tab.users',
        fallback: 'Users',
        icon: mdiAccountCogOutline,
        link: '/admin/users',
        role: Role.Admin,
      },
      {
        key: 'teams',
        label: 'admin.tab.teams',
        fallback: 'Teams',
        icon: mdiAccountGroupOutline,
        link: '/admin/teams',
        role: Role.Admin,
      },
    ],
  },
  {
    key: 'infrastructure',
    label: 'admin.nav.group.infrastructure',
    fallback: 'Infrastructure',
    items: [
      {
        key: 'instances',
        label: 'admin.tab.instances',
        fallback: 'Containers',
        icon: mdiPackageVariantClosed,
        link: '/admin/instances',
        role: Role.Admin,
      },
      {
        key: 'soc',
        label: 'admin.soc.nav',
        fallback: 'SOC & Anti-Cheat',
        icon: mdiShieldSearch,
        link: '/admin/soc',
        role: Role.Admin,
      },
      {
        key: 'files',
        label: 'admin.nav.files',
        fallback: 'Files',
        icon: mdiFolderOutline,
        link: '/admin/files',
        role: Role.Admin,
      },
    ],
  },
  {
    key: 'observability',
    label: 'admin.nav.group.observability',
    fallback: 'Observability',
    items: [
      {
        key: 'logs',
        label: 'admin.tab.logs',
        fallback: 'Logs',
        icon: mdiFileDocumentOutline,
        link: '/admin/logs',
        role: Role.Admin,
      },
      {
        // ZARRAR: the auditorium stage (monitor console). Opens outside the admin shell.
        key: 'zarrar-stage',
        label: 'admin.nav.zarrar_stage',
        fallback: 'ZARRAR Stage',
        icon: mdiBroadcast,
        link: '/monitor',
        role: Role.Monitor,
      },
      {
        key: 'monitor',
        label: 'admin.nav.monitor',
        fallback: 'Game monitor',
        icon: mdiChartTimelineVariant,
        link: '/games',
        role: Role.Monitor,
        matches: ['/games/'],
      },
    ],
  },
  {
    key: 'system',
    label: 'admin.nav.group.system',
    fallback: 'System',
    items: [
      {
        key: 'settings',
        label: 'admin.tab.settings',
        fallback: 'Settings',
        icon: mdiSitemapOutline,
        link: '/admin/settings',
        role: Role.Admin,
      },
    ],
  },
]

const matchesItem = (item: AdminNavItem, pathname: string): boolean => {
  // `/admin` would otherwise claim every admin route.
  if (item.link === '/admin') return pathname === '/admin' || pathname === '/admin/'

  // The monitor entry only lights up on a monitor route, not on `/games` itself.
  if (item.key === 'monitor') return /^\/games\/\d+\/monitor(\/|$)/.test(pathname)

  if (pathname === item.link || pathname.startsWith(`${item.link}/`)) return true

  return item.matches?.some((prefix) => pathname.startsWith(prefix)) ?? false
}

/**
 * Resolves the visible sections for the signed-in operator and which one the
 * current URL belongs to. Returns groups with no visible items removed, so a
 * Monitor never sees an empty "System" heading.
 */
export const useAdminNavigation = () => {
  const { role } = useUserRole()
  const location = useLocation()

  const groups = useMemo(
    () =>
      ADMIN_NAV_GROUPS.map((group) => ({
        ...group,
        items: group.items.filter((item) => RequireRole(item.role, role)),
      })).filter((group) => group.items.length > 0),
    [role]
  )

  const activeKey = useMemo(() => {
    for (const group of groups) {
      const hit = group.items.find((item) => matchesItem(item, location.pathname))
      if (hit) return hit.key
    }
    return undefined
  }, [groups, location.pathname])

  return { groups, activeKey, pathname: location.pathname, role }
}
