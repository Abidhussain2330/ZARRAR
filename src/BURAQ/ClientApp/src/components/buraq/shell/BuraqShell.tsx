import { AppShell, Box, LoadingOverlay } from '@mantine/core'
import { FC, PropsWithChildren } from 'react'
import { BuraqFooter } from '@Components/buraq/shell/BuraqFooter'
import { BuraqTopBar } from '@Components/buraq/shell/BuraqTopBar'
import { DEFAULT_LOADING_OVERLAY } from '@Utils/Shared'
import { useIsMobile } from '@Utils/ThemeOverride'
import { useConfig } from '@Hooks/useConfig'
import classes from '@Styles/buraq/BuraqShell.module.css'
import reskin from '@Styles/buraq/reskin.module.css'
import '@Styles/buraq-v2-shell.css'

const MAIN_ID = 'buraq-shell-main'

export interface BuraqShellProps extends PropsWithChildren {
  width?: string
  isLoading?: boolean
  withFooter?: boolean
  withHeader?: boolean
  stickyHeader?: boolean
  openColorModal: () => void
  skin?: 'default' | 'reskin'
}

/**
 * Frame for the admin console and the game monitor.
 *
 * Players never mount this: every player page renders in `PlayerShell`. The
 * old "player mode" (left sidebar with Command Centre / Posts / Games / Team /
 * About, plus its mobile drawer) was the legacy player front end and has been
 * removed — admin sections are navigated through `AdminLayout`'s rail and
 * drawer instead.
 */
export const BuraqShell: FC<BuraqShellProps> = ({
  children,
  width,
  isLoading,
  withFooter = false,
  withHeader,
  stickyHeader = false,
  openColorModal,
  skin,
}) => {
  const { config } = useConfig()
  const isMobile = useIsMobile()

  return (
    <AppShell
      p={0}
      withBorder={false}
      zIndex="var(--buraq-z-sidebar)"
      className={`buraq-v2-app ${classes.shell} ${skin === 'reskin' ? reskin.scope : ''}`}
      header={{ height: 'var(--buraq-topbar-height)' }}
    >
      <a href={`#${MAIN_ID}`} className={classes.skipLink}>Skip to content</a>

      <BuraqTopBar openColorModal={openColorModal} compact={isMobile} />

      <AppShell.Main id={MAIN_ID} tabIndex={-1} className={`${classes.main} buraq-shell-main-content`}>
        <LoadingOverlay visible={isLoading ?? false} overlayProps={DEFAULT_LOADING_OVERLAY} />
        <div className={classes.column}>
          {withHeader && (
            <div className={classes.pageHeader} data-sticky={stickyHeader || undefined}>
              {config.slogan && <p className={classes.slogan}>&gt;&nbsp;{config.slogan}</p>}
            </div>
          )}
          <Box w={width ?? (isMobile ? '94%' : '80%')} className={classes.content}>
            {children}
          </Box>
        </div>
        {withFooter && <BuraqFooter />}
      </AppShell.Main>
    </AppShell>
  )
}
