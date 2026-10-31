import { useMantineColorScheme, useMantineTheme } from '@mantine/core'
import React, { FC, useState } from 'react'
import { ZarrarShell } from '@Components/zarrar/shell/ZarrarShell'
import { CustomColorModal } from '@Components/CustomColorModal'
import { Watermark } from '@Components/Watermark'
import { WithWiderScreen } from '@Components/WithWiderScreen'
import { useUser } from '@Hooks/useUser'
import { useSyncedPreferences } from '@Hooks/zarrar/useSyncedPreferences'

interface WithNavBarProps extends React.PropsWithChildren {
  width?: string
  minWidth?: number
  isLoading?: boolean
  withFooter?: boolean
  withHeader?: boolean
  stickyHeader?: boolean
  /** Opt a page into the blue/purple "reskin" palette (see reskin.module.css). Leave unset elsewhere. */
  skin?: 'default' | 'reskin'
}

export interface AppControlProps {
  openColorModal: () => void
}

/**
 * Every page in the platform renders inside this component, so its props are a
 * fixed contract: only the internals changed when the shell was rebranded to
 * ZARRAR, and the layout itself now lives in `ZarrarShell`.
 */
export const WithNavBar: FC<WithNavBarProps> = ({
  children,
  width,
  isLoading,
  minWidth,
  withFooter = false,
  withHeader,
  stickyHeader = false,
  skin,
}) => {
  const theme = useMantineTheme()
  const { colorScheme } = useMantineColorScheme()
  const { user } = useUser()
  useSyncedPreferences(user?.userId)
  const [colorModalOpened, setColorModalOpened] = useState(false)

  const openColorModal = () => setColorModalOpened(true)

  return (
    <WithWiderScreen minWidth={minWidth}>
      <Watermark
        text={user?.userId ?? ''}
        textColor={colorScheme === 'dark' ? theme.colors.gray[3] : theme.colors.gray[7]}
        rotate={-12}
        textSize={14}
        gutter={22}
        opacity={colorScheme === 'dark' ? 0.018 : 0.025}
      >
        <ZarrarShell
          width={width}
          isLoading={isLoading}
          withFooter={withFooter}
          withHeader={withHeader}
          stickyHeader={stickyHeader}
          openColorModal={openColorModal}
          skin={skin}
        >
          {children}
        </ZarrarShell>
        <CustomColorModal opened={colorModalOpened} onClose={() => setColorModalOpened(false)} />
      </Watermark>
    </WithWiderScreen>
  )
}
