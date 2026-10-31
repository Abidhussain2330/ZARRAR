import {
  ActionIcon,
  Button,
  Center,
  Image,
  Input,
  NumberInput,
  Stack,
  Switch,
  Text,
  Textarea,
  TextInput,
} from '@mantine/core'
import { DateTimePicker } from '@mantine/dates'
import { Dropzone } from '@mantine/dropzone'
import { useClipboard, useInputState } from '@mantine/hooks'
import { notifications, showNotification, updateNotification } from '@mantine/notifications'
import { mdiCheck, mdiClipboard, mdiClose, mdiContentSaveOutline, mdiDeleteOutline, mdiDiceMultiple, mdiDownload } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import localizedFormat from 'dayjs/plugin/localizedFormat'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useParams } from 'react-router'
import { AdminGameLayout } from '@Components/admin/AdminGameLayout'
import { SwitchLabel } from '@Components/admin/SwitchLabel'
import {
  AdminFormGrid,
  AdminFormWide,
  AdminSection,
  openAdminConfirm,
  showAdminSuccess,
} from '@Components/admin/shell'
import { ErrorState, Panel } from '@Components/zarrar'
import { downloadBlob } from '@Utils/ApiHelper'
import { getInputNumber, IMAGE_MIME_TYPES, randomInviteCode, showErrorMsg, tryGetErrorMsg } from '@Utils/Shared'
import { useAdminGame } from '@Hooks/useGame'
import api, { GameInfoModel } from '@Api'
import misc from '@Styles/Misc.module.css'

dayjs.extend(localizedFormat)

const GameInfoEdit: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1', 10)
  const { game: gameSource, error, mutate } = useAdminGame(numId)
  const [game, setGame] = useState<GameInfoModel>()
  const navigate = useNavigate()

  const [disabled, setDisabled] = useState(false)
  const [start, setStart] = useInputState(dayjs())
  const [end, setEnd] = useInputState(dayjs())
  const [wpddl, setWpddl] = useInputState(3)

  const clipboard = useClipboard()
  const { t } = useTranslation()

  useEffect(() => {
    if (numId < 0) {
      showNotification({
        color: 'red',
        message: t('common.error.param_error'),
        icon: <Icon path={mdiClose} size={1} />,
      })
      navigate('/admin/games')
      return
    }

    if (gameSource) {
      setGame(gameSource)
      setStart(dayjs(gameSource.start))
      setEnd(dayjs(gameSource.end))

      const deadline = dayjs(gameSource.writeupDeadline).diff(gameSource.end, 'h')
      setWpddl(deadline < 0 ? 0 : deadline)
    }
  }, [id, gameSource])

  const onUpdatePoster = async (file: File | undefined) => {
    if (!game || !file) return

    setDisabled(true)
    notifications.clean()
    showNotification({
      id: 'upload-poster',
      color: 'orange',
      message: t('admin.notification.games.info.poster.uploading'),
      loading: true,
      autoClose: false,
    })

    try {
      const res = await api.edit.editUpdateGamePoster(game.id!, { file })
      updateNotification({
        id: 'upload-poster',
        color: 'teal',
        message: t('admin.notification.games.info.poster.uploaded'),
        icon: <Icon path={mdiCheck} size={1} />,
        autoClose: true,
        loading: false,
      })
      mutate({ ...game, poster: res.data })
    } catch (err) {
      updateNotification({
        id: 'upload-poster',
        color: 'red',
        title: t('admin.notification.games.info.poster.upload_failed'),
        message: tryGetErrorMsg(err, t),
        icon: <Icon path={mdiClose} size={1} />,
        autoClose: true,
        loading: false,
      })
    } finally {
      setDisabled(false)
    }
  }

  const onUpdateInfo = async () => {
    if (!game?.title) return
    setDisabled(true)

    try {
      await api.edit.editUpdateGame(game.id!, {
        ...game,
        inviteCode: game.inviteCode?.length ? game.inviteCode : null,
        start: start.valueOf(),
        end: end.valueOf(),
        writeupDeadline: end.add(wpddl, 'h').valueOf(),
      })
      showAdminSuccess(t('admin.notification.games.info.info_updated'))
      mutate()
      api.game.mutateGameGames()
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setDisabled(false)
    }
  }

  const onConfirmDelete = async () => {
    if (!game) return

    try {
      await api.edit.editDeleteGame(game.id!)
      showAdminSuccess(t('admin.notification.games.info.deleted'))
      navigate('/admin/games')
    } catch (err) {
      showErrorMsg(err, t)
    }
  }

  const onExportGame = async () => {
    if (!game?.id) return
    await downloadBlob(api.edit.editExportGame(game.id, { format: 'blob' }), setDisabled, t)
  }

  return (
    <AdminGameLayout
      tab="info"
      isLoading={!game && !error}
      description={t('admin.nav.game_info_description', {
        defaultValue: 'Schedule, registration rules, write-up policy and public presentation.',
      })}
      actions={
        <>
          <Button
            variant="outline"
            color="red"
            disabled={disabled || !game}
            leftSection={<Icon path={mdiDeleteOutline} size={0.8} />}
            onClick={() =>
              openAdminConfirm({
                t,
                title: t('admin.button.games.delete'),
                message: t('admin.content.games.info.delete', { name: game?.title }),
                subject: game?.title,
                consequences: [
                  t('admin.content.games.info.delete_consequence', {
                    defaultValue: 'Challenges, submissions, scoreboard history and write-ups go with it.',
                  }),
                ],
                onConfirm: onConfirmDelete,
              })
            }
          >
            {t('admin.button.games.delete')}
          </Button>
          <Button
            variant="default"
            disabled={disabled || !game}
            leftSection={<Icon path={mdiDownload} size={0.8} />}
            onClick={onExportGame}
          >
            {t('admin.button.games.export')}
          </Button>
          <Button
            variant="default"
            disabled={disabled || !game?.publicKey}
            leftSection={<Icon path={mdiClipboard} size={0.8} />}
            onClick={() => {
              clipboard.copy(game?.publicKey || '')
              showAdminSuccess(t('admin.notification.games.info.public_key_copied'))
            }}
          >
            {t('admin.button.games.copy_public_key')}
          </Button>
          <Button
            disabled={disabled || !game}
            leftSection={<Icon path={mdiContentSaveOutline} size={0.8} />}
            onClick={onUpdateInfo}
          >
            {t('admin.button.save')}
          </Button>
        </>
      }
    >
      {error && !game ? (
        <ErrorState
          message={t('admin.state.load_failed_game', { defaultValue: 'Could not load this event.' })}
          onRetry={() => mutate()}
        />
      ) : (
        <>
          <Panel>
            <AdminSection title={t('admin.tab.games.info')}>
              <AdminFormGrid>
                <TextInput
                  required
                  label={t('admin.content.games.info.title.label')}
                  description={t('admin.content.games.info.title.description')}
                  disabled={disabled}
                  value={game?.title ?? ''}
                  onChange={(e) => game && setGame({ ...game, title: e.target.value })}
                />
                <NumberInput
                  required
                  min={0}
                  label={t('admin.content.games.info.member_limit.label')}
                  description={t('admin.content.games.info.member_limit.description')}
                  disabled={disabled}
                  value={game?.teamMemberCountLimit ?? ''}
                  onChange={(e) => {
                    const number = getInputNumber(e)
                    if (!game || isNaN(number)) return
                    setGame({ ...game, teamMemberCountLimit: number })
                  }}
                />
                <NumberInput
                  required
                  min={0}
                  label={t('admin.content.games.info.container_limit.label')}
                  description={t('admin.content.games.info.container_limit.description')}
                  disabled={disabled}
                  value={game?.containerCountLimit ?? ''}
                  onChange={(e) => {
                    const number = getInputNumber(e)
                    if (!game || isNaN(number)) return
                    setGame({ ...game, containerCountLimit: number })
                  }}
                />
                <TextInput
                  label={t('admin.content.games.info.invite_code.label')}
                  description={t('admin.content.games.info.invite_code.description')}
                  placeholder={t('admin.content.games.info.invite_code.placeholder')}
                  disabled={disabled}
                  value={game?.inviteCode || ''}
                  onChange={(e) => game && setGame({ ...game, inviteCode: e.target.value })}
                  rightSection={
                    <ActionIcon
                      variant="subtle"
                      disabled={disabled}
                      aria-label={t('admin.content.games.info.invite_code.label')}
                      onClick={() => game && setGame({ ...game, inviteCode: randomInviteCode() })}
                    >
                      <Icon path={mdiDiceMultiple} size={0.8} />
                    </ActionIcon>
                  }
                />
                <DateTimePicker
                  required
                  clearable={false}
                  label={t('admin.content.games.info.start_time')}
                  valueFormat="L LT"
                  disabled={disabled}
                  value={start.toDate()}
                  onChange={(e) => {
                    const newDate = dayjs(e)
                    setStart(newDate)
                    if (newDate && end < newDate) setEnd(newDate.add(2, 'h'))
                  }}
                />
                <DateTimePicker
                  required
                  clearable={false}
                  label={t('admin.content.games.info.end_time')}
                  valueFormat="L LT"
                  disabled={disabled}
                  minDate={start.toDate()}
                  value={end.toDate()}
                  error={end < start}
                  onChange={(e) => setEnd(dayjs(e))}
                />
                <Switch
                  disabled={disabled}
                  checked={game?.acceptWithoutReview ?? false}
                  classNames={{ root: misc.switchVerticalMiddle }}
                  label={SwitchLabel(
                    t('admin.content.games.info.accept_without_review.label'),
                    t('admin.content.games.info.accept_without_review.description')
                  )}
                  onChange={(e) => game && setGame({ ...game, acceptWithoutReview: e.target.checked })}
                />
                <Switch
                  disabled={disabled}
                  checked={game?.practiceMode ?? true}
                  classNames={{ root: misc.switchVerticalMiddle }}
                  label={SwitchLabel(
                    t('admin.content.games.info.practice_mode.label'),
                    t('admin.content.games.info.practice_mode.description')
                  )}
                  onChange={(e) => game && setGame({ ...game, practiceMode: e.target.checked })}
                />
              </AdminFormGrid>
            </AdminSection>
          </Panel>

          <Panel>
            <AdminSection title={t('admin.tab.games.writeups')}>
              <AdminFormGrid>
                <Switch
                  disabled={disabled}
                  checked={game?.writeupRequired ?? false}
                  classNames={{ root: misc.switchVerticalMiddle }}
                  label={SwitchLabel(
                    t('admin.content.games.info.writeup_required.label'),
                    t('admin.content.games.info.writeup_required.description')
                  )}
                  onChange={(e) => game && setGame({ ...game, writeupRequired: e.target.checked })}
                />
                <NumberInput
                  required
                  min={0}
                  label={t('admin.content.games.info.writeup_deadline.label')}
                  description={t('admin.content.games.info.writeup_deadline.description')}
                  disabled={disabled}
                  value={wpddl}
                  onChange={(e) => setWpddl(getInputNumber(e))}
                />
                <AdminFormWide>
                  <Textarea
                    autosize
                    minRows={4}
                    maxRows={8}
                    label={t('admin.content.games.info.writeup_instruction')}
                    description={t('admin.content.markdown_support')}
                    disabled={disabled}
                    value={game?.writeupNote ?? ''}
                    onChange={(e) => game && setGame({ ...game, writeupNote: e.target.value })}
                  />
                </AdminFormWide>
              </AdminFormGrid>
            </AdminSection>
          </Panel>

          <Panel>
            <AdminSection
              title={t('admin.content.games.info.content')}
              description={t('admin.content.markdown_support')}
            >
              <AdminFormGrid columns="2">
                <Stack gap="sm">
                  <Textarea
                    autosize
                    minRows={5}
                    maxRows={8}
                    label={t('admin.content.games.info.summary.label')}
                    description={t('admin.content.games.info.summary.description')}
                    disabled={disabled}
                    value={game?.summary ?? ''}
                    onChange={(e) => game && setGame({ ...game, summary: e.target.value })}
                  />
                  <Textarea
                    autosize
                    minRows={10}
                    maxRows={16}
                    label={t('admin.content.games.info.content')}
                    description={t('admin.content.markdown_support')}
                    disabled={disabled}
                    value={game?.content ?? ''}
                    onChange={(e) => game && setGame({ ...game, content: e.target.value })}
                  />
                </Stack>

                <Input.Wrapper label={t('admin.content.games.info.poster')}>
                  <Dropzone
                    onDrop={(files) => onUpdatePoster(files[0])}
                    onReject={() =>
                      showNotification({
                        color: 'red',
                        title: t('common.error.file_invalid.title'),
                        message: t('common.error.file_invalid.message'),
                        icon: <Icon path={mdiClose} size={1} />,
                      })
                    }
                    maxSize={3 * 1024 * 1024}
                    accept={IMAGE_MIME_TYPES}
                    disabled={disabled}
                    data-poster={game?.poster || undefined}
                    classNames={{ root: misc.gamePoster }}
                  >
                    <Center className={misc.noPointerEvents}>
                      {game?.poster ? (
                        <Image height="14rem" fit="contain" src={game.poster} alt={t('admin.content.games.info.poster')} />
                      ) : (
                        <Center h="14rem">
                          <Stack gap={0}>
                            <Text size="lg" inline>
                              {t('common.content.drop_zone.content', {
                                type: t('common.content.drop_zone.type.poster'),
                              })}
                            </Text>
                            <Text size="sm" c="dimmed" inline mt={7}>
                              {t('common.content.drop_zone.limit')}
                            </Text>
                          </Stack>
                        </Center>
                      )}
                    </Center>
                  </Dropzone>
                </Input.Wrapper>
              </AdminFormGrid>
            </AdminSection>
          </Panel>
        </>
      )}
    </AdminGameLayout>
  )
}

export default GameInfoEdit
