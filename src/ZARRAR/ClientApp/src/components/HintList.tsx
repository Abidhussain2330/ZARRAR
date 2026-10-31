import {
  ActionIcon,
  Button,
  Group,
  Input,
  InputWrapperProps,
  NumberInput,
  ScrollArea,
  Stack,
  TextInput,
} from '@mantine/core'
import { mdiClose, mdiPlus } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC } from 'react'
import { useTranslation } from 'react-i18next'

interface HintListProps extends InputWrapperProps {
  hints: string[]
  penalties?: number[]
  onChangeHint: (hints: string[], penalties: number[]) => void
  disabled?: boolean
  height?: number
}

export const HintList: FC<HintListProps> = (props) => {
  const { hints, penalties = [], onChangeHint, disabled, height, ...rest } = props

  const { t } = useTranslation()

  const hintdict = hints.map((hint, key) => ({ hint, penalty: penalties[key] ?? 0, key }))

  const handleChange = (key: number, value: string) => {
    const newHints = [...hintdict]
    newHints[key].hint = value
    onChangeHint(
      newHints.map((h) => h.hint),
      newHints.map((h) => h.penalty)
    )
  }

  const handlePenaltyChange = (key: number, value: string | number) => {
    const penalty = typeof value === 'number' ? value : Number(value)
    if (!Number.isFinite(penalty) || penalty < 0) return

    const newHints = [...hintdict]
    newHints[key].penalty = penalty
    onChangeHint(
      newHints.map((h) => h.hint),
      newHints.map((h) => h.penalty)
    )
  }

  const handleAdd = () => {
    const newHints = [...hintdict, { hint: '', penalty: 0, key: hintdict.length }]
    onChangeHint(
      newHints.map((h) => h.hint),
      newHints.map((h) => h.penalty)
    )
  }

  const handleDelete = (key: number) => {
    const newHints = hintdict.filter((h) => h.key !== key)
    onChangeHint(
      newHints.map((h) => h.hint),
      newHints.map((h) => h.penalty)
    )
  }

  return (
    <Input.Wrapper {...rest}>
      <ScrollArea offsetScrollbars scrollbarSize={4} h={height}>
        <Stack gap="xs">
          {hintdict.map((kv) => (
            <Group key={kv.key} align="flex-end" wrap="nowrap">
              <TextInput
                flex={1}
                value={kv.hint}
                disabled={disabled}
                onChange={(e) => handleChange(kv.key, e.target.value)}
                rightSection={
                  <ActionIcon
                    aria-label={t('admin.button.challenges.hint.delete', { defaultValue: 'Delete hint' })}
                    disabled={disabled}
                    onClick={() => handleDelete(kv.key)}
                  >
                    <Icon path={mdiClose} size={1} />
                  </ActionIcon>
                }
              />
              <NumberInput
                w={132}
                min={0}
                step={5}
                label={t('admin.content.games.challenges.hint_penalty', { defaultValue: 'Point cost' })}
                description={t('admin.content.games.challenges.hint_penalty_hint', {
                  defaultValue: '0 keeps it free',
                })}
                value={kv.penalty}
                disabled={disabled}
                onChange={(value) => handlePenaltyChange(kv.key, value)}
              />
            </Group>
          ))}
          <Button mr={4} disabled={disabled} leftSection={<Icon path={mdiPlus} size={1} />} onClick={handleAdd}>
            {t('admin.button.challenges.hint.add')}
          </Button>
        </Stack>
      </ScrollArea>
    </Input.Wrapper>
  )
}
