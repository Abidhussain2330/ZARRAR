import { CSSProperties, FC, ReactNode } from 'react'
import classes from '@Styles/zarrar/Indicators.module.css'

/**
 * Maps a category name onto a `--zarrar-cat-*` token suffix.
 *
 * Keys are canonical names plus the aliases seen in the wild. ZARRAR lets an
 * organiser define their own categories, so anything unrecognised resolves to
 * `misc` rather than rendering an undefined custom property.
 */
const CATEGORY_TOKENS: Record<string, string> = {
  web: 'web',
  'web exploitation': 'web',
  pwn: 'pwn',
  binary: 'pwn',
  'binary exploitation': 'pwn',
  exploitation: 'pwn',
  crypto: 'crypto',
  cryptography: 'crypto',
  reverse: 'reverse',
  reversing: 'reverse',
  re: 'reverse',
  rev: 'reverse',
  forensics: 'forensics',
  forensic: 'forensics',
  misc: 'misc',
  ppc: 'misc',
  osint: 'osint',
  intelligence: 'osint',
  stego: 'stego',
  steg: 'stego',
  steganography: 'stego',
  hardware: 'hardware',
  hw: 'hardware',
  mobile: 'mobile',
  android: 'mobile',
  ios: 'mobile',
  blockchain: 'blockchain',
  'smart contract': 'blockchain',
  ai: 'ai',
  ml: 'ai',
  'machine learning': 'ai',
  pentest: 'pentest',
  'penetration testing': 'pentest',
}

/** Exported so charts and legends can colour a category the same way a badge does. */
export const resolveCategoryToken = (category: string): string => {
  const key = category
    .trim()
    .toLowerCase()
    .replace(/[\s_-]+/g, ' ')
  return CATEGORY_TOKENS[key] ?? 'misc'
}

export interface CategoryBadgeProps {
  /** Any string. ZARRAR categories are configurable, so no fixed union here. */
  category: string
  /** Display text when it differs from the lookup key, e.g. a translated name. */
  label?: ReactNode
  icon?: ReactNode
  size?: 'sm' | 'md'
  className?: string
}

export const CategoryBadge: FC<CategoryBadgeProps> = ({ category, label, icon, size = 'md', className }) => {
  // Indirection through a local custom property: the token name is only known at
  // runtime, and CSS Modules cannot build `var(--zarrar-cat-<x>)` from a class.
  const style = { '--zarrar-badge-color': `var(--zarrar-cat-${resolveCategoryToken(category)})` } as CSSProperties

  return (
    <span className={[classes.category, className ?? ''].filter(Boolean).join(' ')} data-size={size} style={style}>
      {icon && (
        <span className={classes.categoryIcon} aria-hidden>
          {icon}
        </span>
      )}
      <span className={classes.categoryText}>{label ?? category}</span>
    </span>
  )
}
