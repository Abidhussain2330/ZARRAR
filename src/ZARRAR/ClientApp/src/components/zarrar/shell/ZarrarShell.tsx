import { AppShell, Box, LoadingOverlay } from '@mantine/core'
import { FC, PropsWithChildren } from 'react'
import { ZarrarFooter } from '@Components/zarrar/shell/ZarrarFooter'
import { ZarrarTopBar } from '@Components/zarrar/shell/ZarrarTopBar'
import { DEFAULT_LOADING_OVERLAY } from '@Utils/Shared'
import { useIsMobile } from '@Utils/ThemeOverride'
import { useConfig } from '@Hooks/useConfig'
import classes from '@Styles/zarrar/ZarrarShell.module.css'
import reskin from '@Styles/zarrar/reskin.module.css'
import '@Styles/zarrar-v2-shell.css'

const MAIN_ID = 'zarrar-shell-main'

export interface ZarrarShellProps extends PropsWithChildren {
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
export const ZarrarShell: FC<ZarrarShellProps> = ({
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
      zIndex="var(--zarrar-z-sidebar)"
      className={`zarrar-v2-app ${classes.shell} ${skin === 'reskin' ? reskin.scope : ''}`}
      header={{ height: 'var(--zarrar-topbar-height)' }}
    >
      <a href={`#${MAIN_ID}`} className={classes.skipLink}>Skip to content</a>

      <ZarrarTopBar openColorModal={openColorModal} compact={isMobile} />

      <AppShell.Main id={MAIN_ID} tabIndex={-1} className={`${classes.main} zarrar-shell-main-content`}>
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
        {withFooter && <ZarrarFooter />}
      </AppShell.Main>
    </AppShell>
  )
}
