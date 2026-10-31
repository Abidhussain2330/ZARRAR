import { Button, ComboboxItem, Select, SimpleGrid } from '@mantine/core'
import { mdiHexagonSlice6, mdiPlus, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import { Dispatch, FC, SetStateAction, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { AdminGameLayout } from '@Components/admin/AdminGameLayout'
import { BloodBonusModel } from '@Components/admin/BloodBonusModel'
import { ChallengeCreateModal } from '@Components/admin/ChallengeCreateModal'
import { ChallengeEditCard } from '@Components/admin/ChallengeEditCard'
import {
  AdminResultCount,
  AdminSearch,
  AdminToolbar,
  openAdminConfirm,
  showAdminSuccess,
} from '@Components/admin/shell'
import { EmptyState, ErrorState, LoadingState, Panel } from '@Components/zarrar'
import { ChallengeCategoryItem, ChallengeCategoryList, showErrorMsg, useChallengeCategoryLabelMap } from '@Utils/Shared'
import { useEditChallenges } from '@Hooks/useEdit'
import api, { ChallengeInfoModel } from '@Api'
import classes from '@Styles/admin/AdminControls.module.css'

const GameChallengeList: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1', 10)

  const [createOpened, setCreateOpened] = useState(false)
  const [bonusOpened, setBonusOpened] = useState(false)
  const [category, setCategory] = useState<string | null>(null)
  const [query, setQuery] = useState('')
  const [busy, setBusy] = useState(false)

  const challengeCategoryLabelMap = useChallengeCategoryLabelMap()
  const { t } = useTranslation()
  const { challenges, error, mutate } = useEditChallenges(numId)

  const visible = useMemo(() => {
    const needle = query.trim().toLowerCase()

    return (challenges ?? []).filter((challenge) => {
      if (category && challenge.category !== category) return false
      return !needle || challenge.title.toLowerCase().includes(needle)
    })
  }, [challenges, category, query])

  const onConfirmToggle = async (challenge: ChallengeInfoModel, setDisabled: Dispatch<SetStateAction<boolean>>) => {
    setDisabled(true)

    try {
      await api.edit.editUpdateGameChallenge(numId, challenge.id!, { isEnabled: !challenge.isEnabled })
      showAdminSuccess(t('admin.notification.games.challenges.updated'))
      mutate(challenges?.map((c) => (c.id === challenge.id ? { ...c, isEnabled: !challenge.isEnabled } : c)))
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setDisabled(false)
    }
  }

  const onToggle = (challenge: ChallengeInfoModel, setDisabled: Dispatch<SetStateAction<boolean>>) =>
    openAdminConfirm({
      t,
      severity: 'caution',
      title: challenge.isEnabled ? t('admin.button.challenges.disable') : t('admin.button.challenges.enable'),
      message: challenge.isEnabled
        ? t('admin.content.games.challenges.disable', { name: challenge.title })
        : t('admin.content.games.challenges.enable', { name: challenge.title }),
      subject: challenge.title,
      onConfirm: () => onConfirmToggle(challenge, setDisabled),
    })

  const onFlushScoreboard = async () => {
    setBusy(true)

    try {
      await api.edit.editFlushScoreboardCache(numId)
      showAdminSuccess(t('admin.notification.games.info.scoreboard_flushed'))
      mutate()
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setBusy(false)
    }
  }

  const isFiltered = Boolean(category || query.trim())

  return (
    <AdminGameLayout
      tab="challenges"
      description={t('admin.nav.challenges_description', {
        defaultValue: 'Authoring, scoring, attachments, flags and container settings.',
      })}
      actions={
        <>
          <Button
            variant="default"
            disabled={busy}
            leftSection={<Icon path={mdiRefresh} size={0.8} />}
            onClick={() =>
              openAdminConfirm({
                t,
                severity: 'caution',
                title: t('admin.button.challenges.flush_scoreboard'),
                message: t('admin.content.games.challenges.flush_scoreboard', {
                  defaultValue: 'Recompute the cached scoreboard for this event now?',
                }),
                onConfirm: onFlushScoreboard,
              })
            }
          >
            {t('admin.button.challenges.flush_scoreboard')}
          </Button>
          <Button
            variant="default"
            leftSection={<Icon path={mdiHexagonSlice6} size={0.8} />}
            onClick={() => setBonusOpened(true)}
          >
            {t('admin.button.challenges.bonus')}
          </Button>
          <Button leftSection={<Icon path={mdiPlus} size={0.8} />} onClick={() => setCreateOpened(true)}>
            {t('admin.button.challenges.new')}
          </Button>
        </>
      }
    >
      <AdminToolbar
        filters={
          <Select
            className={classes.filter}
            clearable
            searchable
            value={category}
            onChange={setCategory}
            aria-label={t('admin.content.games.challenges.category')}
            placeholder={t('admin.content.show_all')}
            nothingFoundMessage={t('admin.content.nothing_found')}
            renderOption={ChallengeCategoryItem}
            data={ChallengeCategoryList.map((item) => {
              const meta = challengeCategoryLabelMap.get(item)
              return { value: item, label: meta?.name, ...meta } as ComboboxItem
            })}
          />
        }
        trailing={
          <AdminResultCount shown={visible.length} total={challenges?.length} filtered={isFiltered} />
        }
      >
        <AdminSearch
          value={query}
          onChange={setQuery}
          placeholder={t('admin.placeholder.challenges.search', { defaultValue: 'Filter by title' })}
        />
      </AdminToolbar>

      {error ? (
        <ErrorState
          message={t('admin.state.load_failed_challenges', { defaultValue: 'Could not load challenges.' })}
          onRetry={() => mutate()}
        />
      ) : !challenges ? (
        <Panel>
          <LoadingState variant="card-grid" label={t('admin.tab.games.challenges')} />
        </Panel>
      ) : visible.length === 0 ? (
        <Panel>
          <EmptyState
            title={
              isFiltered
                ? t('admin.state.no_results', { defaultValue: 'No matching challenges' })
                : t('admin.content.games.challenges.empty.title')
            }
            description={
              isFiltered
                ? t('admin.state.no_results_hint', { defaultValue: 'Adjust the search or clear the filters.' })
                : t('admin.content.games.challenges.empty.description')
            }
          />
        </Panel>
      ) : (
        <SimpleGrid cols={{ base: 1, xs: 2, md: 3, lg: 4, xl: 5 }} spacing="sm">
          {visible.map((challenge) => (
            <ChallengeEditCard key={challenge.id} challenge={challenge} onToggle={onToggle} />
          ))}
        </SimpleGrid>
      )}

      <ChallengeCreateModal
        title={t('admin.button.challenges.new')}
        size="min(30rem, 92vw)"
        opened={createOpened}
        onClose={() => setCreateOpened(false)}
        onAddChallenge={(challenge) => mutate([challenge, ...(challenges ?? [])])}
      />
      <BloodBonusModel
        title={t('admin.button.challenges.bonus')}
        size="min(30rem, 92vw)"
        opened={bonusOpened}
        onClose={() => setBonusOpened(false)}
      />
    </AdminGameLayout>
  )
}

export default GameChallengeList
