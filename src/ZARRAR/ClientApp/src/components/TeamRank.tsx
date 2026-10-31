import {
  Avatar,
  Badge,
  Card,
  CardProps,
  Group,
  PasswordInput,
  Progress,
  Skeleton,
  Stack,
  Text,
  Title,
} from '@mantine/core'
import { useClipboard } from '@mantine/hooks'
import { showNotification } from '@mantine/notifications'
import { mdiAccountGroup, mdiCheck, mdiExclamationThick, mdiKey } from '@mdi/js'
import { Icon } from '@mdi/react'
import cx from 'clsx'
import { FC, useEffect, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useParams } from 'react-router'
import { Panel, StatusDot } from '@Components/zarrar'
import { ErrorCodes } from '@Utils/Shared'
import { useIsMobile } from '@Utils/ThemeOverride'
import { useGameTeamInfo } from '@Hooks/useGame'
import misc from '@Styles/Misc.module.css'
import rankingClasses from '@Styles/pages/OperativeRankings.module.css'

export interface TeamRankProps extends CardProps {
  appearance?: 'default' | 'rankings'
}

export const TeamRank: FC<TeamRankProps> = ({ appearance = 'default', ...props }) => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')
  const navigate = useNavigate()
  const { teamInfo, game, error } = useGameTeamInfo(numId)

  const clipboard = useClipboard()
  const isMobile = useIsMobile(1080)

  const { t } = useTranslation()

  const solved = (teamInfo?.rank?.solvedCount ?? 0) / (teamInfo?.challengeCount ?? 1)

  const division = useMemo(() => {
    if (teamInfo?.rank?.divisionId && game?.divisions) {
      const division = game.divisions.find((d) => d.id === teamInfo.rank!.divisionId)
      return division?.name ?? ''
    }

    return null
  }, [teamInfo?.rank?.divisionId, game?.divisions])

  useEffect(() => {
    if (error?.status === ErrorCodes.GameEnded) {
      navigate(`/games/${numId}`)
      showNotification({
        color: 'yellow',
        message: t('game.notification.ended'),
        icon: <Icon path={mdiExclamationThick} size={1} />,
      })
    }
  }, [error, navigate, numId, t])

  const rank = teamInfo?.rank

  const item = (label: string, value?: null | string | number) => (
    <Stack gap={2}>
      <Skeleton visible={!rank}>
        <Text ff="monospace" fw="bold">
          {value ?? '0'}
        </Text>
      </Skeleton>
      <Text size="xs" fw={500}>
        {label}
      </Text>
    </Stack>
  )

  if (appearance === 'rankings') {
    if (!rank) return null

    const rankLabel = rank.rank > 0 ? `#${rank.rank}` : 'Unranked'
    const solvedPercent =
      teamInfo?.challengeCount && teamInfo.challengeCount > 0
        ? Math.min(100, (rank.solvedCount / teamInfo.challengeCount) * 100)
        : 0

    return (
      <Panel
        eyebrow="CURRENT TEAM"
        title="Your standing"
        icon={<Icon path={mdiAccountGroup} size={0.82} />}
        variant="raised"
        className={[rankingClasses.teamPanel, props.className ?? ''].filter(Boolean).join(' ')}
      >
        <div className={rankingClasses.teamIdentity}>
          <Avatar alt="" size={54} radius="xl" src={rank.avatar} className={rankingClasses.teamAvatar}>
            {rank.name?.slice(0, 2).toUpperCase() || 'T'}
          </Avatar>
          <span>
            <strong>{rank.name || 'Team'}</strong>
            <small>{division || 'Open division'}</small>
          </span>
          <StatusDot status={rank.rank > 0 ? 'active' : 'offline'} label={rankLabel} size="sm" />
        </div>

        <dl className={rankingClasses.teamMetrics}>
          <div>
            <dt>Overall rank</dt>
            <dd>{rankLabel}</dd>
          </div>
          {division && (
            <div>
              <dt>Division rank</dt>
              <dd>{rank.divisionRank && rank.divisionRank > 0 ? `#${rank.divisionRank}` : '—'}</dd>
            </div>
          )}
          <div>
            <dt>Score</dt>
            <dd>{rank.score.toLocaleString()}</dd>
          </div>
          <div>
            <dt>Solves</dt>
            <dd>
              {rank.solvedCount}
              <small> / {teamInfo?.challengeCount ?? 0}</small>
            </dd>
          </div>
        </dl>

        <div className={rankingClasses.solveProgress}>
          <span>
            <span>Challenge completion</span>
            <strong>{Math.round(solvedPercent)}%</strong>
          </span>
          <progress value={solvedPercent} max={100}>
            {Math.round(solvedPercent)}%
          </progress>
        </div>

        {rank.rank <= 0 && (
          <p className={rankingClasses.unrankedNote}>
            Your team is participating but has not earned an eligible overall rank yet.
          </p>
        )}
      </Panel>
    )
  }

  return (
    <Card {...props} shadow="sm">
      <Stack gap="xs">
        <Group gap="sm" wrap="nowrap">
          <Avatar alt="avatar" size={50} radius="md" src={rank?.avatar}>
            {rank?.name?.slice(0, 1) ?? 'T'}
          </Avatar>
          <Skeleton visible={!rank}>
            <Stack gap={2} align="flex-start">
              <Title order={3} lineClamp={1}>
                {rank?.name ?? 'Team'}
              </Title>
              {division && (
                <Badge size="xs" variant="outline">
                  {division}
                </Badge>
              )}
            </Stack>
          </Skeleton>
        </Group>
        <Group grow ta="center">
          {item(t('game.label.score_table.rank_total'), rank?.rank || '-')}
          {division && item(t('game.label.score_table.rank_division'), rank?.divisionRank)}
          {item(t('game.label.score_table.score'), rank?.score)}
          {item(t('game.label.score_table.solved_count'), rank?.solvedCount)}
        </Group>
        <Progress value={solved * 100} />
        {!isMobile && (
          <PasswordInput
            value={teamInfo?.teamToken ?? ''}
            readOnly
            leftSection={<Icon path={mdiKey} size={1} />}
            variant="unstyled"
            onClick={() => {
              clipboard.copy(teamInfo?.teamToken)
              showNotification({
                color: 'teal',
                message: t('team.notification.token.copied'),
                icon: <Icon path={mdiCheck} size={1} />,
              })
            }}
            classNames={{ innerInput: cx(misc.cCopy, misc.ffmono) }}
          />
        )}
      </Stack>
    </Card>
  )
}
