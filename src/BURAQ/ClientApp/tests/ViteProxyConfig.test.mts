import assert from 'node:assert/strict'
import fs from 'node:fs'
import test from 'node:test'

test('the development /api proxy forwards websocket upgrades for container access', () => {
  const source = fs.readFileSync(new URL('../vite.config.mts', import.meta.url), 'utf8')

  assert.match(source, /['"]\/api['"]\s*:\s*\{\s*target:\s*TARGET,\s*ws:\s*true\s*\}/)
})
