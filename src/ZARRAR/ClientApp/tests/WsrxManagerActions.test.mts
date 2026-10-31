import assert from 'node:assert/strict'
import fs from 'node:fs'
import test from 'node:test'

test('optional WSRX icon actions expose stable accessible names', () => {
  const source = fs.readFileSync(new URL('../src/components/WsrxManager.tsx', import.meta.url), 'utf8')

  assert.match(source, /aria-label=\{t\('common\.button\.retry'\)\}/)
  assert.match(source, /aria-label=\{t\('wsrx\.button\.config'\)\}/)
})
