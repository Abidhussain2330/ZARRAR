import { useCallback, useEffect, useRef } from 'react'
import useSWR from 'swr'
import api, { MotionPreference as ServerMotionPreference, UserPreferencesModel } from '@Api'
import { MotionPreference, useZarrarPreferences } from '@Hooks/zarrar/useZarrarPreferences'

const toLocalMotion = (motion: ServerMotionPreference): MotionPreference => {
  switch (motion) {
    case ServerMotionPreference.Full:
      return 'full'
    case ServerMotionPreference.Reduced:
      return 'reduced'
    default:
      return 'system'
  }
}

const signatureFor = (preferences: UserPreferencesModel): string =>
  `${preferences.motion}|${preferences.lowEnergyMode}|${preferences.sidebarCollapsed}`

/**
 * Reconciles the authenticated user's server preferences with the shell's
 * presentation-only local cache.
 *
 * The server is applied after the first successful load and after an explicit
 * save. There is deliberately no bidirectional autosave effect: keeping a
 * server response and an editable draft separate prevents feedback loops and
 * keeps failed saves from changing the live interface.
 */
export const useSyncedPreferences = (userId?: string) => {
  const local = useZarrarPreferences()
  const appliedRef = useRef<string | null>(null)
  const {
    data: preferences,
    error,
    isLoading,
    mutate,
  } = useSWR<UserPreferencesModel>(
    userId ? ['zarrar-user-preferences', userId] : null,
    async () => {
      const response = await api.dashboard.dashboardGetPreferences()
      return response.data
    },
    {
      refreshInterval: 0,
      revalidateOnFocus: false,
      shouldRetryOnError: false,
    }
  )

  const applyToShell = useCallback(
    (next: UserPreferencesModel, force = false) => {
      if (!userId) return

      const appliedKey = `${userId}|${signatureFor(next)}`
      if (!force && appliedRef.current === appliedKey) return
      appliedRef.current = appliedKey

      const motion = toLocalMotion(next.motion)
      const energy = next.lowEnergyMode ? 'low' : 'normal'

      if (local.motion !== motion) local.setMotion(motion)
      if (local.energy !== energy) local.setEnergy(energy)
      if (local.sidebarCollapsed !== next.sidebarCollapsed) local.setSidebarCollapsed(next.sidebarCollapsed)
    },
    [
      local.energy,
      local.motion,
      local.setEnergy,
      local.setMotion,
      local.setSidebarCollapsed,
      local.sidebarCollapsed,
      userId,
    ]
  )

  useEffect(() => {
    if (!userId) {
      appliedRef.current = null
      return
    }

    if (preferences) applyToShell(preferences)
  }, [applyToShell, preferences, userId])

  const savePreferences = useCallback(
    async (next: UserPreferencesModel): Promise<UserPreferencesModel> => {
      const response = await api.dashboard.dashboardUpdatePreferences(next)
      const saved = response.data
      await mutate(saved, { revalidate: false })
      applyToShell(saved, true)
      return saved
    },
    [applyToShell, mutate]
  )

  return {
    preferences,
    error,
    isLoading,
    reload: () => mutate(),
    savePreferences,
  }
}
