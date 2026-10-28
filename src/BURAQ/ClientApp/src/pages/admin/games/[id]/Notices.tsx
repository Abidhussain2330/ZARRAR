import { Button, Divider, SimpleGrid, Stack, Text } from '@mantine/core'
import { mdiPlus } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { InlineMarkdown } from '@Components/MarkdownRenderer'
import { AdminGameLayout } from '@Components/admin/AdminGameLayout'
import { GameNoticeEditCard } from '@Components/admin/GameNoticeEditCard'
import { GameNoticeEditModal } from '@Components/admin/GameNoticeEditModal'
import { openAdminConfirm, showAdminSuccess } from '@Components/admin/shell'
import { EmptyState, ErrorState, Panel } from '@Components/buraq'
import { showErrorMsg } from '@Utils/Shared'
import { OnceSWRConfig } from '@Hooks/useConfig'
import api, { GameNotice } from '@Api'

const GameNoticeEdit: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1', 10)
  const { data: gameNotices, error, mutate } = api.edit.useEditGetGameNotices(numId, OnceSWRConfig)

  const [isEditModalOpen, setIsEditModalOpen] = useState(false)
  const [activeGameNotice, setActiveGameNotice] = useState<GameNotice | null>(null)

  const { t } = useTranslation()

  const onConfirmDelete = async (gameNotice: GameNotice) => {
    try {
      await api.edit.editDeleteGameNotice(numId, gameNotice.id)
      showAdminSuccess(t('admin.notification.games.notices.deleted'))
      mutate(gameNotices?.filter((notice) => notice.id !== gameNotice.id) ?? [])
    } catch (err) {
      showErrorMsg(err, t)
    }
  }

  const onDeleteGameNotice = (gameNotice: GameNotice) =>
    openAdminConfirm({
      t,
      title: t('admin.button.notices.delete'),
      message: (
        <Stack gap="xs">
          <Text size="sm">{t('admin.content.games.notices.delete')}</Text>
          <Divider />
          <InlineMarkdown source={gameNotice.values.at(-1) || ''} />
        </Stack>
      ),
      onConfirm: () => onConfirmDelete(gameNotice),
    })

  return (
    <AdminGameLayout
      tab="notices"
      isLoading={!gameNotices && !error}
      description={t('admin.nav.notices_description', {
        defaultValue: 'Announcements broadcast to every participant in this event.',
      })}
      actions={
        <Button
          leftSection={<Icon path={mdiPlus} size={0.8} />}
          onClick={() => {
            setActiveGameNotice(null)
            setIsEditModalOpen(true)
          }}
        >
          {t('admin.button.notices.new')}
        </Button>
      }
    >
      {error ? (
        <ErrorState
          message={t('admin.state.load_failed_notices', { defaultValue: 'Could not load announcements.' })}
          onRetry={() => mutate()}
        />
      ) : !gameNotices?.length ? (
        <Panel>
          <EmptyState
            title={t('admin.content.games.notices.empty.title')}
            description={t('admin.content.games.notices.empty.description')}
          />
        </Panel>
      ) : (
        <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md">
          {gameNotices.map((gameNotice) => (
            <GameNoticeEditCard
              key={gameNotice.id}
              gameNotice={gameNotice}
              onDelete={() => onDeleteGameNotice(gameNotice)}
              onEdit={() => {
                setActiveGameNotice(gameNotice)
                setIsEditModalOpen(true)
              }}
            />
          ))}
        </SimpleGrid>
      )}

      <GameNoticeEditModal
        size="min(32rem, 92vw)"
        title={activeGameNotice ? t('admin.button.notices.edit') : t('admin.button.notices.new')}
        opened={isEditModalOpen}
        onClose={() => setIsEditModalOpen(false)}
        gameNotice={activeGameNotice}
        mutateGameNotice={(gameNotice: GameNotice) =>
          mutate([gameNotice, ...(gameNotices?.filter((notice) => notice.id !== gameNotice.id) ?? [])])
        }
      />
    </AdminGameLayout>
  )
}

export default GameNoticeEdit
