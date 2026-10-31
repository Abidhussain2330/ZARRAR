import assert from 'node:assert/strict'
import fs from 'node:fs'
import test from 'node:test'

test('challenge enabled-state switch has a challenge-specific accessible name', () => {
  const source = fs.readFileSync(new URL('../src/components/admin/ChallengeEditCard.tsx', import.meta.url), 'utf8')

  assert.match(source, /aria-label=\{t\('admin\.action\.challenge_toggle_named'/)
  assert.match(source, /name: challenge\.title/)
})
