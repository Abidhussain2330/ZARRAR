import { useCallback, useMemo, useState } from 'react'

export interface AdminSelection<K> {
  selected: K[]
  count: number
  isSelected: (key: K) => boolean
  toggle: (key: K) => void
  /** Selects every key in `pageKeys`, or clears them all if they are already selected. */
  toggleAll: (pageKeys: K[]) => void
  allSelected: (pageKeys: K[]) => boolean
  someSelected: (pageKeys: K[]) => boolean
  clear: () => void
}

/**
 * Bulk-selection state for an admin table.
 *
 * Selection is keyed rather than index-based so it survives a refetch that
 * reorders rows — otherwise "delete selected" after a background revalidation
 * would act on whatever happens to sit at those indices now.
 */
export const useAdminSelection = <K,>(): AdminSelection<K> => {
  const [selected, setSelected] = useState<K[]>([])

  const isSelected = useCallback((key: K) => selected.includes(key), [selected])

  const toggle = useCallback((key: K) => {
    setSelected((current) => (current.includes(key) ? current.filter((item) => item !== key) : [...current, key]))
  }, [])

  const allSelected = useCallback(
    (pageKeys: K[]) => pageKeys.length > 0 && pageKeys.every((key) => selected.includes(key)),
    [selected]
  )

  const someSelected = useCallback(
    (pageKeys: K[]) => pageKeys.some((key) => selected.includes(key)) && !allSelected(pageKeys),
    [selected, allSelected]
  )

  const toggleAll = useCallback(
    (pageKeys: K[]) => {
      setSelected((current) => {
        const everySelected = pageKeys.length > 0 && pageKeys.every((key) => current.includes(key))
        if (everySelected) return current.filter((key) => !pageKeys.includes(key))

        const next = [...current]
        for (const key of pageKeys) if (!next.includes(key)) next.push(key)
        return next
      })
    },
    []
  )

  const clear = useCallback(() => setSelected([]), [])

  return useMemo(
    () => ({
      selected,
      count: selected.length,
      isSelected,
      toggle,
      toggleAll,
      allSelected,
      someSelected,
      clear,
    }),
    [selected, isSelected, toggle, toggleAll, allSelected, someSelected, clear]
  )
}
