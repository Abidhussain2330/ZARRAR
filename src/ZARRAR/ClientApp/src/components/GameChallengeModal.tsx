import { ModalProps } from '@mantine/core'
import { notifications, showNotification, updateNotification } from '@mantine/notifications'
import { mdiCheck, mdiClose, mdiLoading } from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChallengeModal } from '@Components/ChallengeModal'
import type { ChallengeSubmissionState } from '@Components/ChallengeModal'
import type { InstanceAction } from '@Components/InstanceEntry'
import type { ZarrarDifficulty } from '@Components/zarrar'
import { encryptApiData } from '@Utils/Crypto'
import { ChallengeCategoryItemProps, showErrorMsg } from '@Utils/Shared'
import { useConfig } from '@Hooks/useConfig'
import { flagHit } from '@Utils/zarrar/flagHit'
import api, { AnswerResult, ChallengeType, SubmissionType } from '@Api'

interface GameChallengeModalProps extends ModalProps {
  gameId: number
  gameTitle: string
  gameEnded: boolean
  practiceMode?: boolean
  cateData: ChallengeCategoryItemProps
  title: string
  score: number
  difficulty?: ZarrarDifficulty
  solveCount?: number
  locked?: boolean
  challengeId: number
  status?: SubmissionType
}

const getResponseStatus = (error: unknown): number | undefined =>
  (error as { response?: { status?: number } } | undefined)?.response?.status

export const GameChallengeModal: FC<GameChallengeModalProps> = (props) => {
  const {
    gameId,
    gameTitle,
    gameEnded,
    practiceMode,
    challengeId,
    cateData,
    status,
    title,
    score,
    difficulty,
    solveCount,
    locked,
    ...modalProps
  } = props
  const { onClose, opened } = modalProps

  const {
    data: challenge,
    error: challengeError,
    mutate,
  } = api.game.useGameGetChallenge(
    gameId,
    challengeId,
    {
      refreshInterval: 120 * 1000,
    },
    opened
  )

  const { config } = useConfig()
  const { t } = useTranslation()

  const wrongFlagHints = useMemo(
    () =>
      t('challenge.content.wrong_flag_hints', {
        returnObjects: true,
      }) as string[],
    [t]
  )

  const isDynamic =
    challenge?.type === ChallengeType.StaticContainer || challenge?.type === ChallengeType.DynamicContainer

  const [submitId, setSubmitId] = useState(0)
  const [flag, setFlag] = useState('')
  const [solvedChallengeId, setSolvedChallengeId] = useState<number | null>(null)
  const [submissionState, setSubmissionState] = useState<ChallengeSubmissionState>('idle')
  const [submissionMessage, setSubmissionMessage] = useState('')
  const [instanceAction, setInstanceAction] = useState<InstanceAction>()
  const [instanceFailed, setInstanceFailed] = useState(false)
  const [unlockingHintIndex, setUnlockingHintIndex] = useState<number>()

  const isLimitReached = (challenge?.limit && (challenge.attempts ?? 0) >= challenge.limit) || false
  const interactionDisabled = submissionState === 'pending' || instanceAction !== undefined

  useEffect(() => {
    setSubmitId(0)
    setFlag('')
    setSubmissionState('idle')
    setSubmissionMessage('')
    setInstanceAction(undefined)
    setInstanceFailed(false)
    setUnlockingHintIndex(undefined)
  }, [challengeId])

  const onUnlockHint = async (hintIndex: number) => {
    if (!challengeId || interactionDisabled || unlockingHintIndex !== undefined) return
    setUnlockingHintIndex(hintIndex)

    try {
      await api.game.gameUnlockHint(gameId, challengeId, hintIndex)
      await mutate()
      showNotification({
        color: 'teal',
        title: t('challenge.notification.hint.unlocked.title', { defaultValue: 'Hint unlocked' }),
        message: t('challenge.notification.hint.unlocked.message', {
          defaultValue: 'Your team can now see this hint.',
        }),
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (error) {
      showErrorMsg(error, t)
    } finally {
      setUnlockingHintIndex(undefined)
    }
  }

  const onCreate = async () => {
    if (!challengeId || interactionDisabled) return
    setInstanceAction('creating')
    setInstanceFailed(false)

    try {
      const response = await api.game.gameCreateContainer(gameId, challengeId)
      await mutate(
        {
          ...challenge,
          context: {
            ...challenge?.context,
            closeTime: response.data.expectStopAt,
            instanceEntry: response.data.entry,
          },
        },
        { revalidate: false }
      )
      showNotification({
        color: 'teal',
        title: t('challenge.notification.instance.created.title'),
        message: t('challenge.notification.instance.created.message'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (error) {
      setInstanceFailed(true)
      showErrorMsg(error, t)
    } finally {
      setInstanceAction(undefined)
    }
  }

  const requestDestroy = useCallback(async () => {
    const latestChallenge = (await mutate()) ?? challenge
    if (!latestChallenge?.context?.instanceEntry) return

    await api.game.gameDeleteContainer(gameId, challengeId)
    await mutate(
      {
        ...latestChallenge,
        context: {
          ...latestChallenge.context,
          closeTime: null,
          instanceEntry: null,
        },
      },
      { revalidate: false }
    )
    setInstanceFailed(false)
    showNotification({
      color: 'teal',
      title: t('challenge.notification.instance.destroyed.title'),
      message: t('challenge.notification.instance.destroyed.message'),
      icon: <Icon path={mdiCheck} size={1} />,
    })
  }, [challenge, challengeId, gameId, mutate, t])

  const onDestroy = async () => {
    if (!challengeId || interactionDisabled) return
    setInstanceAction('destroying')

    try {
      await requestDestroy()
    } catch (error) {
      setInstanceFailed(true)
      showErrorMsg(error, t)
    } finally {
      setInstanceAction(undefined)
    }
  }

  const onExtend = async () => {
    if (!challengeId || interactionDisabled) return
    setInstanceAction('extending')

    try {
      const response = await api.game.gameExtendContainerLifetime(gameId, challengeId)
      await mutate(
        {
          ...challenge,
          context: {
            ...challenge?.context,
            closeTime: response.data.expectStopAt,
          },
        },
        { revalidate: false }
      )
      setInstanceFailed(false)
      showNotification({
        color: 'teal',
        title: t('challenge.notification.instance.extend.success.title'),
        message: t('challenge.notification.instance.extend.success.message'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (error) {
      setInstanceFailed(true)
      showErrorMsg(error, t)
    } finally {
      setInstanceAction(undefined)
    }
  }

  const onSubmit = async () => {
    if (!challengeId || !flag) {
      showNotification({
        color: 'red',
        message: t('challenge.notification.flag.empty'),
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    setSubmissionState('pending')
    setSubmissionMessage(t('challenge.notification.flag.submitted.message'))

    try {
      const response = await api.game.gameSubmit(gameId, challengeId, {
        flag: await encryptApiData(t, flag, config.apiPublicKey),
      })
      setSubmitId(response.data)
      notifications.hide('flag-submitted')
      showNotification({
        id: 'flag-submitted',
        color: 'orange',
        title: t('challenge.notification.flag.submitted.title'),
        message: t('challenge.notification.flag.submitted.message'),
        loading: true,
        autoClose: false,
      })

      const nextAttempt = (challenge?.attempts ?? 0) + 1
      const attempts = challenge?.limit && challenge.limit > 0 ? Math.min(nextAttempt, challenge.limit) : nextAttempt

      await mutate(
        {
          ...challenge,
          attempts,
        },
        { revalidate: false }
      )
    } catch (error) {
      const rateLimited = getResponseStatus(error) === 429
      setSubmissionState(rateLimited ? 'rate-limited' : 'error')
      setSubmissionMessage(
        rateLimited
          ? t('common.error.try_later', { defaultValue: 'Too many attempts. Please wait and try again.' })
          : t('challenge.notification.flag.error', { defaultValue: 'The flag could not be submitted. Try again.' })
      )
      showErrorMsg(error, t)
    }
  }

  const checkDataFlag = useCallback(
    async (id: number, result: string) => {
      if (result === AnswerResult.Accepted) {
        const acceptedMessage = gameEnded
          ? t('challenge.notification.flag.accepted.ended')
          : t('challenge.notification.flag.accepted.message')
        setSubmissionState('correct')
        setSubmissionMessage(acceptedMessage)
        flagHit('ok', challenge?.title)
        setSolvedChallengeId(challengeId)
        updateNotification({
          id: 'flag-submitted',
          color: 'teal',
          title: t('challenge.notification.flag.accepted.title'),
          message: acceptedMessage,
          icon: <Icon path={mdiCheck} size={1} />,
          autoClose: 8000,
          loading: false,
        })

        if (isDynamic && challenge?.context?.instanceEntry) {
          setInstanceAction('destroying')
          try {
            await requestDestroy()
          } catch (error) {
            setInstanceFailed(true)
            showErrorMsg(error, t)
          } finally {
            setInstanceAction(undefined)
          }
        }

        onClose()
        return
      }

      if (result === AnswerResult.WrongAnswer) {
        const wrongMessage = wrongFlagHints[Math.floor(Math.random() * wrongFlagHints.length)]
        setSubmissionState('incorrect')
        setSubmissionMessage(wrongMessage)
        flagHit('bad', challenge?.title ? `Wrong flag on ${challenge.title}` : 'Wrong flag')
        updateNotification({
          id: 'flag-submitted',
          color: 'red',
          title: t('challenge.notification.flag.wrong'),
          message: wrongMessage,
          icon: <Icon path={mdiClose} size={1} />,
          autoClose: 8000,
          loading: false,
        })
        return
      }

      const unknownMessage = t('challenge.notification.flag.unknown.message', { id })
      setSubmissionState('unknown')
      setSubmissionMessage(unknownMessage)
      updateNotification({
        id: 'flag-submitted',
        color: 'yellow',
        title: t('challenge.notification.flag.unknown.title'),
        message: unknownMessage,
        icon: <Icon path={mdiLoading} size={1} />,
        autoClose: false,
        withCloseButton: true,
      })
    },
    [challenge?.context?.instanceEntry, challenge?.title, challengeId, gameEnded, isDynamic, onClose, requestDestroy, t, wrongFlagHints]
  )

  useEffect(() => {
    if (!submitId || !opened) return

    const polling = window.setInterval(async () => {
      try {
        const response = await api.game.gameStatus(gameId, challengeId, submitId)
        if (response.data !== AnswerResult.FlagSubmitted) {
          setFlag('')
          window.clearInterval(polling)
          await checkDataFlag(submitId, response.data)
        }
      } catch (error) {
        window.clearInterval(polling)
        setFlag('')
        const rateLimited = getResponseStatus(error) === 429
        setSubmissionState(rateLimited ? 'rate-limited' : 'error')
        setSubmissionMessage(
          rateLimited
            ? t('common.error.try_later', { defaultValue: 'Too many requests. Please wait and try again.' })
            : t('challenge.notification.flag.status_error', {
                defaultValue: 'Submission status could not be checked. Try again.',
              })
        )
        showErrorMsg(error, t)
      }
    }, 500)

    return () => window.clearInterval(polling)
  }, [checkDataFlag, challengeId, gameId, opened, submitId, t])

  useEffect(() => {
    if (challengeId !== solvedChallengeId) return

    if (status !== SubmissionType.Unaccepted && status !== undefined) {
      setSolvedChallengeId(null)
    }
  }, [status, challengeId, solvedChallengeId])

  return (
    <ChallengeModal
      {...modalProps}
      gameTitle={gameTitle}
      challenge={challenge ?? { title, score }}
      cateData={cateData}
      difficulty={difficulty}
      solveCount={solveCount}
      locked={locked}
      loading={!challenge && !challengeError}
      loadError={!!challengeError}
      onRetry={() => void mutate()}
      solved={(status !== SubmissionType.Unaccepted && status !== undefined) || solvedChallengeId === challengeId}
      flag={flag}
      setFlag={setFlag}
      submissionState={submissionState}
      submissionMessage={submissionMessage}
      instanceAction={instanceAction}
      instanceFailed={instanceFailed}
      onCreate={() => void onCreate()}
      onDestroy={() => void onDestroy()}
      onSubmitFlag={() => void onSubmit()}
      onUnlockHint={(hintIndex) => void onUnlockHint(hintIndex)}
      unlockingHintIndex={unlockingHintIndex}
      disabled={interactionDisabled || isLimitReached}
      onExtend={() => void onExtend()}
      gameEnded={gameEnded}
      practiceMode={practiceMode}
    />
  )
}
