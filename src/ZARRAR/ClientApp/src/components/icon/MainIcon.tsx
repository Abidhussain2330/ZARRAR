import { StyleProp, rem } from '@mantine/core'
import { FC, SVGProps } from 'react'

/*
 * The ZARRAR application mark.
 *
 * This used to carry the upstream ZARRAR logo path data. That artwork is the
 * upstream author's trademark and is explicitly excluded from Custom THE ARZENS License by
 * TRADEMARKS.md §5, so it cannot ship as this product's identity. It has been
 * replaced by THE ARZENS mark, which is the same glyph drawn by
 * `@Components/zarrar/brand/ZarrarMark` — kept in sync by hand because this
 * component has to accept Mantine's `StyleProp` sizing and raw SVG props that
 * ZarrarMark does not.
 *
 * Packaged raster assets live in `ClientApp/public/`; preserve the identity and licensing notes.
 */

export interface MainIconProps {
  ignoreTheme?: boolean
  size?: StyleProp<string | number>
}

export const MainIcon: FC<MainIconProps & SVGProps<SVGSVGElement>> = ({ size }) => (
  // ZARRAR: the Z shield emblem. `ignoreTheme` no longer changes anything because the
  // emblem is raster artwork; the prop is kept so existing callers still type-check.
  <img
    src="/assets/zarrar/zarrar-emblem-sm.png"
    alt="ZARRAR"
    style={{
      width: rem(size) || 'auto',
      height: 'auto',
      aspectRatio: '1 / 1',
      objectFit: 'contain',
    }}
  />
)
