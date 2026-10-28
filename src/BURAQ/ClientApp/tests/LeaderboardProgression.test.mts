import assert from 'node:assert/strict'
import fs from 'node:fs'
import test from 'node:test'

const read = (path: string) => fs.readFileSync(new URL(path, import.meta.url), 'utf8')

test('score progression is lazy-loaded and mounts into a visible chart surface', () => {
  const scoreboard = read('../src/pages/games/[id]/Scoreboard.tsx')
  const chart = read('../src/components/player/LeaderboardChart.tsx')

  assert.match(scoreboard, /lazy\(\(\)\s*=>\s*import\(['"]@Components\/player\/LeaderboardChart['"]\)/)
  assert.match(scoreboard, /view === 'graph'[\s\S]*?leaderboard\/timeline\?count=\$\{GRAPH_TEAMS\}/)
  assert.doesNotMatch(scoreboard, /^import\s+\{\s*LeaderboardChart\s*\}/m)
  assert.match(chart, /style=\{\{\s*width:\s*'100%',\s*height:\s*320\s*\}\}/)
})
