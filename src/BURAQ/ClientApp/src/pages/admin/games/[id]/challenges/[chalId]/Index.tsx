import {
  Button,
  ComboboxItem,
  Group,
  Input,
  NumberInput,
  Select,
  Slider,
  Stack,
  Switch,
  Text,
  Textarea,
  TextInput,
} from '@mantine/core'
import { DateTimePicker } from '@mantine/dates'
import { mdiContentSaveOutline, mdiDatabaseEditOutline, mdiDeleteOutline, mdiEyeOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router'
import { HintList } from '@Components/HintList'
import { InstanceEntry } from '@Components/InstanceEntry'
import { AdminChallengeLayout } from '@Components/admin/AdminChallengeLayout'
import { AdvancedChallengeRuntimeControls } from '@Components/admin/AdvancedChallengeRuntimeControls'
import { ChallengePreviewModal } from '@Components/admin/ChallengePreviewModal'
import { SwitchLabel } from '@Components/admin/SwitchLabel'
import { AdminFormGrid, AdminFormWide, AdminSection, openAdminConfirm, showAdminSuccess } from '@Components/admin/shell'
import { ErrorState, Panel } from '@Components/buraq'
import { ScoreFunc } from '@Components/charts/ScoreFunc'
import {
  ChallengeCategoryItem,
  ChallengeCategoryList,
  ChallengeTypeItem,
  getInputNumber,
  NetworkModeItem,
  NetworkModeList,
  showErrorMsg,
  useChallengeCategoryLabelMap,
  useChallengeTypeLabelMap,
  useNetworkModeMap,
} from '@Utils/Shared'
import { useEditChallenge, useEditChallenges } from '@Hooks/useEdit'
import { useGame } from '@Hooks/useGame'
import api, { ChallengeCategory, ChallengeType, ChallengeUpdateModel, NetworkMode } from '@Api'
import misc from '@Styles/Misc.module.css'

const GameChallengeEdit: FC = () => {
  const navigate = useNavigate()
  const { id, chalId } = useParams()
  const [numId, numCId] = [parseInt(id ?? '-1', 10), parseInt(chalId ?? '-1', 10)]

  const { game } = useGame(numId)
  const { challenge, error, mutate } = useEditChallenge(numId, numCId)
  const { challenges, mutate: mutateChals } = useEditChallenges(numId)

  const [challengeInfo, setChallengeInfo] = useState<ChallengeUpdateModel>({ ...challenge })
  const [deadline, setDeadline] = useState<dayjs.Dayjs | null>(
    challenge?.deadlineUtc ? dayjs(challenge.deadlineUtc) : null
  )

  const [disabled, setDisabled] = useState(false)
  const [minRate, setMinRate] = useState((challenge?.minScoreRate ?? 0.25) * 100)
  const [category, setCategory] = useState<string | null>(challenge?.category ?? ChallengeCategory.Misc)
  const [networkMode, setNetworkMode] = useState<string | null>(challenge?.networkMode ?? NetworkMode.Open)
  const [type, setType] = useState<string | null>(challenge?.type ?? ChallengeType.StaticAttachment)
  const [currentAcceptCount, setCurrentAcceptCount] = useState(0)
  const [previewOpened, setPreviewOpened] = useState(false)

  const challengeTypeLabelMap = useChallengeTypeLabelMap()
  const challengeCategoryLabelMap = useChallengeCategoryLabelMap()
  const networkModeLabelMap = useNetworkModeMap()
  const { t } = useTranslation()

  useEffect(() => {
    if (!challenge) return

    setChallengeInfo({ ...challenge })
    setCategory(challenge.category)
    setType(challenge.type)
    setMinRate((challenge.minScoreRate ?? 0.25) * 100)
    setCurrentAcceptCount(challenge.acceptedCount)
    setDeadline(challenge.deadlineUtc ? dayjs(challenge.deadlineUtc) : null)
    setNetworkMode(challenge.networkMode ?? NetworkMode.Open)
  }, [challenge])

  const onUpdate = async (update: ChallengeUpdateModel, noFeedback?: boolean) => {
    setDisabled(true)

    try {
      const res = await api.edit.editUpdateGameChallenge(numId, numCId, {
        ...update,
        deadlineUtc: deadline ? deadline.valueOf() : 0,
        isEnabled: undefined,
      })
      if (!noFeedback) showAdminSuccess(t('admin.notification.games.challenges.updated'))
      mutate(res.data)
      mutateChals()
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      if (!noFeedback) setDisabled(false)
    }
  }

  const onSave = () =>
    onUpdate({ ...challengeInfo, category: category as ChallengeCategory, minScoreRate: minRate / 100 })

  const onConfirmDelete = async () => {
    setDisabled(true)

    try {
      await api.edit.editRemoveGameChallenge(numId, numCId)
      showAdminSuccess(t('admin.notification.games.challenges.deleted'))
      mutateChals(
        challenges?.filter((item) => item.id !== numCId),
        { revalidate: false }
      )
      navigate(`/admin/games/${id}/challenges`)
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setDisabled(false)
    }
  }

  /*
   * Creating or destroying the test container saves first: the container is
   * built from the image and port currently persisted, so testing unsaved
   * values would silently test the previous ones.
   */
  const onToggleTestContainer = async () => {
    if (!challenge) return
    setDisabled(true)

    await onUpdate({ ...challengeInfo, category: category as ChallengeCategory, minScoreRate: minRate / 100 }, true)

    try {
      if (challenge.testContainer) {
        await api.edit.editDestroyTestContainer(numId, numCId)
        showAdminSuccess(t('admin.notification.games.instances.deleted'))
        mutate({ ...challenge, testContainer: undefined })
      } else {
        const res = await api.edit.editCreateTestContainer(numId, numCId)
        showAdminSuccess(t('admin.notification.games.instances.created'))
        mutate({ ...challenge, testContainer: res.data })
      }
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setDisabled(false)
    }
  }

  const tryDefault: <T>(values: T[], defaultValue?: NonNullable<T>) => NonNullable<T> | undefined = (
    values,
    fallback
  ) => values.find((value) => !!value) ?? fallback

  const isContainer = type === ChallengeType.StaticContainer || type === ChallengeType.DynamicContainer

  return (
    <AdminChallengeLayout
      isLoading={!challenge && !error}
      description={t('admin.nav.challenge_description', {
        defaultValue: 'Scoring curve, hints, submission limits and runtime configuration.',
      })}
      actions={
        <>
          <Button
            variant="outline"
            color="red"
            disabled={disabled || !challenge}
            leftSection={<Icon path={mdiDeleteOutline} size={0.8} />}
            onClick={() =>
              openAdminConfirm({
                t,
                title: t('admin.button.challenges.delete'),
                message: t('admin.content.games.challenges.delete', { name: challengeInfo?.title }),
                subject: challengeInfo?.title ?? undefined,
                consequences: [
                  t('admin.content.games.challenges.delete_consequence', {
                    defaultValue: 'Flags, attachments and every submission against this challenge are removed.',
                  }),
                ],
                onConfirm: onConfirmDelete,
              })
            }
          >
            {t('admin.button.challenges.delete')}
          </Button>
          <Button
            variant="default"
            disabled={disabled}
            leftSection={<Icon path={mdiEyeOutline} size={0.8} />}
            onClick={() => setPreviewOpened(true)}
          >
            {t('admin.button.challenges.preview')}
          </Button>
          <Button
            variant="default"
            component={Link}
            to={`/admin/games/${numId}/challenges/${numCId}/flags`}
            leftSection={<Icon path={mdiDatabaseEditOutline} size={0.8} />}
          >
            {t('admin.button.challenges.edit_more')}
          </Button>
          <Button
            disabled={disabled || !challenge}
            leftSection={<Icon path={mdiContentSaveOutline} size={0.8} />}
            onClick={onSave}
          >
            {t('admin.button.save')}
          </Button>
        </>
      }
    >
      {error && !challenge ? (
        <ErrorState
          message={t('admin.state.load_failed_challenge', { defaultValue: 'Could not load this challenge.' })}
          onRetry={() => mutate()}
        />
      ) : (
        <>
          <Panel>
            <AdminSection title={t('admin.content.games.challenges.title')}>
              <AdminFormGrid>
                <TextInput
                  required
                  label={t('admin.content.games.challenges.title')}
                  disabled={disabled}
                  value={challengeInfo.title ?? ''}
                  onChange={(e) => setChallengeInfo({ ...challengeInfo, title: e.target.value })}
                />
                <Select
                  readOnly
                  label={t('admin.content.games.challenges.type.label')}
                  description={t('admin.content.games.challenges.type.description')}
                  value={type}
                  disabled={disabled}
                  renderOption={ChallengeTypeItem}
                  data={Object.entries(ChallengeType).map(([, value]) => {
                    const meta = challengeTypeLabelMap.get(value)
                    return { value, label: meta?.name, ...meta } as ComboboxItem
                  })}
                />
                <Select
                  required
                  label={t('admin.content.games.challenges.category')}
                  value={category}
                  disabled={disabled}
                  renderOption={ChallengeCategoryItem}
                  onChange={(value) => {
                    setCategory(value)
                    setChallengeInfo({ ...challengeInfo, category: value as ChallengeCategory })
                  }}
                  data={ChallengeCategoryList.map((item) => {
                    const meta = challengeCategoryLabelMap.get(item)
                    return { value: item, label: meta?.name, ...meta } as ComboboxItem
                  })}
                />
                <AdminFormWide>
                  <Textarea
                    autosize
                    minRows={5}
                    maxRows={12}
                    label={t('admin.content.games.challenges.description')}
                    description={t('admin.content.markdown_support')}
                    disabled={disabled}
                    value={challengeInfo.content ?? ''}
                    onChange={(e) => setChallengeInfo({ ...challengeInfo, content: e.target.value })}
                  />
                </AdminFormWide>
              </AdminFormGrid>
            </AdminSection>
          </Panel>

          <Panel>
            <AdminSection title={t('admin.content.games.challenges.score')}>
              <AdminFormGrid>
                <Stack gap="sm">
                  <Group wrap="nowrap" gap="sm">
                    <NumberInput
                      required
                      min={0}
                      label={t('admin.content.games.challenges.score')}
                      disabled={disabled}
                      value={challengeInfo.originalScore ?? 500}
                      onChange={(e) => {
                        const number = getInputNumber(e)
                        if (isNaN(number)) return
                        setChallengeInfo({ ...challengeInfo, originalScore: number })
                      }}
                    />
                    <NumberInput
                      required
                      min={0.1}
                      step={0.2}
                      decimalScale={2}
                      fixedDecimalScale
                      label={t('admin.content.games.challenges.difficulty')}
                      disabled={disabled}
                      value={challengeInfo.difficulty ?? 100}
                      onChange={(e) => {
                        const number = getInputNumber(e, true)
                        if (isNaN(number)) return
                        setChallengeInfo({ ...challengeInfo, difficulty: number })
                      }}
                    />
                  </Group>

                  <Input.Wrapper required label={t('admin.content.games.challenges.min_score_radio.label')}>
                    <Slider
                      disabled={disabled}
                      value={minRate}
                      onChange={setMinRate}
                      marks={[
                        { value: 20, label: '20%' },
                        { value: 50, label: '50%' },
                        { value: 80, label: '80%' },
                      ]}
                      label={(value) =>
                        t('admin.content.games.challenges.min_score_radio.description', {
                          min_score: ((value / 100) * (challengeInfo.originalScore ?? 500)).toFixed(0),
                        })
                      }
                      classNames={{ label: misc.challEditLabel }}
                    />
                  </Input.Wrapper>

                  <Switch
                    mt="lg"
                    disabled={disabled}
                    checked={!challengeInfo.disableBloodBonus}
                    label={SwitchLabel(
                      t('admin.content.games.challenges.blood_bonus.label'),
                      t('admin.content.games.challenges.blood_bonus.description')
                    )}
                    onChange={(e) => setChallengeInfo({ ...challengeInfo, disableBloodBonus: !e.target.checked })}
                  />
                </Stack>

                <ScoreFunc
                  currentAcceptCount={currentAcceptCount}
                  originalScore={challengeInfo.originalScore ?? 500}
                  minScoreRate={minRate / 100}
                  difficulty={challengeInfo.difficulty ?? 30}
                />
              </AdminFormGrid>
            </AdminSection>
          </Panel>

          <Panel>
            <AdminSection title={t('admin.content.games.challenges.hints')}>
              <AdminFormGrid>
                <Stack gap="sm">
                  <NumberInput
                    min={0}
                    max={10000}
                    label={t('admin.content.games.challenges.submission_limit.label')}
                    description={t('admin.content.games.challenges.submission_limit.description')}
                    placeholder={t('admin.content.games.challenges.submission_limit.placeholder')}
                    disabled={disabled}
                    value={challengeInfo.submissionLimit ?? ''}
                    onChange={(e) => {
                      const number = getInputNumber(e)
                      if (isNaN(number)) return
                      setChallengeInfo({ ...challengeInfo, submissionLimit: number })
                    }}
                  />
                  <DateTimePicker
                    clearable
                    valueFormat="L LT"
                    label={t('admin.content.games.challenges.deadline.label')}
                    placeholder={t('admin.content.games.challenges.deadline.placeholder')}
                    disabled={disabled}
                    value={deadline?.toDate()}
                    onChange={(e) => setDeadline(e ? dayjs(e) : null)}
                  />
                  {type === ChallengeType.DynamicAttachment && (
                    <TextInput
                      label={t('admin.content.games.challenges.attachment_name.label')}
                      description={t('admin.content.games.challenges.attachment_name.description')}
                      disabled={disabled}
                      value={challengeInfo.fileName ?? 'attachment'}
                      onChange={(e) => setChallengeInfo({ ...challengeInfo, fileName: e.target.value })}
                    />
                  )}
                </Stack>

                <HintList
                  label={
                    <Group gap="sm">
                      <Text size="sm">{t('admin.content.games.challenges.hints')}</Text>
                      <Text size="xs" c="dimmed">
                        {t('admin.content.markdown_inline_support')}
                      </Text>
                    </Group>
                  }
                  hints={challengeInfo.hints ?? []}
                  penalties={challengeInfo.hintPenalties ?? []}
                  disabled={disabled}
                  height={200}
                  onChangeHint={(hints, hintPenalties) => setChallengeInfo({ ...challengeInfo, hints, hintPenalties })}
                />
              </AdminFormGrid>
            </AdminSection>
          </Panel>

          {isContainer && (
            <Panel>
              <AdminSection
                title={t('admin.content.games.challenges.container_image')}
                description={t('admin.content.games.challenges.network_mode.description')}
              >
                <AdminFormGrid>
                  <AdminFormWide>
                    <Group align="flex-end" gap="sm" wrap="wrap">
                      <TextInput
                        required
                        label={t('admin.content.games.challenges.container_image')}
                        disabled={disabled}
                        value={challengeInfo.containerImage ?? ''}
                        onChange={(e) => setChallengeInfo({ ...challengeInfo, containerImage: e.target.value })}
                        classNames={{ root: misc.flexGrow }}
                      />
                      <NumberInput
                        required
                        min={1}
                        max={65535}
                        w="8rem"
                        label={t('admin.content.games.challenges.service_port.label')}
                        disabled={disabled}
                        value={challengeInfo.exposePort ?? 80}
                        onChange={(e) => {
                          const number = getInputNumber(e)
                          if (isNaN(number)) return
                          setChallengeInfo({ ...challengeInfo, exposePort: number })
                        }}
                      />
                      <Button
                        miw="9rem"
                        color={challenge?.testContainer ? 'orange' : 'green'}
                        disabled={disabled}
                        onClick={onToggleTestContainer}
                      >
                        {challenge?.testContainer
                          ? t('admin.button.challenges.test_container.destroy')
                          : t('admin.button.challenges.test_container.create')}
                      </Button>
                    </Group>
                  </AdminFormWide>

                  <AdminFormWide>
                    <InstanceEntry
                      test
                      label={`${challenge?.title} @ ${game?.title} (test)`}
                      disabled={disabled}
                      context={{
                        closeTime: challenge?.testContainer?.expectStopAt,
                        instanceEntry: challenge?.testContainer?.entry,
                      }}
                    />
                  </AdminFormWide>

                  <Select
                    required
                    label={t('admin.content.games.challenges.network_mode.label')}
                    description={t('admin.content.games.challenges.network_mode.description')}
                    value={networkMode ?? NetworkMode.Open}
                    disabled={disabled}
                    renderOption={NetworkModeItem}
                    onChange={(value) => {
                      setNetworkMode(value)
                      setChallengeInfo({ ...challengeInfo, networkMode: value as NetworkMode })
                    }}
                    data={NetworkModeList.map((mode) => {
                      const meta = networkModeLabelMap.get(mode)
                      return { value: mode, ...meta } as ComboboxItem
                    })}
                  />
                  <NumberInput
                    required
                    min={1}
                    max={1024}
                    label={t('admin.content.games.challenges.cpu_limit.label')}
                    description={t('admin.content.games.challenges.cpu_limit.description')}
                    disabled={disabled}
                    value={challengeInfo.cpuCount ?? 1}
                    onChange={(e) => {
                      const number = getInputNumber(e)
                      if (isNaN(number)) return
                      setChallengeInfo({ ...challengeInfo, cpuCount: number })
                    }}
                  />
                  <NumberInput
                    required
                    min={32}
                    max={1048576}
                    label={t('admin.content.games.challenges.memory_limit.label')}
                    description={t('admin.content.games.challenges.memory_limit.description')}
                    disabled={disabled}
                    value={challengeInfo.memoryLimit ?? 32}
                    onChange={(e) => {
                      const number = getInputNumber(e)
                      if (isNaN(number)) return
                      setChallengeInfo({ ...challengeInfo, memoryLimit: number })
                    }}
                  />
                  <NumberInput
                    required
                    min={0}
                    max={1048576}
                    label={t('admin.content.games.challenges.storage_limit.label')}
                    description={t('admin.content.games.challenges.storage_limit.description')}
                    disabled={disabled}
                    value={challengeInfo.storageLimit ?? 32}
                    onChange={(e) => {
                      const number = getInputNumber(e)
                      if (isNaN(number)) return
                      setChallengeInfo({ ...challengeInfo, storageLimit: number })
                    }}
                  />
                  <Switch
                    disabled={disabled}
                    checked={challengeInfo.enableTrafficCapture ?? false}
                    classNames={{ root: misc.switchVerticalMiddle }}
                    label={SwitchLabel(
                      t('admin.content.games.challenges.traffic_capture.label'),
                      t('admin.content.games.challenges.traffic_capture.description')
                    )}
                    onChange={(e) => setChallengeInfo({ ...challengeInfo, enableTrafficCapture: e.target.checked })}
                  />
                </AdminFormGrid>
              </AdminSection>
            </Panel>
          )}

          <AdvancedChallengeRuntimeControls challengeId={numCId} challengeType={challenge?.type} />
        </>
      )}

      <ChallengePreviewModal
        challenge={{
          title: tryDefault([challengeInfo?.title, challenge?.title], ''),
          content: tryDefault([challengeInfo?.content, challenge?.content]),
          hints: (tryDefault([challengeInfo?.hints, challenge?.hints], []) ?? []).map((content, index) => ({
            index,
            content,
            penalty: (challengeInfo?.hintPenalties ?? challenge?.hintPenalties ?? [])[index] ?? 0,
            isUnlocked: true,
          })),
          score: tryDefault([challengeInfo?.originalScore, challenge?.originalScore], 0),
          limit: tryDefault([challengeInfo?.submissionLimit, challenge?.submissionLimit], 0),
          category: category as ChallengeCategory,
          deadline: deadline ? deadline.valueOf() : undefined,
          type: challenge?.type ?? ChallengeType.StaticAttachment,
          context: {
            closeTime: challenge?.testContainer?.expectStopAt ?? null,
            instanceEntry: challenge?.testContainer?.entry ?? null,
            url: challenge?.attachment?.url ?? null,
            fileSize: challenge?.attachment?.fileSize ?? null,
          },
        }}
        opened={previewOpened}
        onClose={() => setPreviewOpened(false)}
        disabled={disabled}
        onCreate={() => void onToggleTestContainer()}
        onDestroy={() => void onToggleTestContainer()}
        cateData={challengeCategoryLabelMap.get(
          (challengeInfo?.category as ChallengeCategory) ?? ChallengeCategory.Misc
        )!}
      />
    </AdminChallengeLayout>
  )
}

export default GameChallengeEdit
