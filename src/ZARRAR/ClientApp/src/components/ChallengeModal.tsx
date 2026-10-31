import { Button, Group, Modal, ModalProps, Stack, Text, TextInput, Title } from '@mantine/core'
import {
  mdiAlertCircleOutline,
  mdiCheckCircleOutline,
  mdiClockOutline,
  mdiDownloadBoxOutline,
  mdiFileDocumentOutline,
  mdiFlagOutline,
  mdiLightbulbOnOutline,
  mdiOpenInNew,
  mdiPackageVariantClosed,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import duration from 'dayjs/plugin/duration'
import { FC, ReactNode, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { InstanceEntry } from '@Components/InstanceEntry'
import type { InstanceAction } from '@Components/InstanceEntry'
import { InlineMarkdown, Markdown } from '@Components/MarkdownRenderer'
import { CategoryBadge, DifficultyIndicator, ErrorState, LoadingState, StatusDot } from '@Components/zarrar'
import type { ZarrarDifficulty } from '@Components/zarrar'
import { useLanguage } from '@Utils/I18n'
import { ChallengeCategoryItemProps } from '@Utils/Shared'
import { ChallengeDetailModel, ChallengeType } from '@Api'
import classes from '@Styles/ChallengeModal.module.css'
import misc from '@Styles/Misc.module.css'

dayjs.extend(duration)

export type ChallengeSubmissionState =
  'idle' | 'pending' | 'correct' | 'incorrect' | 'rate-limited' | 'error' | 'unknown'

interface ChallengeDeadlineNoticeProps {
  deadline: dayjs.Dayjs
  onExpiredChange: (expired: boolean) => void
}

const ChallengeDeadlineNotice: FC<ChallengeDeadlineNoticeProps> = ({ deadline, onExpiredChange }) => {
  const { t } = useTranslation()
  const { locale } = useLanguage()
  const [now, setNow] = useState(dayjs())
  const formattedDeadline = deadline.locale(locale).format('L LTS')

  useEffect(() => {
    setNow(dayjs())
    const timer = window.setInterval(() => setNow(dayjs()), 1000)
    return () => window.clearInterval(timer)
  }, [deadline])

  const expired = now.isAfter(deadline)

  useEffect(() => {
    onExpiredChange(expired)
  }, [expired, onExpiredChange])

  const remaining = dayjs.duration(Math.max(0, deadline.diff(now)))
  const countdownText = `${Math.floor(remaining.asHours()).toString().padStart(2, '0')}:${remaining.format('mm:ss')}`

  return (
    <div className={classes.deadline} data-expired={expired || undefined}>
      <div>
        <Text className={classes.metaLabel}>{t('challenge.content.deadline.remaining')}</Text>
        <Text
          className={classes.countdown}
          aria-label={`${t('challenge.content.deadline.remaining')} ${countdownText}`}
        >
          {expired ? '00:00:00' : countdownText}
        </Text>
      </div>
      <div className={classes.deadlineDate}>
        <Text className={classes.metaLabel}>{t('challenge.content.deadline.label')}</Text>
        <Text className={classes.metaValue}>{formattedDeadline}</Text>
      </div>
    </div>
  )
}

interface DetailSectionProps {
  title: string
  icon: string
  children: ReactNode
  className?: string
}

const DetailSection: FC<DetailSectionProps> = ({ title, icon, children, className }) => (
  <section className={[classes.section, className ?? ''].filter(Boolean).join(' ')}>
    <header className={classes.sectionHeader}>
      <Icon path={icon} size={0.82} aria-hidden />
      <h3 className={classes.sectionTitle}>{title}</h3>
    </header>
    <div className={classes.sectionBody}>{children}</div>
  </section>
)

const formatFileSize = (bytes?: number | null): string | null => {
  if (!bytes || bytes <= 0) return null

  const units = ['B', 'KB', 'MB', 'GB']
  const unitIndex = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1)
  const value = bytes / 1024 ** unitIndex
  return `${value >= 10 || unitIndex === 0 ? value.toFixed(0) : value.toFixed(1)} ${units[unitIndex]}`
}

const SUBMISSION_ICON: Record<Exclude<ChallengeSubmissionState, 'idle'>, string> = {
  pending: mdiClockOutline,
  correct: mdiCheckCircleOutline,
  incorrect: mdiAlertCircleOutline,
  'rate-limited': mdiClockOutline,
  error: mdiAlertCircleOutline,
  unknown: mdiAlertCircleOutline,
}

export interface ChallengeModalProps extends ModalProps {
  challenge?: ChallengeDetailModel
  cateData: ChallengeCategoryItemProps
  difficulty?: ZarrarDifficulty
  solveCount?: number
  solved?: boolean
  locked?: boolean
  disabled?: boolean
  loading?: boolean
  loadError?: boolean
  gameTitle?: string
  gameEnded?: boolean
  practiceMode?: boolean
  flag: string
  submissionState?: ChallengeSubmissionState
  submissionMessage?: string
  instanceAction?: InstanceAction
  instanceFailed?: boolean
  setFlag: (value: string) => void
  onCreate: () => void
  onExtend?: () => void
  onDestroy: () => void
  onSubmitFlag: () => void
  onUnlockHint?: (hintIndex: number) => void
  unlockingHintIndex?: number
  onRetry?: () => void
  onDownload?: () => void
}

export const ChallengeModal: FC<ChallengeModalProps> = (props) => {
  const {
    challenge,
    cateData,
    difficulty,
    solveCount,
    solved,
    locked,
    disabled,
    loading,
    loadError,
    gameTitle,
    gameEnded,
    practiceMode,
    flag,
    submissionState = 'idle',
    submissionMessage,
    instanceAction,
    instanceFailed,
    setFlag,
    onCreate,
    onExtend,
    onDestroy,
    onDownload,
    onSubmitFlag,
    onUnlockHint,
    unlockingHintIndex,
    onRetry,
    withCloseButton = true,
    closeButtonProps,
    ...modalProps
  } = props
  const { t } = useTranslation()
  const { locale } = useLanguage()

  const placeholders = useMemo(
    () =>
      t('challenge.content.flag_placeholders', {
        returnObjects: true,
      }) as string[],
    [t]
  )

  const [placeholder, setPlaceholder] = useState('')
  useEffect(() => {
    setPlaceholder(placeholders[Math.floor(Math.random() * placeholders.length)])
  }, [challenge?.id, placeholders])

  const deadlineTime = useMemo(() => (challenge?.deadline ? dayjs(challenge.deadline) : null), [challenge?.deadline])
  const [isDeadlinePassed, setIsDeadlinePassed] = useState(() => (deadlineTime ? dayjs().isAfter(deadlineTime) : false))

  useEffect(() => {
    setIsDeadlinePassed(deadlineTime ? dayjs().isAfter(deadlineTime) : false)
  }, [deadlineTime])

  const isLimitReached = (challenge?.limit && (challenge.attempts ?? 0) >= challenge.limit) || false
  const isContainer =
    challenge?.type === ChallengeType.StaticContainer || challenge?.type === ChallengeType.DynamicContainer
  const deadlineBlocksSubmission = isDeadlinePassed && !(gameEnded && practiceMode)
  const status = solved ? 'solved' : locked || deadlineBlocksSubmission ? 'locked' : 'active'
  const statusLabel =
    status === 'solved'
      ? t('challenge.content.already_solved')
      : status === 'locked'
        ? t('common.indicator.status.locked', { defaultValue: 'Locked' })
        : t('common.indicator.status.active', { defaultValue: 'Active' })

  const challengeIdentifier = challenge?.id ? `CH-${challenge.id.toString().padStart(3, '0')}` : 'CH-—'
  const category = challenge?.category ?? cateData?.name ?? 'Misc'

  const header = (
    <div className={classes.headerLayout}>
      <div className={classes.headerIdentity}>
        <Text className={classes.eyebrow}>{t('challenge.content.details', { defaultValue: 'Challenge details' })}</Text>
        <Group gap="sm" align="center" wrap="wrap">
          <CategoryBadge
            category={String(category)}
            label={cateData?.name ?? String(category)}
            icon={cateData && <Icon path={cateData.icon} size={0.68} />}
          />
          {difficulty && <DifficultyIndicator level={difficulty} />}
        </Group>
        <Title order={2} className={classes.challengeTitle}>
          {challenge?.title ?? ''}
        </Title>
        <Text className={classes.identifier}>{challengeIdentifier}</Text>
      </div>

      <div
        className={classes.metrics}
        aria-label={t('challenge.content.statistics', { defaultValue: 'Challenge statistics' })}
      >
        <div className={classes.metric}>
          <Text className={classes.metaLabel}>{t('challenge.label.points', { defaultValue: 'Points' })}</Text>
          <Text className={classes.metricValue}>{(challenge?.score ?? 0).toLocaleString()}</Text>
        </div>
        <div className={classes.metric}>
          <Text className={classes.metaLabel}>{t('challenge.label.solves', { defaultValue: 'Solves' })}</Text>
          <Text className={classes.metricValue}>{solveCount === undefined ? '—' : solveCount.toLocaleString()}</Text>
        </div>
        <div className={classes.metric}>
          <Text className={classes.metaLabel}>{t('challenge.label.status', { defaultValue: 'Status' })}</Text>
          <StatusDot status={status} label={statusLabel} />
        </div>
      </div>
    </div>
  )

  const link = challenge?.context?.url
  // Static and personalized artifacts are intentionally served by authenticated game APIs.
  // Treating only raw /assets URLs as downloads made these protected same-origin routes look
  // like arbitrary external links even though the browser receives an attachment response.
  const protectedAttachment = !!link && /^\/api\/game\/\d+\/challenges\/\d+\/(?:attachment|artifact)$/.test(link)
  const localAttachment = !!link && (link.startsWith('/assets') || protectedAttachment)
  const attachmentName = localAttachment
    ? protectedAttachment
      ? `${challenge?.title ?? 'Challenge'} attachment`
      : link.split('/').pop()
    : t('common.content.external_link')
  const attachmentSize = formatFileSize(challenge?.context?.fileSize)
  const withAttachment = !!link || !!onDownload

  const attachmentContent = withAttachment ? (
    <div className={classes.attachmentRow}>
      <div className={classes.attachmentIdentity}>
        <span className={classes.resourceIcon} aria-hidden>
          <Icon path={localAttachment ? mdiPackageVariantClosed : mdiFileDocumentOutline} size={0.9} />
        </span>
        <div className={classes.resourceText}>
          <Text className={classes.resourceName} title={attachmentName ?? undefined}>
            {attachmentName}
          </Text>
          {attachmentSize && <Text className={classes.resourceMeta}>{attachmentSize}</Text>}
        </div>
      </div>
      <Button
        component="a"
        href={link ?? '#'}
        variant="light"
        target="_blank"
        rel="noreferrer"
        leftSection={<Icon path={localAttachment ? mdiDownloadBoxOutline : mdiOpenInNew} size={0.78} />}
        onClick={
          onDownload
            ? (event) => {
                event.preventDefault()
                onDownload()
              }
            : undefined
        }
      >
        {localAttachment
          ? t('challenge.button.download.attachment')
          : t('common.button.open', { defaultValue: 'Open' })}
      </Button>
    </div>
  ) : (
    <div className={classes.emptyRow}>
      <Icon path={mdiDownloadBoxOutline} size={0.9} aria-hidden />
      <Text>{t('challenge.content.attachments.empty', { defaultValue: 'No attachments for this challenge.' })}</Text>
    </div>
  )

  const hintsContent =
    challenge?.hints && challenge.hints.length > 0 ? (
      <ol className={classes.hintList}>
        {challenge.hints.map((hint, position) => {
          const hintIndex = hint.index ?? position
          return (
            <li key={hintIndex} className={classes.hint}>
              <span className={classes.hintIndex}>{(hintIndex + 1).toString().padStart(2, '0')}</span>
              {hint.isUnlocked ? (
                <InlineMarkdown size="sm" source={hint.content ?? ''} />
              ) : (
                <Button
                  size="xs"
                  variant="light"
                  color="yellow"
                  disabled={disabled || solved || !onUnlockHint}
                  loading={unlockingHintIndex === hintIndex}
                  onClick={() => onUnlockHint?.(hintIndex)}
                >
                  {t('challenge.button.unlock_hint', {
                    defaultValue: `Unlock hint (-${hint.penalty} points)`,
                    penalty: hint.penalty,
                  })}
                </Button>
              )}
            </li>
          )
        })}
      </ol>
    ) : (
      <div className={classes.emptyRow}>
        <Icon path={mdiLightbulbOnOutline} size={0.9} aria-hidden />
        <Text>{t('challenge.content.hints.empty', { defaultValue: 'No hints are available.' })}</Text>
      </div>
    )

  const attemptsInfo = useMemo(() => {
    if (typeof challenge?.attempts !== 'number' || solved) return null

    if (deadlineTime && isDeadlinePassed) {
      return t('challenge.content.deadline.expired', {
        deadline: deadlineTime.locale(locale).format('L LTS'),
      })
    }

    if (challenge?.limit) {
      const remaining = challenge.limit - challenge.attempts
      return remaining > 0
        ? t('challenge.content.attempts.remaining', { remaining })
        : t('challenge.content.attempts.exhausted')
    }

    return t('challenge.content.attempts.count', { count: challenge.attempts })
  }, [challenge?.attempts, challenge?.limit, solved, deadlineTime, locale, isDeadlinePassed, t])

  const inputValue = solved
    ? t('challenge.content.already_solved')
    : isLimitReached
      ? t('challenge.content.attempts.placeholder')
      : flag

  const canSubmitDespiteDeadline = !isDeadlinePassed || (gameEnded && practiceMode)
  const inputDisabled = disabled || solved || isLimitReached || !canSubmitDespiteDeadline

  const submitSection = (
    <DetailSection title={t('challenge.button.submit_flag')} icon={mdiFlagOutline} className={classes.submitSection}>
      <form
        onSubmit={(event) => {
          event.preventDefault()
          if (!solved && canSubmitDespiteDeadline) onSubmitFlag()
        }}
      >
        <Stack gap="sm">
          <TextInput
            label={t('challenge.content.flag.label', { defaultValue: 'Flag' })}
            description={attemptsInfo}
            placeholder={placeholder}
            value={inputValue}
            disabled={inputDisabled}
            autoComplete="off"
            spellCheck={false}
            onChange={(event) => setFlag(event.currentTarget.value)}
            classNames={{ input: [misc.ffmono, classes.flagInput].filter(Boolean).join(' ') }}
          />
          <Button
            fullWidth
            type="submit"
            className={classes.submitButton}
            disabled={inputDisabled}
            loading={submissionState === 'pending'}
            leftSection={submissionState === 'pending' ? undefined : <Icon path={mdiFlagOutline} size={0.82} />}
          >
            {t('challenge.button.submit_flag')}
          </Button>
        </Stack>
      </form>

      {submissionState !== 'idle' && submissionMessage && (
        <div
          className={classes.submissionFeedback}
          data-state={submissionState}
          role={submissionState === 'error' || submissionState === 'rate-limited' ? 'alert' : 'status'}
          aria-live="polite"
        >
          <Icon path={SUBMISSION_ICON[submissionState]} size={0.86} aria-hidden />
          <Text>{submissionMessage}</Text>
        </div>
      )}

      {(locked || deadlineBlocksSubmission) && !solved && (
        <div className={classes.lockedNotice} role="status">
          <StatusDot
            status="locked"
            label={t('challenge.content.locked', { defaultValue: 'Submissions are locked.' })}
          />
        </div>
      )}

      {deadlineTime && <ChallengeDeadlineNotice deadline={deadlineTime} onExpiredChange={setIsDeadlinePassed} />}
    </DetailSection>
  )

  const details = (
    <div className={classes.detailGrid}>
      <div className={classes.contentColumn}>
        <DetailSection
          title={t('challenge.content.description', { defaultValue: 'Description' })}
          icon={mdiFileDocumentOutline}
        >
          {challenge?.content ? (
            <div className={classes.markdown}>
              <Markdown source={challenge.content} />
            </div>
          ) : (
            <div className={classes.emptyRow}>
              <Icon path={mdiFileDocumentOutline} size={0.9} aria-hidden />
              <Text>{t('challenge.content.description.empty', { defaultValue: 'No description provided.' })}</Text>
            </div>
          )}
        </DetailSection>

        <DetailSection
          title={t('challenge.content.attachments', { defaultValue: 'Attachments' })}
          icon={mdiDownloadBoxOutline}
        >
          {attachmentContent}
        </DetailSection>

        <DetailSection title={t('challenge.content.hints', { defaultValue: 'Hints' })} icon={mdiLightbulbOnOutline}>
          {hintsContent}
        </DetailSection>
      </div>

      <aside
        className={classes.actionRail}
        aria-label={t('challenge.content.actions', { defaultValue: 'Challenge actions' })}
      >
        {submitSection}
        {isContainer && (
          <InstanceEntry
            variant="v2"
            label={`${challenge?.title ?? ''} @ ${gameTitle ?? ''}`}
            context={challenge?.context ?? {}}
            onCreate={onCreate}
            onExtend={onExtend}
            onDestroy={onDestroy}
            disabled={disabled}
            action={instanceAction}
            failed={instanceFailed}
          />
        )}
      </aside>
    </div>
  )

  return (
    <Modal.Root
      size="min(74rem, calc(100vw - 2rem))"
      {...modalProps}
      onClose={() => {
        setFlag('')
        modalProps.onClose()
      }}
      centered
      classNames={classes}
    >
      <Modal.Overlay />
      <Modal.Content>
        <Modal.Header>
          <Modal.Title>{header}</Modal.Title>
          {withCloseButton && <Modal.CloseButton {...closeButtonProps} aria-label={t('common.button.close')} />}
        </Modal.Header>
        <Modal.Body>
          {loading ? (
            <LoadingState
              variant="panel"
              count={6}
              label={t('challenge.content.loading', { defaultValue: 'Loading challenge details' })}
            />
          ) : loadError ? (
            <ErrorState
              title={t('challenge.content.error.title', { defaultValue: 'Challenge unavailable' })}
              message={t('challenge.content.error.message', {
                defaultValue: 'The challenge details could not be loaded.',
              })}
              onRetry={onRetry}
            />
          ) : (
            details
          )}
        </Modal.Body>
      </Modal.Content>
    </Modal.Root>
  )
}
