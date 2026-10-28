import { Box, Button } from '@mantine/core'
import { useClipboard } from '@mantine/hooks'
import { mdiClose, mdiPlus } from '@mdi/js'
import { Icon } from '@mdi/react'
import { showNotification } from '@mantine/notifications'
import { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useParams } from 'react-router'
import { AdminGameLayout } from '@Components/admin/AdminGameLayout'
import { DivisionCard } from '@Components/admin/DivisionCard'
import { DivisionEditDrawer } from '@Components/admin/DivisionEditDrawer'
import { showAdminSuccess } from '@Components/admin/shell'
import { EmptyState, ErrorState, Panel } from '@Components/buraq'
import { showErrorMsg } from '@Utils/Shared'
import { useEditChallenges } from '@Hooks/useEdit'
import { useAdminDivisions } from '@Hooks/useGame'
import api, { Division } from '@Api'
import classes from '@Styles/Divisions.module.css'

const GameDivisionManagement: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1', 10)

  const { t } = useTranslation()
  const navigate = useNavigate()
  const clipboard = useClipboard({ timeout: 1500 })

  const { divisions, error, mutate: mutateDivisions } = useAdminDivisions(numId)
  const { challenges } = useEditChallenges(numId)

  const [modalOpened, setModalOpened] = useState(false)
  const [activeDivision, setActiveDivision] = useState<Division | null>(null)

  useEffect(() => {
    if (Number.isNaN(numId) || numId < 0) {
      showNotification({
        color: 'red',
        message: t('common.error.param_error'),
        icon: <Icon path={mdiClose} size={1} />,
      })
      navigate('/admin/games')
    }
  }, [navigate, numId, t])

  const challengeTitleMap = useMemo(() => {
    const map = new Map<number, string>()
    challenges?.forEach((challenge) => {
      if (challenge.id) map.set(challenge.id, challenge.title)
    })
    return map
  }, [challenges])

  const sortedDivisions = useMemo(
    () => (divisions ? divisions.toSorted((a, b) => a.name.localeCompare(b.name)) : []),
    [divisions]
  )

  const handleCopyInviteCode = (code: string) => {
    if (!code) return

    try {
      clipboard.copy(code)
      showAdminSuccess(t('admin.notification.games.divisions.invite_copied'))
    } catch {
      showNotification({
        color: 'red',
        message: t('common.error.try_later'),
        icon: <Icon path={mdiClose} size={1} />,
      })
    }
  }

  const handleDivisionSaved = (division: Division) => {
    const current = divisions ?? []
    const index = current.findIndex((item) => item.id === division.id)
    const next =
      index === -1 ? [...current, division] : current.map((item) => (item.id === division.id ? division : item))
    mutateDivisions(next, false)
    setActiveDivision(null)
  }

  /*
   * `DivisionCard` owns the confirmation: it renders `ActionIconWithConfirm`,
   * a popover confirm that also carries the pending state for the request. The
   * page therefore performs the delete and does not add a second dialog on top.
   */
  const handleDeleteDivision = async (division: Division) => {
    try {
      await api.edit.editDeleteDivision(numId, division.id)
      showAdminSuccess(t('admin.notification.games.divisions.deleted'))
      mutateDivisions(
        (divisions ?? []).filter((item) => item.id !== division.id),
        false
      )
    } catch (err) {
      showErrorMsg(err, t)
    }
  }

  return (
    <AdminGameLayout
      tab="divisions"
      isLoading={divisions === undefined && !error}
      description={t('admin.nav.divisions_description', {
        defaultValue: 'Sub-competitions with their own invite codes, challenge sets and scoreboards.',
      })}
      actions={
        <Button
          leftSection={<Icon path={mdiPlus} size={0.8} />}
          onClick={() => {
            setActiveDivision(null)
            setModalOpened(true)
          }}
        >
          {t('admin.button.divisions.new')}
        </Button>
      }
    >
      {error ? (
        <ErrorState
          message={t('admin.state.load_failed_divisions', { defaultValue: 'Could not load divisions.' })}
          onRetry={() => mutateDivisions()}
        />
      ) : sortedDivisions.length === 0 ? (
        <Panel>
          <EmptyState
            title={t('admin.content.games.divisions.empty.title')}
            description={t('admin.content.games.divisions.empty.description')}
          />
        </Panel>
      ) : (
        <Box className={classes.masonryContainer}>
          {sortedDivisions.map((division) => (
            <DivisionCard
              key={division.id}
              division={division}
              challengeTitleMap={challengeTitleMap}
              onEdit={(selected) => {
                setActiveDivision(selected)
                setModalOpened(true)
              }}
              onDelete={handleDeleteDivision}
              onCopyInviteCode={handleCopyInviteCode}
              className={classes.masonryItem}
            />
          ))}
        </Box>
      )}

      <DivisionEditDrawer
        title={activeDivision ? t('admin.button.divisions.edit') : t('admin.button.divisions.new')}
        opened={modalOpened}
        onClose={() => {
          setModalOpened(false)
          setActiveDivision(null)
        }}
        gameId={numId}
        division={activeDivision}
        challenges={challenges}
        onDivisionSaved={handleDivisionSaved}
      />
    </AdminGameLayout>
  )
}

export default GameDivisionManagement
