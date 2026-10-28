import {
  ActionIcon,
  Avatar,
  Badge,
  Button,
  Group,
  Image,
  Modal,
  ModalProps,
  ScrollArea,
  Stack,
  Text,
  Textarea,
  TextInput,
  Tooltip,
} from '@mantine/core'
import { Dropzone } from '@mantine/dropzone'
import { useClipboard } from '@mantine/hooks'
import { useModals } from '@mantine/modals'
import { showNotification } from '@mantine/notifications'
import {
  mdiAccountArrowRightOutline,
  mdiAccountGroupOutline,
  mdiAccountRemoveOutline,
  mdiAlertCircleOutline,
  mdiCameraOutline,
  mdiCheck,
  mdiCloudUploadOutline,
  mdiContentCopy,
  mdiCrown,
  mdiDeleteOutline,
  mdiExitToApp,
  mdiLockOutline,
  mdiRefresh,
  mdiShieldAccountOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, FormEvent, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { EmptyState, ErrorState, LoadingState } from '@Components/buraq'
import { IMAGE_MIME_TYPES, tryGetErrorMsg } from '@Utils/Shared'
import api, { TeamInfoModel, TeamUserInfoModel } from '@Api'
import styles from '@Styles/components/TeamEditModal.module.css'

type TeamAction = 'save' | 'invite' | 'avatar' | 'transfer' | 'kick' | 'leave' | 'delete'

interface TeamEditModalProps extends Omit<ModalProps, 'children'> {
  team: TeamInfoModel | null
  currentUserId?: string
  onTeamChanged?: (team: TeamInfoModel | null) => void
}

interface TeamMemberRowProps {
  member: TeamUserInfoModel
  currentUserId?: string
  canManage: boolean
  locked: boolean
  pending: boolean
  onTransfer: (member: TeamUserInfoModel) => void
  onKick: (member: TeamUserInfoModel) => void
}

const TeamMemberRow: FC<TeamMemberRowProps> = ({
  member,
  currentUserId,
  canManage,
  locked,
  pending,
  onTransfer,
  onKick,
}) => {
  const memberName = member.userName || 'Unnamed member'

  return (
    <li className={styles.memberRow}>
      <Avatar className={styles.memberAvatar} size={40} radius="md" src={member.avatar}>
        {memberName.slice(0, 1).toUpperCase()}
      </Avatar>
      <div className={styles.memberIdentity}>
        <strong title={memberName}>{memberName}</strong>
        <span>
          {member.captain ? 'Captain' : 'Member'}
          {member.id === currentUserId ? ' · You' : ''}
        </span>
      </div>
      {member.captain ? (
        <Badge
          className={styles.captainBadge}
          variant="outline"
          leftSection={<Icon path={mdiCrown} size={0.58} />}
        >
          Captain
        </Badge>
      ) : canManage ? (
        <div className={styles.memberActions}>
          <Tooltip label="Transfer captain role" withArrow>
            <ActionIcon
              variant="light"
              color="violet"
              aria-label={`Transfer captain role to ${memberName}`}
              disabled={pending}
              onClick={() => onTransfer(member)}
            >
              <Icon path={mdiAccountArrowRightOutline} size={0.82} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label={locked ? 'Member removal is unavailable while the team is locked' : 'Remove member'} withArrow>
            <ActionIcon
              variant="light"
              color="red"
              aria-label={`Remove ${memberName} from the team`}
              disabled={pending || locked}
              onClick={() => onKick(member)}
            >
              <Icon path={mdiAccountRemoveOutline} size={0.82} />
            </ActionIcon>
          </Tooltip>
        </div>
      ) : (
        <span className={styles.memberRole}>Member</span>
      )}
    </li>
  )
}

export const TeamEditModal: FC<TeamEditModalProps> = ({
  team,
  currentUserId,
  onTeamChanged,
  onClose,
  ...modalProps
}) => {
  const { t } = useTranslation()
  const clipboard = useClipboard({ timeout: 1800 })
  const modals = useModals()
  const { data: teams, mutate: mutateTeams } = api.team.useTeamGetTeamsInfo()

  const [teamInfo, setTeamInfo] = useState<TeamInfoModel | null>(team)
  const [baseline, setBaseline] = useState<TeamInfoModel | null>(team)
  const [action, setAction] = useState<TeamAction | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  const [inviteCode, setInviteCode] = useState('')
  const [inviteLoading, setInviteLoading] = useState(false)
  const [inviteError, setInviteError] = useState<string | null>(null)

  const [avatarModalOpened, setAvatarModalOpened] = useState(false)
  const [avatarFile, setAvatarFile] = useState<File | null>(null)
  const [avatarPreview, setAvatarPreview] = useState<string | null>(null)
  const [avatarError, setAvatarError] = useState<string | null>(null)

  const teamId = teamInfo?.id
  const captain = useMemo(() => teamInfo?.members?.find((member) => member.captain), [teamInfo?.members])
  const isCaptain = Boolean(currentUserId && captain?.id === currentUserId)
  const isLocked = Boolean(teamInfo?.locked)
  const isPending = action !== null

  const hasTeamChanges = useMemo(
    () =>
      Boolean(
        teamInfo &&
          baseline &&
          ((teamInfo.name ?? '') !== (baseline.name ?? '') || (teamInfo.bio ?? '') !== (baseline.bio ?? '')),
      ),
    [baseline, teamInfo],
  )

  useEffect(() => {
    setTeamInfo(team)
    setBaseline(team)
    setAction(null)
    setActionError(null)
    setInviteCode('')
    setInviteError(null)
    setAvatarFile(null)
    setAvatarError(null)
  }, [team])

  useEffect(() => {
    if (!avatarFile) {
      setAvatarPreview(null)
      return
    }

    const preview = URL.createObjectURL(avatarFile)
    setAvatarPreview(preview)
    return () => URL.revokeObjectURL(preview)
  }, [avatarFile])

  useEffect(() => {
    let active = true
    setInviteCode('')
    setInviteError(null)

    if (!isCaptain || !teamId) {
      setInviteLoading(false)
      return () => {
        active = false
      }
    }

    setInviteLoading(true)
    api.team
      .teamInviteCode(teamId)
      .then((response) => {
        if (active) setInviteCode(response.data)
      })
      .catch((error) => {
        if (active) setInviteError(tryGetErrorMsg(error, t))
      })
      .finally(() => {
        if (active) setInviteLoading(false)
      })

    return () => {
      active = false
    }
  }, [isCaptain, teamId, t])

  const applyTeam = async (updated: TeamInfoModel) => {
    setTeamInfo(updated)
    setBaseline(updated)
    onTeamChanged?.(updated)

    if (teams) {
      await mutateTeams(
        teams.map((candidate) => (candidate.id === updated.id ? updated : candidate)),
        { revalidate: false },
      )
    } else {
      await mutateTeams()
    }
  }

  const removeTeamFromCache = async (id: number) => {
    onTeamChanged?.(null)

    if (teams) {
      await mutateTeams(
        teams.filter((candidate) => candidate.id !== id),
        { revalidate: false },
      )
    } else {
      await mutateTeams()
    }
  }

  const close = () => {
    if (isPending) return
    setAvatarModalOpened(false)
    setAvatarFile(null)
    setAvatarError(null)
    onClose()
  }

  const closeAvatarModal = () => {
    if (action === 'avatar') return
    setAvatarFile(null)
    setAvatarError(null)
    setAvatarModalOpened(false)
  }

  const updateTeamField = (field: 'name' | 'bio', value: string) => {
    setTeamInfo((current) => (current ? { ...current, [field]: value } : current))
  }

  const saveTeam = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!teamInfo?.id || !isCaptain || !hasTeamChanges) return

    const name = teamInfo.name?.trim() ?? ''
    if (!name) {
      setActionError('Team name cannot be empty.')
      return
    }

    setAction('save')
    setActionError(null)

    try {
      const response = await api.team.teamUpdateTeam(teamInfo.id, { name, bio: teamInfo.bio?.trim() ?? '' })
      await applyTeam(response.data)
      showNotification({
        color: 'teal',
        message: t('team.notification.updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (error) {
      setActionError(tryGetErrorMsg(error, t))
    } finally {
      setAction(null)
    }
  }

  const refreshInviteCode = async () => {
    if (!teamId || !isCaptain) return

    setAction('invite')
    setInviteError(null)

    try {
      const response = await api.team.teamUpdateInviteToken(teamId)
      setInviteCode(response.data)
      showNotification({
        color: 'teal',
        message: t('team.notification.invite_code.updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (error) {
      setInviteError(tryGetErrorMsg(error, t))
    } finally {
      setAction(null)
    }
  }

  const confirmRefreshInviteCode = () => {
    modals.openConfirmModal({
      title: 'Regenerate invitation code?',
      children: <Text size="sm">The current code will stop working immediately.</Text>,
      labels: { confirm: 'Regenerate code', cancel: t('common.modal.cancel') },
      confirmProps: { color: 'orange' },
      onConfirm: refreshInviteCode,
    })
  }

  const copyInviteCode = () => {
    if (!inviteCode) return
    clipboard.copy(inviteCode)
    showNotification({
      color: 'teal',
      message: t('team.notification.invite_code.copied'),
      icon: <Icon path={mdiCheck} size={1} />,
    })
  }

  const uploadAvatar = async () => {
    if (!avatarFile || !teamId || !isCaptain) {
      setAvatarError('Select an image before saving.')
      return
    }

    setAction('avatar')
    setAvatarError(null)

    try {
      const response = await api.team.teamAvatar(teamId, { file: avatarFile })
      const updated = { ...teamInfo, avatar: response.data }
      await applyTeam(updated)
      showNotification({
        color: 'teal',
        message: t('common.avatar.uploaded'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      setAvatarFile(null)
      setAvatarModalOpened(false)
    } catch (error) {
      setAvatarError(tryGetErrorMsg(error, t))
    } finally {
      setAction(null)
    }
  }

  const transferCaptain = async (member: TeamUserInfoModel) => {
    if (!teamId || !isCaptain || !member.id) return

    setAction('transfer')
    setActionError(null)

    try {
      const response = await api.team.teamTransfer(teamId, { newCaptainId: member.id })
      await applyTeam(response.data)
      showNotification({
        color: 'teal',
        title: t('team.notification.transfer.success'),
        message: t('team.notification.updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (error) {
      setActionError(tryGetErrorMsg(error, t))
    } finally {
      setAction(null)
    }
  }

  const confirmTransferCaptain = (member: TeamUserInfoModel) => {
    modals.openConfirmModal({
      title: t('team.content.transfer.confirm.title'),
      children: (
        <Text size="sm">
          {t('team.content.transfer.confirm.message', { team: teamInfo?.name, user: member.userName })}
        </Text>
      ),
      labels: { confirm: 'Transfer captain', cancel: t('common.modal.cancel') },
      confirmProps: { color: 'orange' },
      onConfirm: () => transferCaptain(member),
    })
  }

  const kickMember = async (member: TeamUserInfoModel) => {
    if (!teamId || !isCaptain || !member.id || isLocked) return

    setAction('kick')
    setActionError(null)

    try {
      const response = await api.team.teamKickUser(teamId, member.id)
      await applyTeam(response.data)
      showNotification({
        color: 'teal',
        title: t('team.notification.kick.success'),
        message: t('team.notification.updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (error) {
      setActionError(tryGetErrorMsg(error, t))
    } finally {
      setAction(null)
    }
  }

  const confirmKickMember = (member: TeamUserInfoModel) => {
    modals.openConfirmModal({
      title: t('team.content.kick.confirm.title'),
      children: <Text size="sm">{t('team.content.kick.confirm.message', { user: member.userName })}</Text>,
      labels: { confirm: 'Remove member', cancel: t('common.modal.cancel') },
      confirmProps: { color: 'red' },
      onConfirm: () => kickMember(member),
    })
  }

  const leaveTeam = async () => {
    if (!teamId || isCaptain || isLocked) return

    setAction('leave')
    setActionError(null)

    try {
      await api.team.teamLeave(teamId)
      await removeTeamFromCache(teamId)
      showNotification({
        color: 'teal',
        title: t('team.notification.leave.success'),
        message: t('team.notification.updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      onClose()
    } catch (error) {
      setActionError(tryGetErrorMsg(error, t))
    } finally {
      setAction(null)
    }
  }

  const deleteTeam = async () => {
    if (!teamId || !isCaptain || isLocked) return

    setAction('delete')
    setActionError(null)

    try {
      await api.team.teamDeleteTeam(teamId)
      await removeTeamFromCache(teamId)
      showNotification({
        color: 'teal',
        title: t('team.notification.disband.success'),
        message: t('team.notification.updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      onClose()
    } catch (error) {
      setActionError(tryGetErrorMsg(error, t))
    } finally {
      setAction(null)
    }
  }

  const confirmLeaveOrDelete = () => {
    const deleting = isCaptain
    modals.openConfirmModal({
      title: deleting ? t('team.content.disband.confirm.title') : t('team.content.leave.confirm.title'),
      children: (
        <Text size="sm">
          {deleting
            ? t('team.content.disband.confirm.message', { team: teamInfo?.name })
            : t('team.content.leave.confirm.message', { team: teamInfo?.name })}
        </Text>
      ),
      labels: {
        confirm: deleting ? t('team.button.disband') : t('team.button.leave'),
        cancel: t('common.modal.cancel'),
      },
      confirmProps: { color: 'red' },
      onConfirm: deleting ? deleteTeam : leaveTeam,
    })
  }

  const retryInvite = () => {
    if (!teamId || !isCaptain) return
    setInviteLoading(true)
    setInviteError(null)
    api.team
      .teamInviteCode(teamId)
      .then((response) => setInviteCode(response.data))
      .catch((error) => setInviteError(tryGetErrorMsg(error, t)))
      .finally(() => setInviteLoading(false))
  }

  return (
    <Modal
      {...modalProps}
      size="xl"
      onClose={close}
      centered
      classNames={{ content: styles.content, header: styles.header, body: styles.body }}
    >
      {!teamInfo ? (
        <EmptyState title="Team unavailable" description="Close this dialog and select a team again." />
      ) : (
        <div className={styles.layout}>
          <section className={styles.teamHero}>
            <button
              type="button"
              className={styles.avatarButton}
              onClick={() => isCaptain && setAvatarModalOpened(true)}
              disabled={!isCaptain || isPending}
              aria-label={isCaptain ? 'Change team avatar' : 'Team avatar'}
            >
              <Avatar className={styles.teamAvatar} size={88} radius="md" src={teamInfo.avatar}>
                {teamInfo.name?.slice(0, 1).toUpperCase() || 'T'}
              </Avatar>
              {isCaptain && (
                <span className={styles.avatarEdit} aria-hidden>
                  <Icon path={mdiCameraOutline} size={0.75} />
                </span>
              )}
            </button>
            <div className={styles.heroCopy}>
              <span>{isCaptain ? 'Captain command' : 'Team membership'}</span>
              <h2>{teamInfo.name || 'Unnamed team'}</h2>
              <p>{teamInfo.bio || 'No team biography has been added.'}</p>
            </div>
            <div className={styles.heroBadges}>
              <Badge
                variant="outline"
                color={isCaptain ? 'violet' : 'cyan'}
                leftSection={<Icon path={isCaptain ? mdiCrown : mdiAccountGroupOutline} size={0.58} />}
              >
                {isCaptain ? 'Captain' : 'Member'}
              </Badge>
              {isLocked && (
                <Badge variant="outline" color="orange" leftSection={<Icon path={mdiLockOutline} size={0.58} />}>
                  Locked
                </Badge>
              )}
            </div>
          </section>

          {isLocked && (
            <div className={styles.lockNotice} role="status">
              <Icon path={mdiLockOutline} size={0.8} aria-hidden />
              This team is locked by active game participation. Member removal, leaving and deletion are unavailable.
            </div>
          )}

          {actionError && (
            <div className={styles.error} role="alert">
              <Icon path={mdiAlertCircleOutline} size={0.8} aria-hidden />
              {actionError}
            </div>
          )}

          <div className={styles.contentGrid}>
            <form className={styles.teamForm} onSubmit={saveTeam}>
              <div className={styles.sectionHeading}>
                <div>
                  <span>TEAM_IDENTITY</span>
                  <h3>Team profile</h3>
                </div>
                <Badge variant="light" color={isCaptain ? 'cyan' : 'gray'}>
                  {isCaptain ? 'Editable' : 'Captain managed'}
                </Badge>
              </div>
              <TextInput
                required
                label={t('team.label.name')}
                description={`${teamInfo.name?.length ?? 0}/20`}
                maxLength={20}
                readOnly={!isCaptain}
                disabled={isPending}
                value={teamInfo.name ?? ''}
                onChange={(event) => updateTeamField('name', event.currentTarget.value)}
              />
              <Textarea
                label={t('team.label.bio')}
                placeholder={t('team.placeholder.bio')}
                description={`${teamInfo.bio?.length ?? 0}/72`}
                maxLength={72}
                minRows={4}
                maxRows={6}
                autosize
                readOnly={!isCaptain}
                disabled={isPending}
                value={teamInfo.bio ?? ''}
                onChange={(event) => updateTeamField('bio', event.currentTarget.value)}
              />
              {isCaptain && (
                <div className={styles.saveRow}>
                  <Button type="submit" loading={action === 'save'} disabled={!hasTeamChanges || isPending}>
                    {t('team.button.save')}
                  </Button>
                </div>
              )}
            </form>

            {isCaptain && (
              <section className={styles.inviteSection}>
                <div className={styles.sectionHeading}>
                  <div>
                    <span>PRIVATE_ACCESS</span>
                    <h3>Invitation code</h3>
                  </div>
                  <Icon path={mdiShieldAccountOutline} size={1} aria-hidden />
                </div>
                <p>Share this code only with the people you want to add to the team.</p>
                {inviteLoading ? (
                  <LoadingState variant="panel" count={2} label="Loading invitation code" />
                ) : inviteError ? (
                  <ErrorState compact title="Invitation unavailable" message={inviteError} onRetry={retryInvite} />
                ) : (
                  <div className={styles.inviteControl}>
                    <TextInput
                      aria-label="Team invitation code"
                      value={inviteCode}
                      readOnly
                      onFocus={(event) => event.currentTarget.select()}
                    />
                    <Tooltip label={clipboard.copied ? 'Copied' : 'Copy invitation code'} withArrow>
                      <ActionIcon
                        size="lg"
                        variant="light"
                        aria-label="Copy invitation code"
                        disabled={!inviteCode || isPending}
                        onClick={copyInviteCode}
                      >
                        <Icon path={clipboard.copied ? mdiCheck : mdiContentCopy} size={0.8} />
                      </ActionIcon>
                    </Tooltip>
                    <Tooltip label="Regenerate invitation code" withArrow>
                      <ActionIcon
                        size="lg"
                        variant="light"
                        color="orange"
                        aria-label="Regenerate invitation code"
                        loading={action === 'invite'}
                        disabled={isPending}
                        onClick={confirmRefreshInviteCode}
                      >
                        <Icon path={mdiRefresh} size={0.8} />
                      </ActionIcon>
                    </Tooltip>
                  </div>
                )}
              </section>
            )}
          </div>

          <section className={styles.rosterSection}>
            <div className={styles.sectionHeading}>
              <div>
                <span>ACTIVE_ROSTER</span>
                <h3>Members</h3>
              </div>
              <strong>{teamInfo.members?.length ?? 0}</strong>
            </div>
            {!teamInfo.members || teamInfo.members.length === 0 ? (
              <EmptyState compact title="No members" description="This team currently has no visible members." />
            ) : (
              <ScrollArea.Autosize mah={330} offsetScrollbars type="auto">
                <ul className={styles.memberList}>
                  {[...teamInfo.members]
                    .sort((left, right) => Number(right.captain) - Number(left.captain))
                    .map((member) => (
                      <TeamMemberRow
                        key={member.id}
                        member={member}
                        currentUserId={currentUserId}
                        canManage={isCaptain}
                        locked={isLocked}
                        pending={isPending}
                        onTransfer={confirmTransferCaptain}
                        onKick={confirmKickMember}
                      />
                    ))}
                </ul>
              </ScrollArea.Autosize>
            )}
          </section>

          <section className={styles.dangerZone}>
            <div>
              <span>DESTRUCTIVE_ACTION</span>
              <h3>{isCaptain ? 'Disband team' : 'Leave team'}</h3>
              <p>
                {isCaptain
                  ? 'Permanently delete this team after confirming the action.'
                  : 'Remove your membership from this team after confirming the action.'}
              </p>
            </div>
            <Button
              color="red"
              variant="outline"
              leftSection={<Icon path={isCaptain ? mdiDeleteOutline : mdiExitToApp} size={0.78} />}
              loading={action === (isCaptain ? 'delete' : 'leave')}
              disabled={isPending || isLocked}
              onClick={confirmLeaveOrDelete}
            >
              {isCaptain ? t('team.button.disband') : t('team.button.leave')}
            </Button>
          </section>
        </div>
      )}

      <Modal
        opened={avatarModalOpened}
        onClose={closeAvatarModal}
        title="Team avatar"
        centered
        classNames={{ content: styles.content, header: styles.header }}
      >
        <Stack gap="md">
          <Dropzone
            className={styles.dropzone}
            onDrop={(files) => {
              setAvatarError(null)
              setAvatarFile(files[0] ?? null)
            }}
            onReject={() => setAvatarError(t('common.error.file_invalid.message'))}
            multiple={false}
            disabled={action === 'avatar'}
            maxSize={3 * 1024 * 1024}
            accept={IMAGE_MIME_TYPES}
          >
            <div className={styles.dropzoneBody}>
              {avatarPreview ? (
                <Image className={styles.avatarPreview} fit="contain" src={avatarPreview} alt="Selected team avatar preview" />
              ) : (
                <>
                  <Icon path={mdiCloudUploadOutline} size={2.2} aria-hidden />
                  <strong>Select a team image</strong>
                  <span>PNG, JPEG, WebP or GIF · maximum 3 MB</span>
                </>
              )}
            </div>
          </Dropzone>
          {avatarError && (
            <div className={styles.error} role="alert">
              <Icon path={mdiAlertCircleOutline} size={0.78} aria-hidden />
              {avatarError}
            </div>
          )}
          <Group justify="flex-end" wrap="wrap">
            <Button variant="default" disabled={action === 'avatar'} onClick={closeAvatarModal}>
              {t('common.modal.cancel')}
            </Button>
            <Button loading={action === 'avatar'} disabled={!avatarFile || action === 'avatar'} onClick={uploadAvatar}>
              {t('common.avatar.save')}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Modal>
  )
}
