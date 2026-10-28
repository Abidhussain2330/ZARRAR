import {
  Button,
  Chip,
  Code,
  FileButton,
  Group,
  Input,
  Progress,
  Stack,
  Text,
  TextInput,
} from '@mantine/core'
import { mdiPuzzleEditOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router'
import { AdminChallengeLayout } from '@Components/admin/AdminChallengeLayout'
import { AttachmentRemoteEditModal } from '@Components/admin/AttachmentRemoteEditModal'
import { AttachmentUploadModal } from '@Components/admin/AttachmentUploadModal'
import { FlagCreateModal } from '@Components/admin/FlagCreateModal'
import { FlagEditPanel } from '@Components/admin/FlagEditPanel'
import { AdminSection, openAdminConfirm, showAdminSuccess } from '@Components/admin/shell'
import { EmptyState, Panel } from '@Components/buraq'
import { showErrorMsg } from '@Utils/Shared'
import { useEditChallenge } from '@Hooks/useEdit'
import api, { ChallengeType, FileType, FlagInfoModel } from '@Api'
import uploadClasses from '@Styles/Upload.module.css'

interface FlagEditProps {
  onDelete: (flag: FlagInfoModel) => void
}

/**
 * Single-attachment challenges: one attachment plus a flag list, or — for a
 * dynamic container — a platform-managed HMAC flag.
 *
 * Note what is *not* rendered anywhere on this screen: a raw flag value. The
 * backend returns `FlagInfoModel.fingerprint` (an HMAC), never the plaintext,
 * and this page keeps it that way.
 */
const OneAttachmentWithFlags: FC<FlagEditProps> = ({ onDelete }) => {
  const { id, chalId } = useParams()
  const [numId, numCId] = [parseInt(id ?? '-1', 10), parseInt(chalId ?? '-1', 10)]

  const { challenge, mutate } = useEditChallenge(numId, numCId)

  const [disabled, setDisabled] = useState(false)
  const [type, setType] = useState<FileType>(challenge?.attachment?.type ?? FileType.None)
  const [remoteUrl, setRemoteUrl] = useState(challenge?.attachment?.url ?? '')
  const [progress, setProgress] = useState(0)
  const [flagCreateModalOpen, setFlagCreateModalOpen] = useState(false)

  const { t } = useTranslation()

  useEffect(() => {
    if (!challenge) return

    setType(challenge.attachment?.type ?? FileType.None)
    setRemoteUrl(challenge.attachment?.url ?? '')
  }, [challenge])

  const fileTypeLabels = new Map<FileType, string>([
    [FileType.None, t('challenge.file_type.none')],
    [FileType.Remote, t('challenge.file_type.remote')],
    [FileType.Local, t('challenge.file_type.local')],
  ])

  const onConfirmClear = async () => {
    setDisabled(true)

    try {
      await api.edit.editUpdateAttachment(numId, numCId, { attachmentType: FileType.None })
      showAdminSuccess(t('admin.notification.games.challenges.attachment.updated'))
      setType(FileType.None)
      if (challenge) mutate({ ...challenge, attachment: null })
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setDisabled(false)
    }
  }

  const onUpload = async (file: File | null) => {
    if (!file) return

    setProgress(0)
    setDisabled(true)

    try {
      const res = await api.assets.assetsUpload({ files: [file] }, undefined, {
        onUploadProgress: (e) => setProgress((e.loaded / (e.total ?? 1)) * 90),
      })
      const remoteFile = res.data[0]
      setProgress(95)

      if (remoteFile) {
        await api.edit.editUpdateAttachment(numId, numCId, {
          attachmentType: FileType.Local,
          fileHash: remoteFile.hash,
        })
        mutate()
        showAdminSuccess(t('admin.notification.games.challenges.attachment.updated'))
      }
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setProgress(0)
      setDisabled(false)
    }
  }

  const onRemote = async () => {
    if (!remoteUrl.startsWith('http')) return
    setDisabled(true)

    try {
      await api.edit.editUpdateAttachment(numId, numCId, { attachmentType: FileType.Remote, remoteUrl })
      showAdminSuccess(t('admin.notification.games.challenges.attachment.updated'))
      mutate()
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setDisabled(false)
    }
  }

  const isDynamicContainer = challenge?.type === ChallengeType.DynamicContainer

  return (
    <>
      <Panel>
        <AdminSection
          title={t('admin.content.games.challenges.attachment.title')}
          actions={
            type !== FileType.Remote ? (
              <FileButton onChange={onUpload}>
                {(props) => (
                  <Button {...props} className={uploadClasses.button} disabled={disabled || type !== FileType.Local}>
                    <div className={uploadClasses.label}>
                      {progress !== 0
                        ? t('admin.button.challenges.attachment.uploading')
                        : t('admin.button.challenges.attachment.upload')}
                    </div>
                    {progress !== 0 && <Progress value={progress} className={uploadClasses.progress} radius="sm" />}
                  </Button>
                )}
              </FileButton>
            ) : (
              <Button disabled={disabled} onClick={onRemote}>
                {t('admin.button.challenges.attachment.save_url')}
              </Button>
            )
          }
        >
          <Stack gap="sm">
            <Input.Wrapper required label={t('admin.content.games.challenges.attachment.type')}>
              <Chip.Group
                value={type}
                onChange={(value) => {
                  if (value === FileType.None) {
                    openAdminConfirm({
                      t,
                      severity: 'caution',
                      title: t('admin.content.games.challenges.attachment.clear.title'),
                      message: t('admin.content.games.challenges.attachment.clear.description'),
                      onConfirm: onConfirmClear,
                    })
                  } else {
                    setType(value as FileType)
                  }
                }}
              >
                <Group gap="sm" wrap="wrap" mt={4}>
                  {Object.entries(FileType).map(([key, value]) => (
                    <Chip key={key} value={value} size="sm">
                      {fileTypeLabels.get(value)}
                    </Chip>
                  ))}
                </Group>
              </Chip.Group>
            </Input.Wrapper>

            {type !== FileType.Remote ? (
              <TextInput
                readOnly
                label={t('admin.content.games.challenges.attachment.link')}
                disabled={disabled || type === FileType.None}
                value={challenge?.attachment?.url ?? ''}
                classNames={{ input: uploadClasses.hover }}
                onClick={() =>
                  challenge?.attachment?.url && window.open(challenge.attachment.url, '_blank', 'noopener,noreferrer')
                }
              />
            ) : (
              <TextInput
                label={t('admin.content.games.challenges.attachment.link')}
                disabled={disabled}
                value={remoteUrl}
                classNames={{ input: uploadClasses.hover }}
                onChange={(e) => setRemoteUrl(e.target.value)}
              />
            )}
          </Stack>
        </AdminSection>
      </Panel>

      <Panel>
        <AdminSection
          title={t('admin.content.games.challenges.flag.title')}
          actions={
            !isDynamicContainer ? (
              <Button disabled={disabled} onClick={() => setFlagCreateModalOpen(true)}>
                {t('admin.button.challenges.flag.add.normal')}
              </Button>
            ) : undefined
          }
        >
          {isDynamicContainer ? (
            <Stack gap="sm">
              <Text size="sm" fw="bold">
                {t('admin.content.games.challenges.flag.hmac.title', {
                  defaultValue: 'Managed HMAC-SHA256 dynamic flags',
                })}
              </Text>
              <Text size="sm" c="dimmed">
                {t('admin.content.games.challenges.flag.hmac.description', {
                  defaultValue:
                    'Each logical instance receives a unique BRQ flag authenticated with the game, challenge, team, participation and instance identifiers. The deployment secret and identifiers are never exposed in the flag.',
                })}
              </Text>
              <Code block>{'BRQ{HMAC_SHA256_VALUE}'}</Code>
            </Stack>
          ) : !challenge?.flags.length ? (
            <EmptyState
              title={t('admin.content.games.challenges.flag.empty.title')}
              description={t('admin.content.games.challenges.flag.empty.description')}
            />
          ) : (
            <FlagEditPanel flags={challenge.flags} onDelete={onDelete} unifiedAttachment={challenge.attachment} />
          )}
        </AdminSection>
      </Panel>

      <FlagCreateModal
        title={t('admin.button.challenges.flag.add.normal')}
        size="min(30rem, 92vw)"
        opened={flagCreateModalOpen}
        onClose={() => setFlagCreateModalOpen(false)}
      />
    </>
  )
}

/** Dynamic-attachment challenges: one attachment *per* flag. */
const FlagsWithAttachments: FC<FlagEditProps> = ({ onDelete }) => {
  const { id, chalId } = useParams()
  const [numId, numCId] = [parseInt(id ?? '-1', 10), parseInt(chalId ?? '-1', 10)]
  const { challenge } = useEditChallenge(numId, numCId)

  const [attachmentUploadModalOpened, setAttachmentUploadModalOpened] = useState(false)
  const [remoteAttachmentModalOpened, setRemoteAttachmentModalOpened] = useState(false)

  const { t } = useTranslation()

  return (
    <>
      <Panel>
        <AdminSection
          title={t('admin.content.games.challenges.flag.title')}
          actions={
            <Group gap="sm">
              <Button variant="default" onClick={() => setRemoteAttachmentModalOpened(true)}>
                {t('admin.button.challenges.flag.add.remote')}
              </Button>
              <Button onClick={() => setAttachmentUploadModalOpened(true)}>
                {t('admin.button.challenges.flag.add.dynamic')}
              </Button>
            </Group>
          }
        >
          {!challenge?.flags.length ? (
            <EmptyState
              title={t('admin.content.games.challenges.flag.empty.title')}
              description={t('admin.content.games.challenges.flag.empty.description')}
            />
          ) : (
            <FlagEditPanel flags={challenge.flags} onDelete={onDelete} />
          )}
        </AdminSection>
      </Panel>

      <AttachmentUploadModal
        title={t('admin.button.challenges.flag.add.dynamic')}
        size="min(40rem, 94vw)"
        opened={attachmentUploadModalOpened}
        onClose={() => setAttachmentUploadModalOpened(false)}
      />
      <AttachmentRemoteEditModal
        title={t('admin.button.challenges.flag.add.remote')}
        size="min(40rem, 94vw)"
        opened={remoteAttachmentModalOpened}
        onClose={() => setRemoteAttachmentModalOpened(false)}
      />
    </>
  )
}

const GameChallengeFlags: FC = () => {
  const { id, chalId } = useParams()
  const [numId, numCId] = [parseInt(id ?? '-1', 10), parseInt(chalId ?? '-1', 10)]
  const { challenge, error, mutate } = useEditChallenge(numId, numCId)

  const { t } = useTranslation()

  const onConfirmDeleteFlag = async (flagId: number) => {
    try {
      await api.edit.editRemoveFlag(numId, numCId, flagId)
      showAdminSuccess(t('admin.notification.games.challenges.flag.deleted'))
      if (challenge) mutate({ ...challenge, flags: challenge.flags.filter((flag) => flag.id !== flagId) })
    } catch (err) {
      showErrorMsg(err, t)
    }
  }

  const onDeleteFlag = (flag: FlagInfoModel) =>
    openAdminConfirm({
      t,
      title: t('admin.button.challenges.flag.delete'),
      message: t('admin.content.games.challenges.flag.delete'),
      // The fingerprint, not the flag. It is what the API returns and it is
      // enough to identify which flag is being removed.
      subject: flag.fingerprint,
      onConfirm: () => {
        if (flag.id) onConfirmDeleteFlag(flag.id)
      },
    })

  return (
    <AdminChallengeLayout
      isLoading={!challenge && !error}
      description={t('admin.nav.flags_description', {
        defaultValue: 'Attachments and flags. Stored flag values are never returned to the browser.',
      })}
      actions={
        <Button
          variant="default"
          component={Link}
          to={`/admin/games/${id}/challenges/${numCId}`}
          leftSection={<Icon path={mdiPuzzleEditOutline} size={0.8} />}
        >
          {t('admin.button.challenges.edit')}
        </Button>
      }
    >
      {challenge?.type === ChallengeType.DynamicAttachment ? (
        <FlagsWithAttachments onDelete={onDeleteFlag} />
      ) : (
        <OneAttachmentWithFlags onDelete={onDeleteFlag} />
      )}
    </AdminChallengeLayout>
  )
}

export default GameChallengeFlags
