import { showNotification } from '@mantine/notifications'
import { mdiCheck } from '@mdi/js'
import { Icon } from '@mdi/react'

/**
 * Success toast for a completed administrative action.
 *
 * A thin wrapper, but it is the reason every create/update/delete across the
 * Command Center confirms itself the same way — the previous screens each
 * hand-rolled `showNotification({ color: 'teal', icon: <Icon .../> })` and a
 * few simply did not confirm at all. Failure paths stay on `showErrorMsg`,
 * which already normalises axios and rate-limit errors.
 */
export const showAdminSuccess = (message: string, title?: string) =>
  showNotification({
    color: 'teal',
    title,
    message,
    icon: <Icon path={mdiCheck} size={1} />,
  })
