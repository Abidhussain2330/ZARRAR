import { ActionIcon, Avatar, Badge, Group, Stack, Text } from '@mantine/core'
import { mdiDownload } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC } from 'react'
import { Link } from 'react-router'
import { ScrollingText } from '@Components/ScrollingText'
import { useLanguage } from '@Utils/I18n'
import { WriteupInfo } from '@Api'
import misc from '@Styles/Misc.module.css'
import classes from '@Styles/admin/AdminWriteupCard.module.css'

interface TeamWriteupCardProps {
  writeup: WriteupInfo
  selected?: boolean
  onClick: () => void
  divisionName?: string
}

export const TeamWriteupCard: FC<TeamWriteupCardProps> = ({ writeup, selected, divisionName, onClick }) => {
  const { locale } = useLanguage()

  return (
    <div
      className={`${classes.card} ${misc.hoverCard} ${selected ? classes.selected : ''}`}
      onClick={onClick}
      data-no-move
    >
      <Group wrap="nowrap" gap={3} justify="space-between">
        <Group gap="sm" wrap="nowrap" justify="space-between" maw="calc(100% - 2rem)">
          <Avatar alt="avatar" src={writeup.team?.avatar} size="md">
            {writeup.team?.name?.slice(0, 1)}
          </Avatar>
          <Stack gap={0} justify="space-between" maw="calc(100% - 3rem)">
            <Group gap="xs">
              <Text size="0.8rem" lineClamp={1} className={classes.meta}>
                #{writeup.team?.id}
              </Text>
              {divisionName && (
                <Badge size="xs" variant="light">
                  {divisionName}
                </Badge>
              )}
            </Group>
            <ScrollingText size="md" fw={600} text={writeup.team?.name ?? ''} />
            <Text size="xs" lineClamp={1} className={classes.meta}>
              {dayjs(writeup.uploadTimeUtc).locale(locale).format('SLL LT')}
            </Text>
          </Stack>
        </Group>
        <ActionIcon variant="subtle" component={Link} target="_blank" to={writeup.url ?? '#'}>
          <Icon path={mdiDownload} size={1} />
        </ActionIcon>
      </Group>
    </div>
  )
}
