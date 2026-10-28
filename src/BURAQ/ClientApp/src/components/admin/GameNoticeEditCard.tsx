import { ActionIcon, Group, Stack, Text } from '@mantine/core'
import { mdiDeleteOutline, mdiPencilOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC } from 'react'
import { useTranslation } from 'react-i18next'
import { InlineMarkdown } from '@Components/MarkdownRenderer'
import { useLanguage } from '@Utils/I18n'
import { GameNotice } from '@Api'
import classes from '@Styles/admin/AdminMiscCards.module.css'

interface GameNoticeEditCardProps {
  gameNotice: GameNotice
  onDelete: () => void
  onEdit: () => void
  className?: string
}

export const GameNoticeEditCard: FC<GameNoticeEditCardProps> = ({ gameNotice, onDelete, onEdit, className }) => {
  const { locale } = useLanguage()
  const { t } = useTranslation()

  return (
    <div className={`${classes.cardTight} ${className ?? ''}`}>
      <Group justify="space-between" wrap="nowrap">
        <Stack gap={1}>
          <InlineMarkdown source={gameNotice.values.at(-1) || ''} />
          <Text size="xs" fw="bold" className={classes.meta}>
            {dayjs(gameNotice.time).locale(locale).format('#SLL LTS')}
          </Text>
        </Stack>
        <Group justify="right" wrap="nowrap">
          <ActionIcon
            variant="subtle"
            color="blue"
            aria-label={t('admin.button.notices.edit')}
            onClick={onEdit}
          >
            <Icon path={mdiPencilOutline} size={0.85} />
          </ActionIcon>
          <ActionIcon
            variant="subtle"
            color="red"
            aria-label={t('admin.button.notices.delete')}
            onClick={onDelete}
          >
            <Icon path={mdiDeleteOutline} size={0.85} />
          </ActionIcon>
        </Group>
      </Group>
    </div>
  )
}
