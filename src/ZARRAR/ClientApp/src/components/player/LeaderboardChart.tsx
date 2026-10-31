import { useMantineColorScheme } from '@mantine/core'
import dayjs from 'dayjs'
import type { EChartsOption, LineSeriesOption } from 'echarts'
import { FC, useMemo } from 'react'
import { EchartsContainer } from '@Components/charts/EchartsContainer'
import {
  zarrarChartBase,
  zarrarGrid,
  zarrarLegend,
  zarrarTooltip,
  zarrarXAxis,
  zarrarYAxis,
  getZarrarChartTheme,
} from '@Utils/zarrar/chartTheme'
import { TimeLine, TopTimeLine } from '@Api'

interface LeaderboardChartProps {
  teams: TopTimeLine[]
  /** Game start/end. The API types these as string | number, and dayjs accepts either. */
  start: string | number
  end: string | number
}

/**
 * Score progression for the leaderboard's graph view.
 *
 * Distinct from ScoreTimeLine, which calls useGameScoreboard internally and therefore pulls the
 * entire scoreboard — every team, every solve, every challenge — just to draw ten lines. Here the
 * series arrive as a prop, fetched from /Leaderboard/Timeline only once the graph is opened, so
 * the default podium view never pays for the chart's data.
 *
 * The option construction deliberately mirrors ScoreTimeLine so both charts read identically.
 */
export const LeaderboardChart: FC<LeaderboardChartProps> = ({ teams, start, end }) => {
  const { colorScheme } = useMantineColorScheme()

  const option = useMemo<EChartsOption>(() => {
    const theme = getZarrarChartTheme()

    const startAt = dayjs(start)
    const endAt = dayjs(end)
    const now = dayjs()
    // Lines are drawn to "now" while a game is live, so a leader who has stopped solving still
    // shows a flat line to the present rather than stopping mid-chart.
    const last = now.isBefore(startAt) ? startAt : now.isAfter(endAt) ? endAt : now

    const series: LineSeriesOption[] = teams.map((team, index) => {
      const finalScore = team.items.at(-1)?.score ?? 0
      const data: Array<[number, number]> = [
        [startAt.valueOf(), 0],
        ...team.items.map((timeline: TimeLine): [number, number] => [timeline.time, timeline.score]),
        [last.valueOf(), finalScore],
      ]

      return {
        type: 'line',
        name: team.name,
        step: 'end',
        data,
        showSymbol: false,
        symbol: 'circle',
        symbolSize: 6,
        lineStyle: { width: index === 0 ? 2.5 : 1.75 },
        emphasis: { focus: 'series' },
      }
    })

    return {
      ...zarrarChartBase(theme),
      animationDuration: 350,
      animationDurationUpdate: 200,
      grid: { ...zarrarGrid(theme), top: theme.space.sm, right: theme.space.sm, bottom: 72, left: theme.space.sm },
      tooltip: {
        ...zarrarTooltip(theme),
        valueFormatter: (value) => `${Number(value).toLocaleString()} pts`,
      },
      legend: { ...zarrarLegend(theme), left: 0, right: 0, bottom: 0 },
      xAxis: {
        // boundaryGap is omitted deliberately: the shared base sets it to a boolean, which a
        // time axis does not accept (it wants a two-value tuple). Spreading it unchanged fails
        // to typecheck against XAXisOption.
        ...(() => {
          const { boundaryGap: _drop, ...rest } = zarrarXAxis(theme)
          return rest
        })(),
        type: 'time',
        min: startAt.valueOf(),
        max: endAt.valueOf(),
        axisLabel: {
          ...zarrarXAxis(theme).axisLabel,
          hideOverlap: true,
          formatter: (value: number) => dayjs(value).format('MMM D'),
        },
      },
      yAxis: {
        // Same boundaryGap incompatibility as the x-axis above.
        ...(() => {
          const { boundaryGap: _drop, ...rest } = zarrarYAxis(theme)
          return rest
        })(),
        type: 'value',
        min: 0,
        name: 'Points',
        nameGap: theme.space.md,
        axisLabel: {
          ...zarrarYAxis(theme).axisLabel,
          formatter: (value: number) => value.toLocaleString(),
        },
      },
      series,
    }
  }, [teams, start, end, colorScheme])

  return <EchartsContainer option={option} style={{ width: '100%', height: 320 }} />
}
