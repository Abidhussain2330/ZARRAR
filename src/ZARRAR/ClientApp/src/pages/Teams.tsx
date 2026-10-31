import { Modal } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiCheck } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, FormEvent, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { PlayerTeamCard } from '@Components/player/PlayerTeamCard'
import { TeamCreateModal } from '@Components/TeamCreateModal'
import { TeamEditModal } from '@Components/TeamEditModal'
import { WithRole } from '@Components/WithRole'
import { Empty, ErrorState, Loading, PageHead, arena } from '@Components/player/Arena'
import { PlayerShell } from '@Components/player/PlayerShell'
import { tryGetErrorMsg } from '@Utils/Shared'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useTeams, useUser } from '@Hooks/useUser'
import api, { Role, TeamInfoModel } from '@Api'
import classes from '@Styles/player/Teams.module.css'

/** Invite codes end in `:<teamId>:<32 hex>`; checked here so a typo fails instantly. */
const INVITE_CODE_PATTERN = /:\d+:[0-9a-f]{32}$/i
const MAX_CAPTAINED_TEAMS = 3

const Teams: FC = () => {
  const { user, error: userError, mutate: mutateUser } = useUser()
  const { teams, mutate: mutateTeams, error: teamsError } = useTeams()
  const { t } = useTranslation()

  const [joinOpened, setJoinOpened] = useState(false)
  const [joinTeamCode, setJoinTeamCode] = useState('')
  const [joinPending, setJoinPending] = useState(false)
  const [joinError, setJoinError] = useState<string | null>(null)

  const [createOpened, setCreateOpened] = useState(false)
  const [editOpened, setEditOpened] = useState(false)
  const [editTeam, setEditTeam] = useState<TeamInfoModel | null>(null)

  usePageTitle(t('team.title.index'))

  const captainedTeams = useMemo(
    () => teams?.filter((team) => team.members?.some((m) => m.captain && m.id === user?.userId)) ?? [],
    [teams, user?.userId]
  )
  const disallowCreate = captainedTeams.length >= MAX_CAPTAINED_TEAMS

  // Keep the open edit modal in step with refreshed team data.
  useEffect(() => {
    if (!editTeam?.id || !teams) return
    const updated = teams.find((team) => team.id === editTeam.id)
    if (updated) setEditTeam(updated)
  }, [editTeam?.id, teams])

  const closeJoin = () => {
    if (joinPending) return
    setJoinTeamCode('')
    setJoinError(null)
    setJoinOpened(false)
  }

  const onJoinTeam = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const code = joinTeamCode.trim()

    if (!INVITE_CODE_PATTERN.test(code)) {
      setJoinError(t('team.notification.join.wrong_invite_code'))
      return
    }

    setJoinPending(true)
    setJoinError(null)

    try {
      await api.team.teamAccept(code)
      await mutateTeams()
      showNotification({
        color: 'teal',
        title: t('team.notification.join.success'),
        message: t('team.notification.updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      setJoinTeamCode('')
      setJoinOpened(false)
    } catch (error) {
      setJoinError(tryGetErrorMsg(error, t))
    } finally {
      setJoinPending(false)
    }
  }

  const onTeamChanged = (updated: TeamInfoModel | null) => {
    if (!updated) {
      setEditOpened(false)
      setEditTeam(null)
      return
    }
    setEditTeam(updated)
  }

  const retry = async () => {
    await Promise.all([mutateUser(), mutateTeams()])
  }

  const pageError = userError ?? teamsError

  const actions = (
    <>
      <button type="button" className={`${arena.btn} ${arena.btnGhost} bqFocusRing`} onClick={() => setJoinOpened(true)}>
        {t('team.button.join')}
      </button>
      <button
        type="button"
        className={`${arena.btn} ${arena.btnPrimary} bqFocusRing`}
        onClick={() => setCreateOpened(true)}
      >
        {t('team.button.create')}
      </button>
    </>
  )

  return (
    <PlayerShell>
      <WithRole requiredRole={Role.User}>
        <PageHead
          eyebrow="ZARRAR / ROSTER"
          title="Your teams"
          subtitle="You compete as a team. Create one, or join with a captain's invite code."
          actions={actions}
        />

        {user && teams && (
          <div className={classes.stats}>
            <div className={classes.stat}>
              <span className={classes.statLabel}>Memberships</span>
              <strong className={classes.statValue}>{teams.length}</strong>
              <span className={classes.statNote}>Teams you belong to</span>
            </div>
            <div className={classes.stat}>
              <span className={classes.statLabel}>Captain of</span>
              <strong className={classes.statValue}>{captainedTeams.length}</strong>
              <span className={classes.statNote}>Teams you own</span>
            </div>
            <div className={classes.stat}>
              <span className={classes.statLabel}>Can still create</span>
              <strong className={classes.statValue}>
                {Math.max(MAX_CAPTAINED_TEAMS - captainedTeams.length, 0)}
              </strong>
              <span className={classes.statNote}>Out of {MAX_CAPTAINED_TEAMS} allowed</span>
            </div>
          </div>
        )}

        {!user || !teams ? (
          pageError ? (
            <ErrorState
              title="Teams unavailable"
              body={tryGetErrorMsg(pageError, t)}
              action={
                <button type="button" className={`${arena.btn} ${arena.btnGhost} bqFocusRing`} onClick={retry}>
                  Retry
                </button>
              }
            />
          ) : (
            <Loading label="Loading your teams…" />
          )
        ) : teams.length === 0 ? (
          <Empty
            title={t('team.content.no_team.title')}
            body="Create a team to compete, or paste an invite code from a captain to join theirs."
            action={<div className={classes.emptyActions}>{actions}</div>}
          />
        ) : (
          <div className={classes.grid}>
            {teams.map((team) => (
              <PlayerTeamCard
                key={team.id}
                team={team}
                isCaptain={team.members?.some((m) => m.captain && m.id === user.userId) ?? false}
                onEdit={() => {
                  setEditTeam(team)
                  setEditOpened(true)
                }}
              />
            ))}
          </div>
        )}

        <Modal opened={joinOpened} title={t('team.button.join')} onClose={closeJoin} centered>
          <form className={classes.joinForm} onSubmit={onJoinTeam}>
            <p className={classes.joinIntro}>
              Paste the invite code a team captain gave you. Each code works for one team.
            </p>

            <label className={classes.joinLabel} htmlFor="invite-code">
              {t('team.label.invite_code')}
            </label>
            <input
              id="invite-code"
              className={`${classes.joinInput} bqFocusRing`}
              required
              autoComplete="off"
              spellCheck={false}
              value={joinTeamCode}
              disabled={joinPending}
              aria-invalid={joinError ? true : undefined}
              onChange={(e) => {
                setJoinError(null)
                setJoinTeamCode(e.currentTarget.value)
              }}
            />

            {joinError && (
              <p className={classes.joinError} role="alert">
                {joinError}
              </p>
            )}

            <div className={classes.joinActions}>
              <button
                type="button"
                className={`${arena.btn} ${arena.btnGhost} bqFocusRing`}
                disabled={joinPending}
                onClick={closeJoin}
              >
                {t('common.modal.cancel')}
              </button>
              <button
                type="submit"
                className={`${arena.btn} ${arena.btnPrimary} bqFocusRing`}
                disabled={!joinTeamCode.trim() || joinPending}
              >
                {joinPending ? 'JOINING…' : t('team.button.join')}
              </button>
            </div>
          </form>
        </Modal>

        <TeamCreateModal
          opened={createOpened}
          title={t('team.button.create')}
          disallowCreate={disallowCreate}
          onClose={() => setCreateOpened(false)}
          mutate={mutateTeams}
        />

        <TeamEditModal
          opened={editOpened}
          title={t('team.button.edit')}
          onClose={() => setEditOpened(false)}
          team={editTeam}
          currentUserId={user?.userId}
          onTeamChanged={onTeamChanged}
        />
      </WithRole>
    </PlayerShell>
  )
}

export default Teams
