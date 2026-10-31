import { FC } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { Link, useLocation } from 'react-router'
import { GateHeading, GateShell, gateClasses as classes } from '@Components/player/GateShell'
import { usePageTitle } from '@Hooks/usePageTitle'
import classesPending from '@Styles/player/Pending.module.css'

const EmailConfirmationPending: FC = () => {
  const location = useLocation()
  const email = (location.state as { email?: string } | null)?.email

  const { t } = useTranslation()

  usePageTitle(t('account.title.verify_email'))

  return (
    <GateShell slim backTo="/account/login" backLabel="Back to sign in">
      <div className={`${classes.statusIcon} ${classes.statusWait}`} aria-hidden="true">
        ✉
      </div>

      <GateHeading title={t('account.content.verify_email.title')} />

      <p className={classesPending.message}>
        <Trans i18nKey="account.content.verify_email.message" />
      </p>

      {email && <p className={classesPending.email}>{email}</p>}

      <div className={classesPending.helpBlock}>
        <p className={classesPending.helpTitle}>{t('account.content.verify_email.not_received.title')}</p>
        <ul className={classesPending.helpList}>
          <Trans i18nKey="account.content.verify_email.not_received.list">
            <li />
            <li />
          </Trans>
        </ul>
      </div>

      <Link className={`${classes.link} bqFocusRing`} to="/account/login">
        {t('account.anchor.login')}
      </Link>
    </GateShell>
  )
}

export default EmailConfirmationPending
