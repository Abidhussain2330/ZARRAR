import assert from 'node:assert/strict'
import fs from 'node:fs'
import test from 'node:test'

test('authenticated challenge attachment and artifact routes render as downloads', () => {
  const source = fs.readFileSync(new URL('../src/components/ChallengeModal.tsx', import.meta.url), 'utf8')

  assert.match(source, /protectedAttachment/)
  assert.match(source, /attachment\|artifact/)
  assert.match(source, /link\.startsWith\('\/assets'\) \|\| protectedAttachment/)
  assert.match(source, /challenge\.button\.download\.attachment/)
})
