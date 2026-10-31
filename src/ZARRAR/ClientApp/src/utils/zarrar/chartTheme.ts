import type {
  EChartsOption,
  GridComponentOption,
  LegendComponentOption,
  TooltipComponentOption,
  XAXisComponentOption,
  YAXisComponentOption,
} from 'echarts'

/*
 * Chart styling for ECharts, the charting library already in this app.
 *
 * ECharts draws to a canvas and takes plain numbers and colour strings, so it
 * cannot read `var(--zarrar-*)` the way CSS can. Everything here is therefore read
 * off the document element at call time and converted: tokens.css stays the
 * single source of truth, and a token change lands on the next render rather
 * than at build time. The fallbacks mirror tokens.css and are only used when
 * the module runs without a document.
 */

const FALLBACK = {
  textPrimary: '#e8eefc',
  textSecondary: '#a3b1cc',
  textMuted: '#6b7a99',
  border: 'rgb(90 120 180 / 18%)',
  borderStrong: 'rgb(90 120 180 / 32%)',
  bgElevated: '#0c1122',
  radius: '8px',
  shadow: '0 12px 32px rgb(0 0 0 / 55%)',
  fontSans: "'Inter', system-ui, sans-serif",
  fontMono: "'JetBrains Mono', ui-monospace, monospace",
} as const

/**
 * Series colours in the order ECharts assigns them. Hue jumps rather than
 * walking the spectrum, so two adjacent series never look like one gradient.
 */
const PALETTE_TOKENS = [
  '--zarrar-cyan',
  '--zarrar-violet',
  '--zarrar-success',
  '--zarrar-warning',
  '--zarrar-blue-bright',
  '--zarrar-magenta',
  '--zarrar-cat-crypto',
  '--zarrar-cat-hardware',
  '--zarrar-cat-mobile',
  '--zarrar-cat-pwn',
] as const

const FALLBACK_PALETTE = [
  '#22d3ee',
  '#8b5cf6',
  '#22c55e',
  '#f59e0b',
  '#60a5fa',
  '#d946ef',
  '#2dd4bf',
  '#fb923c',
  '#facc15',
  '#f43f5e',
] as const

/** The 12px floor set by `--zarrar-text-2xs`. Chart text never goes below it. */
const MIN_FONT_SIZE = 12

/** `--zarrar-space-1` … `--zarrar-space-5`, resolved to the pixels ECharts expects. */
export interface ZarrarChartSpacing {
  xs: number
  sm: number
  md: number
  lg: number
  xl: number
}

export interface ZarrarChartTheme {
  fontFamily: string
  fontFamilyMono: string
  fontSize: number
  /** Axis tick and legend label colour. */
  labelColor: string
  /** Axis names and secondary annotation. */
  mutedColor: string
  axisLineColor: string
  splitLineColor: string
  tooltipBackground: string
  tooltipBorderColor: string
  tooltipTextColor: string
  /** Raw CSS length, passed straight through to the tooltip's own stylesheet. */
  tooltipRadius: string
  tooltipShadow: string
  space: ZarrarChartSpacing
  palette: string[]
}

const FALLBACK_SPACING: ZarrarChartSpacing = { xs: 4, sm: 8, md: 12, lg: 16, xl: 24 }

const toPx = (value: string, fallback: number, rootFontSize: number): number => {
  const parsed = Number.parseFloat(value)
  if (!Number.isFinite(parsed)) return fallback
  return value.trim().endsWith('rem') ? parsed * rootFontSize : parsed
}

const FALLBACK_THEME: ZarrarChartTheme = {
  fontFamily: FALLBACK.fontSans,
  fontFamilyMono: FALLBACK.fontMono,
  fontSize: MIN_FONT_SIZE,
  labelColor: FALLBACK.textSecondary,
  mutedColor: FALLBACK.textMuted,
  axisLineColor: FALLBACK.borderStrong,
  splitLineColor: FALLBACK.border,
  tooltipBackground: FALLBACK.bgElevated,
  tooltipBorderColor: FALLBACK.borderStrong,
  tooltipTextColor: FALLBACK.textPrimary,
  tooltipRadius: FALLBACK.radius,
  tooltipShadow: FALLBACK.shadow,
  space: FALLBACK_SPACING,
  palette: [...FALLBACK_PALETTE],
}

/**
 * Reads the current token values.
 *
 * Call it inside the memo that builds the option object, keyed on whatever the
 * screen already uses to signal a theme change — the result is a snapshot, not
 * a live binding.
 */
export const getZarrarChartTheme = (): ZarrarChartTheme => {
  if (typeof document === 'undefined') return FALLBACK_THEME

  const computed = getComputedStyle(document.documentElement)
  const read = (name: string, fallback: string): string => computed.getPropertyValue(name).trim() || fallback

  // Custom properties come back as authored, so a token may be px or rem
  // depending on how PostCSS processed it. Both are normalised here.
  const rootFontSize = toPx(computed.fontSize, 16, 16)
  const space = (name: string, fallback: number): number =>
    Math.round(toPx(read(name, `${fallback}px`), fallback, rootFontSize))

  return {
    fontFamily: read('--zarrar-font-sans', FALLBACK.fontSans),
    fontFamilyMono: read('--zarrar-font-mono', FALLBACK.fontMono),
    fontSize: Math.max(MIN_FONT_SIZE, Math.round(toPx(read('--zarrar-text-2xs', '0.75rem'), MIN_FONT_SIZE, rootFontSize))),
    labelColor: read('--zarrar-text-secondary', FALLBACK.textSecondary),
    mutedColor: read('--zarrar-text-muted', FALLBACK.textMuted),
    axisLineColor: read('--zarrar-border-strong', FALLBACK.borderStrong),
    splitLineColor: read('--zarrar-border', FALLBACK.border),
    tooltipBackground: read('--zarrar-bg-elevated', FALLBACK.bgElevated),
    tooltipBorderColor: read('--zarrar-border-strong', FALLBACK.borderStrong),
    tooltipTextColor: read('--zarrar-text-primary', FALLBACK.textPrimary),
    tooltipRadius: read('--zarrar-radius-md', FALLBACK.radius),
    tooltipShadow: read('--zarrar-shadow-lg', FALLBACK.shadow),
    space: {
      xs: space('--zarrar-space-1', FALLBACK_SPACING.xs),
      sm: space('--zarrar-space-2', FALLBACK_SPACING.sm),
      md: space('--zarrar-space-3', FALLBACK_SPACING.md),
      lg: space('--zarrar-space-4', FALLBACK_SPACING.lg),
      xl: space('--zarrar-space-5', FALLBACK_SPACING.xl),
    },
    palette: PALETTE_TOKENS.map((token, index) => read(token, FALLBACK_PALETTE[index])),
  }
}

/** Stable colour for a series index, for legends and keys drawn outside ECharts. */
export const zarrarSeriesColor = (index: number, theme: ZarrarChartTheme = getZarrarChartTheme()): string =>
  theme.palette[Math.abs(index) % theme.palette.length]

export const zarrarXAxis = (
  theme: ZarrarChartTheme = getZarrarChartTheme(),
  splitLine = false
): XAXisComponentOption => ({
  axisLine: { lineStyle: { color: theme.axisLineColor } },
  axisTick: { show: false },
  axisLabel: { color: theme.labelColor, fontFamily: theme.fontFamily, fontSize: theme.fontSize },
  nameTextStyle: { color: theme.mutedColor, fontFamily: theme.fontFamily, fontSize: theme.fontSize },
  splitLine: { show: splitLine, lineStyle: { color: theme.splitLineColor, type: 'dashed' } },
})

export const zarrarYAxis = (
  theme: ZarrarChartTheme = getZarrarChartTheme(),
  splitLine = true
): YAXisComponentOption => ({
  axisLine: { show: false },
  axisTick: { show: false },
  axisLabel: { color: theme.labelColor, fontFamily: theme.fontFamily, fontSize: theme.fontSize },
  nameTextStyle: { color: theme.mutedColor, fontFamily: theme.fontFamily, fontSize: theme.fontSize },
  splitLine: { show: splitLine, lineStyle: { color: theme.splitLineColor, type: 'dashed' } },
})

export const zarrarTooltip = (theme: ZarrarChartTheme = getZarrarChartTheme()): TooltipComponentOption => ({
  trigger: 'axis',
  backgroundColor: theme.tooltipBackground,
  borderColor: theme.tooltipBorderColor,
  borderWidth: 1,
  padding: [theme.space.sm, theme.space.md],
  textStyle: { color: theme.tooltipTextColor, fontFamily: theme.fontFamily, fontSize: theme.fontSize },
  axisPointer: { lineStyle: { color: theme.axisLineColor }, crossStyle: { color: theme.axisLineColor } },
  extraCssText: `border-radius:${theme.tooltipRadius};box-shadow:${theme.tooltipShadow};`,
})

/**
 * `outerBoundsMode` + `outerBoundsContain` is the current spelling of the
 * deprecated `grid.containLabel`: it keeps long axis labels inside the canvas
 * instead of clipping them on a narrow viewport.
 */
export const zarrarGrid = (theme: ZarrarChartTheme = getZarrarChartTheme()): GridComponentOption => ({
  top: theme.space.xl,
  left: theme.space.sm,
  right: theme.space.lg,
  bottom: theme.space.sm,
  outerBoundsMode: 'same',
  outerBoundsContain: 'axisLabel',
})

export const zarrarLegend = (theme: ZarrarChartTheme = getZarrarChartTheme()): LegendComponentOption => ({
  type: 'scroll',
  icon: 'roundRect',
  itemWidth: theme.space.md,
  itemHeight: theme.space.xs,
  itemGap: theme.space.lg,
  textStyle: { color: theme.labelColor, fontFamily: theme.fontFamily, fontSize: theme.fontSize },
  pageTextStyle: { color: theme.mutedColor, fontFamily: theme.fontFamily, fontSize: theme.fontSize },
})

/**
 * The shared base a ZARRAR chart spreads into its own option object. Axis
 * types, series and data stay with the caller.
 */
export const zarrarChartBase = (theme: ZarrarChartTheme = getZarrarChartTheme()): EChartsOption => ({
  backgroundColor: 'transparent',
  color: theme.palette,
  textStyle: { fontFamily: theme.fontFamily, fontSize: theme.fontSize, color: theme.labelColor },
  grid: zarrarGrid(theme),
  legend: zarrarLegend(theme),
  tooltip: zarrarTooltip(theme),
})
