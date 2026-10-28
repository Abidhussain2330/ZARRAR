import assert from 'node:assert/strict'
import fs from 'node:fs'
import test from 'node:test'

test('hint delete action has an accessible name', () => {
  const source = fs.readFileSync(new URL('../src/components/HintList.tsx', import.meta.url), 'utf8')

  assert.match(source, /aria-label=\{t\('admin\.button\.challenges\.hint\.delete'/)
})
