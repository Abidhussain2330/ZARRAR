/*
 * BURAQ UI primitives.
 *
 * Import from here rather than reaching into individual files, so a component
 * can move without touching every screen. Type-only re-exports are explicit
 * because `isolatedModules` is on.
 */

export { Panel } from '@Components/buraq/Panel'
export type { PanelProps } from '@Components/buraq/Panel'

export { StatCard } from '@Components/buraq/StatCard'
export type { StatCardProps, StatDelta, StatDeltaDirection, StatDeltaPolarity } from '@Components/buraq/StatCard'

export { DataTable } from '@Components/buraq/DataTable'
export type { DataTableColumn, DataTableProps } from '@Components/buraq/DataTable'

export { BuraqLogo, BuraqMark } from '@Components/buraq/brand/BuraqMark'
export type { BuraqGlyphProps, BuraqLogoProps } from '@Components/buraq/brand/BuraqMark'

/*
 * Shell. `WithNavBar` is the only page-facing entry point in ordinary use, but
 * the pieces are exported individually too — the previous barrel omitted this
 * whole group, which meant every consumer had to reach past it into
 * `@Components/buraq/shell/*` directly.
 */
export { BuraqShell } from '@Components/buraq/shell/BuraqShell'
export type { BuraqShellProps } from '@Components/buraq/shell/BuraqShell'


export { BuraqTopBar, BuraqLanguageMenu, BuraqColorSchemeButton } from '@Components/buraq/shell/BuraqTopBar'
export type { BuraqTopBarProps, BuraqLanguageMenuProps } from '@Components/buraq/shell/BuraqTopBar'


export { BuraqFooter } from '@Components/buraq/shell/BuraqFooter'


export { BURAQ_DIFFICULTIES, DifficultyIndicator } from '@Components/buraq/indicators/DifficultyIndicator'
export type { BuraqDifficulty, DifficultyIndicatorProps } from '@Components/buraq/indicators/DifficultyIndicator'

export { CategoryBadge, resolveCategoryToken } from '@Components/buraq/indicators/CategoryBadge'
export type { CategoryBadgeProps } from '@Components/buraq/indicators/CategoryBadge'

export { StatusDot } from '@Components/buraq/indicators/StatusDot'
export type { BuraqStatus, StatusDotProps } from '@Components/buraq/indicators/StatusDot'

export { LiveBadge } from '@Components/buraq/indicators/LiveBadge'
export type { LiveBadgeProps } from '@Components/buraq/indicators/LiveBadge'

export { LoadingState } from '@Components/buraq/states/LoadingState'
export type { LoadingStateProps, LoadingVariant } from '@Components/buraq/states/LoadingState'

export { EmptyState } from '@Components/buraq/states/EmptyState'
export type { EmptyStateProps } from '@Components/buraq/states/EmptyState'

export { ErrorState } from '@Components/buraq/states/ErrorState'
export type { ErrorStateProps } from '@Components/buraq/states/ErrorState'

export { PermissionDenied } from '@Components/buraq/states/PermissionDenied'
export type { PermissionDeniedProps } from '@Components/buraq/states/PermissionDenied'

export { OfflineState } from '@Components/buraq/states/OfflineState'
export type { OfflineStateProps } from '@Components/buraq/states/OfflineState'

/** Shared by every state component that renders a heading. */
export type { BuraqHeadingLevel } from '@Components/buraq/states/StateShell'

/* Presentation preferences — motion, low-energy mode, sidebar collapse. */
export { useBuraqPreferences } from '@Hooks/buraq/useBuraqPreferences'
export type { MotionPreference, EnergyPreference } from '@Hooks/buraq/useBuraqPreferences'
export { useSyncedPreferences } from '@Hooks/buraq/useSyncedPreferences'

/*
 * Chart styling lives in @Utils, but is re-exported here so a screen composing
 * BURAQ panels and BURAQ charts has one import site.
 */
export {
  buraqChartBase,
  buraqGrid,
  buraqLegend,
  buraqSeriesColor,
  buraqTooltip,
  buraqXAxis,
  buraqYAxis,
  getBuraqChartTheme,
} from '@Utils/buraq/chartTheme'
export type { BuraqChartTheme } from '@Utils/buraq/chartTheme'
