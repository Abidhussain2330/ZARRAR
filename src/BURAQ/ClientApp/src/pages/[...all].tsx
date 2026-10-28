import { FC, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate } from 'react-router'
import { GateHeading, GateShell, gateClasses } from '@Components/player/GateShell'
import { usePageTitle } from '@Hooks/usePageTitle'

/**
 * 404.
 *
 * Rendered without the admin console frame: anyone can land here (players,
 * anonymous visitors, and removed legacy URLs such as `/command-centre`), so
 * it must not expose admin chrome or navigation.
 */
const Error404: FC = () => {
  const navigate = useNavigate()
  const location = useLocation()

  const { t } = useTranslation()

  usePageTitle(t('common.title.404'))

  useEffect(() => {
    if (location.pathname !== '/404') {
      navigate('/404', { replace: true })
    }
  }, [location.pathname, navigate])

  return (
    <GateShell slim backTo="/" backLabel="Back to ZARRAR">
      <div className={gateClasses.bigCode} aria-hidden="true">
        <span className="zr-glitch" data-text="404">
          404
        </span>
        <small>SIGNAL LOST // ROUTE NOT FOUND</small>
      </div>
      <GateHeading title={t('common.content.404.title')} lede={t('common.content.404.text')} />
    </GateShell>
  )
}

export default Error404
