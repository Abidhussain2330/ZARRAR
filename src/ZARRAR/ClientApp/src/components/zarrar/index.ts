/*
 * ZARRAR UI primitives.
 *
 * Import from here rather than reaching into individual files, so a component
 * can move without touching every screen. Type-only re-exports are explicit
 * because `isolatedModules` is on.
 */

export { Panel } from '@Components/zarrar/Panel'
export type { PanelProps } from '@Components/zarrar/Panel'

export { StatCard } from '@Components/zarrar/StatCard'
export type { StatCardProps, StatDelta, StatDeltaDirection, StatDeltaPolarity } from '@Components/zarrar/StatCard'

export { DataTable } from '@Components/zarrar/DataTable'
export type { DataTableColumn, DataTableProps } from '@Components/zarrar/DataTable'

export { ZarrarLogo, ZarrarMark } from '@Components/zarrar/brand/ZarrarMark'
export type { ZarrarGlyphProps, ZarrarLogoProps } from '@Components/zarrar/brand/ZarrarMark'

/*
 * Shell. `WithNavBar` is the only page-facing entry point in ordinary use, but
 * the pieces are exported individually too — the previous barrel omitted this
 * whole group, which meant every consumer had to reach past it into
 * `@Components/zarrar/shell/*` directly.
 */
export { ZarrarShell } from '@Components/zarrar/shell/ZarrarShell'
export type { ZarrarShellProps } from '@Components/zarrar/shell/ZarrarShell'


export { ZarrarTopBar, ZarrarLanguageMenu, ZarrarColorSchemeButton } from '@Components/zarrar/shell/ZarrarTopBar'
export type { ZarrarTopBarProps, ZarrarLanguageMenuProps } from '@Components/zarrar/shell/ZarrarTopBar'


export { ZarrarFooter } from '@Components/zarrar/shell/ZarrarFooter'


export { ZARRAR_DIFFICULTIES, DifficultyIndicator } from '@Components/zarrar/indicators/DifficultyIndicator'
export type { ZarrarDifficulty, DifficultyIndicatorProps } from '@Components/zarrar/indicators/DifficultyIndicator'

export { CategoryBadge, resolveCategoryToken } from '@Components/zarrar/indicators/CategoryBadge'
export type { CategoryBadgeProps } from '@Components/zarrar/indicators/CategoryBadge'

export { StatusDot } from '@Components/zarrar/indicators/StatusDot'
export type { ZarrarStatus, StatusDotProps } from '@Components/zarrar/indicators/StatusDot'

export { LiveBadge } from '@Components/zarrar/indicators/LiveBadge'
export type { LiveBadgeProps } from '@Components/zarrar/indicators/LiveBadge'

export { LoadingState } from '@Components/zarrar/states/LoadingState'
export type { LoadingStateProps, LoadingVariant } from '@Components/zarrar/states/LoadingState'

export { EmptyState } from '@Components/zarrar/states/EmptyState'
export type { EmptyStateProps } from '@Components/zarrar/states/EmptyState'

export { ErrorState } from '@Components/zarrar/states/ErrorState'
export type { ErrorStateProps } from '@Components/zarrar/states/ErrorState'

export { PermissionDenied } from '@Components/zarrar/states/PermissionDenied'
export type { PermissionDeniedProps } from '@Components/zarrar/states/PermissionDenied'

export { OfflineState } from '@Components/zarrar/states/OfflineState'
export type { OfflineStateProps } from '@Components/zarrar/states/OfflineState'

/** Shared by every state component that renders a heading. */
export type { ZarrarHeadingLevel } from '@Components/zarrar/states/StateShell'

/* Presentation preferences — motion, low-energy mode, sidebar collapse. */
export { useZarrarPreferences } from '@Hooks/zarrar/useZarrarPreferences'
export type { MotionPreference, EnergyPreference } from '@Hooks/zarrar/useZarrarPreferences'
export { useSyncedPreferences } from '@Hooks/zarrar/useSyncedPreferences'

/*
 * Chart styling lives in @Utils, but is re-exported here so a screen composing
 * ZARRAR panels and ZARRAR charts has one import site.
 */
export {
  zarrarChartBase,
  zarrarGrid,
  zarrarLegend,
  zarrarSeriesColor,
  zarrarTooltip,
  zarrarXAxis,
  zarrarYAxis,
  getZarrarChartTheme,
} from '@Utils/zarrar/chartTheme'
export type { ZarrarChartTheme } from '@Utils/zarrar/chartTheme'
