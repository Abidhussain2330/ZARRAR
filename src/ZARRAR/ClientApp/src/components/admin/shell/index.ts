/*
 * Admin Command Center — shared surface.
 *
 * Screens import from here rather than reaching into individual files, so a
 * primitive can move without touching every admin page. Type-only re-exports
 * are explicit because `isolatedModules` is on.
 */

export { AdminLayout } from '@Components/admin/shell/AdminLayout'
export type { AdminCrumb, AdminLayoutProps } from '@Components/admin/shell/AdminLayout'

export { AdminSectionNav } from '@Components/admin/shell/AdminSectionNav'
export { ADMIN_NAV_GROUPS, useAdminNavigation } from '@Components/admin/shell/AdminNavigation'
export type { AdminNavGroup, AdminNavItem } from '@Components/admin/shell/AdminNavigation'

export { AdminToolbar, AdminSearch, AdminResultCount } from '@Components/admin/shell/AdminToolbar'
export type {
  AdminToolbarProps,
  AdminSearchProps,
  AdminResultCountProps,
} from '@Components/admin/shell/AdminToolbar'

export { AdminPagination } from '@Components/admin/shell/AdminPagination'
export type { AdminPaginationProps } from '@Components/admin/shell/AdminPagination'

export { AdminTable, AdminBulkBar } from '@Components/admin/shell/AdminTable'
export type { AdminTableProps, AdminBulkBarProps } from '@Components/admin/shell/AdminTable'

export { AdminStatusBadge } from '@Components/admin/shell/AdminStatusBadge'
export type { AdminStatusBadgeProps, AdminTone } from '@Components/admin/shell/AdminStatusBadge'

export {
  AdminMetricGrid,
  AdminSection,
  AdminFormGrid,
  AdminFormWide,
  AdminCellStack,
  AdminCellIdentity,
  AdminCellName,
  AdminCellMeta,
  AdminCellActions,
  AdminMonoValue,
  AdminOutageNotice,
  AdminPending,
} from '@Components/admin/shell/AdminPrimitives'
export type {
  AdminMetricGridProps,
  AdminSectionProps,
  AdminFormGridProps,
  AdminMonoValueProps,
  AdminOutageNoticeProps,
  AdminPendingProps,
} from '@Components/admin/shell/AdminPrimitives'

export { openAdminConfirm } from '@Components/admin/shell/AdminConfirm'
export type { AdminConfirmOptions } from '@Components/admin/shell/AdminConfirm'

export { showAdminSuccess } from '@Components/admin/shell/AdminFeedback'

export { useAdminSelection } from '@Hooks/admin/useAdminSelection'
export type { AdminSelection } from '@Hooks/admin/useAdminSelection'
