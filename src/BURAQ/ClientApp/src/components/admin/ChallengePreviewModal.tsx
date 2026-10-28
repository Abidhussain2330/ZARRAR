import { ModalProps } from '@mantine/core'
import { useInputState } from '@mantine/hooks'
import { showNotification } from '@mantine/notifications'
import { mdiCheck } from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChallengeModal } from '@Components/ChallengeModal'
import { ChallengeCategoryItemProps } from '@Utils/Shared'
import { ChallengeDetailModel } from '@Api'

interface ChallengePreviewModalProps extends ModalProps {
  challenge: ChallengeDetailModel
  cateData: ChallengeCategoryItemProps
  disabled?: boolean
  onCreate: () => void
  onDestroy: () => void
}

export const ChallengePreviewModal: FC<ChallengePreviewModalProps> = (props) => {
  const { challenge, cateData, disabled, onCreate, onDestroy, ...modalProps } = props

  const { t } = useTranslation()
  const [flag, setFlag] = useInputState('')
  const [attempts, setAttempts] = useState(0)

  const onSubmit = () => {
    showNotification({
      color: 'teal',
      title: t('admin.notification.games.challenges.preview.flag_submitted'),
      message: flag,
      icon: <Icon path={mdiCheck} size={1} />,
    })
    setFlag('')
    setAttempts(attempts + 1)

    if (challenge.limit && attempts + 1 >= challenge.limit) {
      setTimeout(() => {
        setAttempts(0)
      }, 1000)
    }
  }

  return (
    <ChallengeModal
      {...modalProps}
      challenge={{ ...challenge, attempts }}
      cateData={cateData}
      disabled={disabled}
      flag={flag}
      setFlag={setFlag}
      onCreate={onCreate}
      onDestroy={onDestroy}
      onSubmitFlag={onSubmit}
    />
  )
}
