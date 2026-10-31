import { Box, Code, List, Stack, Text } from '@mantine/core'
import { modals } from '@mantine/modals'
import { ReactNode } from 'react'
import { TFunction } from 'i18next'

export interface AdminConfirmOptions {
  t: TFunction
  title: string
  /** What is about to happen, in the operator's terms. */
  message: ReactNode
  /** The exact subject — a username, a game title, a container id. */
  subject?: string
  /** Extra consequences worth spelling out before an irreversible action. */
  consequences?: ReactNode[]
  confirmLabel?: string
  /** `danger` for anything that destroys data; `caution` for reversible changes. */
  severity?: 'danger' | 'caution' | 'neutral'
  onConfirm: () => void | Promise<void>
}

const SEVERITY_COLOR = {
  danger: 'red',
  caution: 'orange',
  neutral: undefined,
} as const

/**
 * The single confirmation dialog for administrative actions.
 *
 * Every destructive path in the Command Center routes through this so the
 * wording, the colour of the confirm button and the naming of the subject are
 * identical whether the operator is deleting a user, a game, a flag or a
 * running container. The subject is rendered as a `Code` block rather than
 * interpolated into the sentence, so a name containing markup or a lookalike
 * character cannot disguise what is being destroyed.
 */
export const openAdminConfirm = ({
  t,
  title,
  message,
  subject,
  consequences,
  confirmLabel,
  severity = 'danger',
  onConfirm,
}: AdminConfirmOptions) =>
  modals.openConfirmModal({
    title,
    centered: true,
    children: (
      <Stack gap="sm">
        {typeof message === 'string' || typeof message === 'number' ? (
          <Text size="sm">{message}</Text>
        ) : (
          // Rich callers may supply Stack/InlineMarkdown content containing
          // block elements. A neutral div avoids invalid <p><div|p> nesting.
          <Box>{message}</Box>
        )}
        {subject && (
          <Code block style={{ overflowWrap: 'anywhere' }}>
            {subject}
          </Code>
        )}
        {consequences && consequences.length > 0 && (
          <List size="sm" spacing={4}>
            {consequences.map((item, index) => (
              <List.Item key={index}>{item}</List.Item>
            ))}
          </List>
        )}
        {severity === 'danger' && (
          <Text size="xs" c="dimmed">
            {t('admin.confirm.irreversible', { defaultValue: 'This action cannot be undone.' })}
          </Text>
        )}
      </Stack>
    ),
    labels: {
      confirm: confirmLabel ?? t('common.modal.confirm'),
      cancel: t('common.modal.cancel'),
    },
    confirmProps: { color: SEVERITY_COLOR[severity] },
    onConfirm,
  })
