import { generateColors } from '@mantine/colors-generator'
import {
  ActionIcon,
  Avatar,
  Badge,
  Code,
  Loader,
  MantineThemeOverride,
  Menu,
  Modal,
  Popover,
  Switch,
  Tabs,
  Tooltip,
  TooltipFloating,
  createTheme,
  useMantineTheme,
} from '@mantine/core'
import { createStyles } from '@mantine/emotion'
import { useLocalStorage, useMediaQuery } from '@mantine/hooks'
import { useEffect, useState } from 'react'
import { useLocation } from 'react-router'
import { useConfig } from '@Hooks/useConfig'
import tooltipClasses from '@Styles/Tooltip.module.css'

const CustomTheme: MantineThemeOverride = {
  colors: {
    gray: [
      '#EBEBEB',
      '#CFCFCF',
      '#B3B3B3',
      '#969696',
      '#7A7A7A',
      '#5E5E5E',
      '#414141',
      '#252525',
      '#202020',
      '#141414',
    ],
    // ZARRAR signal red. Mantine's dark scheme reads shade 8, so the scale is laid out
    // to land the logo red (#e01b2e / #ff2a3d) on the shades buttons and accents use.
    brand: [
      '#ffe8ea',
      '#ffd0d4',
      '#ffa3ab',
      '#ff7480',
      '#ff4e5c',
      '#ff3346',
      '#ff2a3d',
      '#f01f33',
      '#e01b2e',
      '#b3121f',
    ],
    alert: [
      '#FFB4B4',
      '#FFA0A0',
      '#FF8c8c',
      '#FF7878',
      '#FF6464',
      '#FE5050',
      '#FE3c3c',
      '#FE2828',
      '#FC1414',
      '#FC0000',
    ],
    light: [
      '#FFFFFF',
      '#F8F8F8',
      '#EFEFEF',
      '#E0E0E0',
      '#DFDFDF',
      '#D0D0D0',
      '#CFCFCF',
      '#C0C0C0',
      '#BFBFBF',
      '#B0B0B0',
    ],
    dark: [
      '#d5d7d7',
      '#acaeae',
      '#8c8f8f',
      '#666969',
      '#4d4f4f',
      '#343535',
      '#2b2c2c',
      '#1d1e1e',
      '#0c0d0d',
      '#010101',
    ],
  },
  primaryColor: 'brand',
  fontFamily:
    'Lexend, -apple-system, BlinkMacSystemFont, Helvetica Neue, PingFang SC, Microsoft YaHei, Source Han Sans SC, Noto Sans CJK SC, sans-serif',
  fontFamilyMonospace:
    'JetBrains Mono, ui-monospace, SFMono-Regular, Monaco, Consolas, Courier New, monospace, sans-serif',
  headings: {
    fontFamily: 'Lexend, sans-serif',
  },
  breakpoints: {
    xs: '30em',
    sm: '48em',
    md: '64em',
    lg: '74em',
    xl: '90em',
    w18: '1800px',
    w24: '2400px',
    w30: '3000px',
    w36: '3600px',
    w42: '4200px',
    w48: '4800px',
  },
  components: {
    Loader: Loader.extend({
      defaultProps: {
        type: 'bars',
      },
    }),
    Switch: Switch.extend({
      styles: {
        body: {
          alignItems: 'center',
        },
        labelWrapper: {
          display: 'flex',
        },
      },
    }),
    Modal: Modal.extend({
      defaultProps: {
        centered: true,
        styles: {
          title: {
            fontWeight: 'bold',
          },
        },
      },
    }),
    Popover: Popover.extend({
      defaultProps: {
        withinPortal: true,
      },
    }),
    ActionIcon: ActionIcon.extend({
      defaultProps: {
        variant: 'transparent',
      },
    }),
    Badge: Badge.extend({
      defaultProps: {
        variant: 'outline',
      },
    }),
    Tabs: Tabs.extend({
      styles: {
        tab: {
          padding: 'var(--mantine-spacing-xs)',
          fontWeight: 500,
        },
      },
    }),
    Avatar: Avatar.extend({
      defaultProps: {
        color: 'brand',
      },
    }),
    Menu: Menu.extend({
      styles: {
        item: {
          fontWeight: 500,
        },
      },
    }),
    Code: Code.extend({
      styles: {
        root: {
          fontWeight: 500,
        },
      },
    }),
    Tooltip: Tooltip.extend({
      classNames: tooltipClasses,
    }),
    TooltipFloating: TooltipFloating.extend({
      classNames: tooltipClasses,
    }),
  },
}

export enum ColorProvider {
  Managed = 'Managed',
  Default = 'Default',
  Custom = 'Custom',
}

export interface CustomColor {
  provider: ColorProvider
  color: string
}

export const useCustomColor = () => {
  const [customColor, setCustomColorInner] = useLocalStorage<CustomColor>({
    key: 'custom-theme',
    defaultValue: { provider: ColorProvider.Managed, color: '' } as CustomColor,
    getInitialValueInEffect: false,
    serialize: (value: CustomColor) => {
      if (value.provider === ColorProvider.Custom && /^#[0-9A-F]{6}$/i.test(value.color)) {
        return value.color
      } else if (value.provider === ColorProvider.Managed) {
        return ''
      } else {
        return 'brand'
      }
    },
    deserialize: (value?: string) => {
      if (typeof value !== 'string') return { provider: ColorProvider.Managed, color: '' }

      if (value === 'brand') {
        return { provider: ColorProvider.Default, color: '' }
      } else if (/^#[0-9A-F]{6}$/i.test(value)) {
        return { provider: ColorProvider.Custom, color: value }
      } else {
        return { provider: ColorProvider.Managed, color: '' }
      }
    },
  })

  const setCustomColor = (color: CustomColor) => {
    // validate custom color, do not save invalid values
    if (color.provider === ColorProvider.Custom && !/^#[0-9A-F]{6}$/i.test(color.color)) return

    setCustomColorInner(color)
  }

  // color: null for use platform color, 'brand' for default theme
  //        or hex color string for custom color
  return { customColor, setCustomColor }
}

export const useCustomTheme = () => {
  const { config } = useConfig()
  const { customColor } = useCustomColor()
  const location = useLocation()
  // Admin's identity is fixed, not the user-configurable player theme — and it has to be
  // set via the theme object, not a competing CSS override. MantineProvider computes its
  // own `--mantine-*` variables from `theme` and injects them through Emotion at runtime,
  // landing after any statically-imported CSS in the DOM; a plain `:root` override in
  // tokens.css can never reliably win that race. This is the only mechanism that actually
  // works, which `generateColors` here already proves out for the player-facing custom
  // color feature below.
  const isAdminRoute = location.pathname.startsWith('/admin')

  const resolveManaged = (color: string | null | undefined) => {
    return color && /^#[0-9A-F]{6}$/i.test(color) ? color : null
  }

  const [theme, setTheme] = useState<MantineThemeOverride>(createTheme(CustomTheme))

  useEffect(() => {
    if (isAdminRoute) {
      setTheme({
        ...CustomTheme,
        colors: {
          ...CustomTheme.colors,
          admin: generateColors('#e33a3a'),
          // Mantine's own dark-mode neutral scale (Paper/Modal/Menu/Popover default
          // surfaces, borders) — swapped to the same warm near-black BURAQ already
          // uses via --buraq-bg-*/--buraq-text-*, so components that never got a
          // bespoke admin restyle still land on the right palette instead of
          // Mantine's stock cool gray.
          dark: [
            '#e8eefc',
            '#a3b1cc',
            '#6b7a99',
            '#45526b',
            '#2c3555',
            '#131b33',
            '#0c1122',
            '#0e1428',
            '#080b16',
            '#05070f',
          ],
        },
        components: {
          ...CustomTheme.components,
          Avatar: Avatar.extend({
            defaultProps: {
              color: 'admin',
            },
          }),
        },
        primaryColor: 'admin',
      })
      return
    }

    if (customColor.provider === ColorProvider.Default) {
      setTheme(CustomTheme)
      return
    }

    const resolvedColor =
      customColor.provider === ColorProvider.Custom
        ? customColor.color
        : customColor.provider === ColorProvider.Managed
          ? resolveManaged(config.customTheme)
          : null

    if (resolvedColor) {
      setTheme({
        ...CustomTheme,
        colors: {
          ...CustomTheme.colors,
          custom: generateColors(resolvedColor),
        },
        components: {
          ...CustomTheme.components,
          Avatar: Avatar.extend({
            defaultProps: {
              color: 'custom',
            },
          }),
        },
        primaryColor: 'custom',
      })
    } else {
      setTheme(CustomTheme)
    }
  }, [customColor, config.customTheme, isAdminRoute])

  return { theme }
}

export const useIsMobile = (limit?: number) => {
  const theme = useMantineTheme()
  const isMobile = useMediaQuery(`(max-width: ${limit ? `${limit}px` : theme.breakpoints.sm})`)
  return isMobile
}

interface UseDisplayInputStylesProps {
  ff?: 'monospace' | 'text'
  fw?: string | number
  lh?: string | number
  cs?: string
}

export const useDisplayInputStyles = createStyles(
  (theme, { fw = 'normal', lh = '1.5rem', ff = 'text', cs = 'auto' }: UseDisplayInputStylesProps) => ({
    wrapper: {
      width: '100%',
    },
    input: {
      fontWeight: fw,
      fontFamily: ff === 'text' ? theme.fontFamily : theme.fontFamilyMonospace,
      height: lh,
      lineHeight: lh,
      cursor: cs,
      userSelect: 'none',
      minHeight: '1rem',
      maxHeight: '2rem',
    },
  })
)
