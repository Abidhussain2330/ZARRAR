import { Button } from '@mantine/core'
import { mdiArrowLeft, mdiArrowRight } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, PropsWithChildren, ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation, useParams } from 'react-router'
import { AdminGameLayout } from '@Components/admin/AdminGameLayout'
import { useEditChallenges } from '@Hooks/useEdit'
import classes from '@Styles/admin/AdminTabs.module.css'

export interface AdminChallengeLayoutProps extends PropsWithChildren {
  title?: string
  description?: ReactNode
  actions?: ReactNode
  isLoading?: boolean
  subnav?: ReactNode
}

/**
 * Frame for the two challenge editor screens (properties and flags).
 *
 * Keeps the previous/next stepper — moving through a game's challenges without
 * returning to the list is the reason authoring a whole event is bearable — and
 * preserves the sub-path, so stepping from one challenge's flags lands on the
 * next challenge's flags rather than its properties.
 */
export const AdminChallengeLayout: FC<AdminChallengeLayoutProps> = ({
  title,
  description,
  actions,
  isLoading,
  subnav,
  children,
}) => {
  const { id, chalId } = useParams()
  const [numId, numCId] = [parseInt(id ?? '-1', 10), parseInt(chalId ?? '-1', 10)]
  const { challenges } = useEditChallenges(numId)
  const location = useLocation()
  const { t } = useTranslation()

  const index = challenges?.findIndex((challenge) => challenge.id === numCId) ?? -1
  const prev = index > 0 ? challenges?.[index - 1] : undefined
  const next = index >= 0 ? challenges?.[index + 1] : undefined
  const current = index >= 0 ? challenges?.[index] : undefined

  const restPath = location.pathname.replace(/\/admin\/games\/\d+\/challenges\/\d+/, '')

  return (
    <AdminGameLayout
      tab="challenges"
      title={title ?? current?.title ?? t('common.label.challenge')}
      description={description}
      actions={actions}
      isLoading={isLoading}
      subnav={subnav}
    >
      {children}

      <div className={classes.stepper}>
        <Button
          component={Link}
          variant="default"
          disabled={!prev}
          leftSection={<Icon path={mdiArrowLeft} size={0.8} />}
          to={prev?.id ? `/admin/games/${numId}/challenges/${prev.id}${restPath}` : '#'}
        >
          {t('admin.button.challenges.previous')}
        </Button>

        <p className={classes.stepperCurrent} title={current?.title}>
          {current?.title}
        </p>

        <Button
          component={Link}
          variant="default"
          disabled={!next}
          rightSection={<Icon path={mdiArrowRight} size={0.8} />}
          to={next?.id ? `/admin/games/${numId}/challenges/${next.id}${restPath}` : '#'}
        >
          {t('admin.button.challenges.next')}
        </Button>
      </div>
    </AdminGameLayout>
  )
}
