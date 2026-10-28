import { FC } from 'react'
import { useTranslation } from 'react-i18next'
import { AccountPreferences } from '@Components/account/AccountPreferences'
import { AccountSecurityPanel } from '@Components/account/AccountSecurityPanel'
import { WithRole } from '@Components/WithRole'
import { ErrorState, Loading, PageHead, arena } from '@Components/player/Arena'
import { PlayerShell } from '@Components/player/PlayerShell'
import { tryGetErrorMsg } from '@Utils/Shared'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useUser } from '@Hooks/useUser'
import { Role } from '@Api'
import classes from '@Styles/player/Settings.module.css'

const Settings: FC = () => {
  const { t } = useTranslation()
  const { user, error, mutate } = useUser()

  usePageTitle('Settings')

  return (
    <PlayerShell>
      <WithRole requiredRole={Role.User}>
        <PageHead
          eyebrow="ZARRAR / ACCOUNT"
          title="Settings"
          subtitle="Interface preferences follow your account across devices."
        />

        {!user && !error ? (
          <Loading label="Loading settings…" />
        ) : error ? (
          <ErrorState
            title="Settings unavailable"
            body={tryGetErrorMsg(error, t)}
            action={
              <button type="button" className={`${arena.btn} ${arena.btnGhost} bqFocusRing`} onClick={() => mutate()}>
                Retry
              </button>
            }
          />
        ) : user?.userId ? (
          <div className={classes.stack}>
            <AccountPreferences userId={user.userId} />

            <AccountSecurityPanel />

            {/* ApiTokenManager is deliberately NOT rendered here. API tokens are privileged
                operator credentials — the component itself refuses non-admin accounts — so they
                belong in the admin portal, not a player's settings. Removing it also avoids
                surfacing a 500 from ApiTokenController, which fails to construct because it
                depends on the concrete ConfigService while only IConfigService is registered. */}
          </div>
        ) : null}
      </WithRole>
    </PlayerShell>
  )
}

export default Settings
