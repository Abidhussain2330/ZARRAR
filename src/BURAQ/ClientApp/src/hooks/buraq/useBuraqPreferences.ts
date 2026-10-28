import { useLocalStorage, useMediaQuery } from '@mantine/hooks'
import { useEffect } from 'react'

export type MotionPreference = 'system' | 'full' | 'reduced'
export type EnergyPreference = 'normal' | 'low'

const MOTION_KEY = 'buraq-motion'
const ENERGY_KEY = 'buraq-energy'
const SIDEBAR_KEY = 'buraq-sidebar-collapsed'

const LEGACY_MOTION_KEY = 'arzens-motion'
const LEGACY_ENERGY_KEY = 'arzens-energy'
const LEGACY_SIDEBAR_KEY = 'arzens-sidebar-collapsed'

const MOTION_VALUES: readonly MotionPreference[] = ['system', 'full', 'reduced']
const ENERGY_VALUES: readonly EnergyPreference[] = ['normal', 'low']

const isMotionPreference = (value: unknown): value is MotionPreference =>
  typeof value === 'string' && (MOTION_VALUES as readonly string[]).includes(value)

const isEnergyPreference = (value: unknown): value is EnergyPreference =>
  typeof value === 'string' && (ENERGY_VALUES as readonly string[]).includes(value)

const isBoolean = (value: unknown): value is boolean => typeof value === 'boolean'

/**
 * One-time migration of a single preference from its pre-BURAQ `arzens-*` key.
 *
 * No-ops once the new key exists — including on every later call, since the
 * legacy key is removed as soon as it is read. The legacy value is parsed and
 * validated against its known union/type before being copied; an unrecognised
 * or corrupted value is discarded rather than carried forward, and the stale
 * key is still removed either way. This preference data is presentation-only
 * (motion, low-energy mode, sidebar state) — never authentication, a token, or
 * anything else sensitive — and no value, legacy or migrated, is logged.
 */
const migrateLegacyPreferenceKey = <T,>(
  legacyKey: string,
  newKey: string,
  isValid: (value: unknown) => value is T
): void => {
  if (typeof window === 'undefined') return
  if (window.localStorage.getItem(newKey) !== null) return

  const legacyRaw = window.localStorage.getItem(legacyKey)
  if (legacyRaw === null) return

  try {
    const legacyValue: unknown = JSON.parse(legacyRaw)
    if (isValid(legacyValue)) {
      window.localStorage.setItem(newKey, JSON.stringify(legacyValue))
    }
  } catch {
    // Corrupted legacy value — discard it; the new key falls through to its
    // normal default below.
  }

  window.localStorage.removeItem(legacyKey)
}

// Runs once at module evaluation, which happens before any component that
// imports this hook renders — so by the time `useLocalStorage` below performs
// its own synchronous initial read, migration has already completed.
migrateLegacyPreferenceKey(LEGACY_MOTION_KEY, MOTION_KEY, isMotionPreference)
migrateLegacyPreferenceKey(LEGACY_ENERGY_KEY, ENERGY_KEY, isEnergyPreference)
migrateLegacyPreferenceKey(LEGACY_SIDEBAR_KEY, SIDEBAR_KEY, isBoolean)

/**
 * Presentation preferences for the BURAQ shell.
 *
 * Applied as `data-buraq-motion` / `data-buraq-energy` attributes on the document
 * root, which `tokens.css` keys off. Doing it with attributes rather than React
 * context means the preference reaches plain CSS — including pseudo-elements and
 * keyframes — without every component having to subscribe.
 *
 * Stored locally so the choice survives a reload even when signed out. For
 * authenticated users the server copy is authoritative and is reconciled by
 * `useSyncedPreferences`.
 */
export const useBuraqPreferences = () => {
  const systemPrefersReduced = useMediaQuery('(prefers-reduced-motion: reduce)', false, {
    getInitialValueInEffect: false,
  })

  const [motion, setMotion] = useLocalStorage<MotionPreference>({
    key: MOTION_KEY,
    defaultValue: 'system',
    getInitialValueInEffect: false,
  })

  const [energy, setEnergy] = useLocalStorage<EnergyPreference>({
    key: ENERGY_KEY,
    defaultValue: 'normal',
    getInitialValueInEffect: false,
  })

  const [sidebarCollapsed, setSidebarCollapsed] = useLocalStorage<boolean>({
    key: SIDEBAR_KEY,
    defaultValue: false,
    getInitialValueInEffect: false,
  })

  // 'system' deliberately writes no attribute, leaving the media query in
  // tokens.css in charge. Writing 'full' is what lets a user override the OS
  // setting, which some people want on a machine where it is set globally.
  useEffect(() => {
    const root = document.documentElement

    if (motion === 'system') root.removeAttribute('data-buraq-motion')
    else root.setAttribute('data-buraq-motion', motion)
  }, [motion])

  useEffect(() => {
    const root = document.documentElement

    if (energy === 'low') root.setAttribute('data-buraq-energy', 'low')
    else root.removeAttribute('data-buraq-energy')
  }, [energy])

  const motionReduced = motion === 'reduced' || (motion === 'system' && systemPrefersReduced)

  return {
    motion,
    setMotion,
    motionReduced,
    energy,
    setEnergy,
    lowEnergy: energy === 'low',
    sidebarCollapsed,
    setSidebarCollapsed,
  }
}
