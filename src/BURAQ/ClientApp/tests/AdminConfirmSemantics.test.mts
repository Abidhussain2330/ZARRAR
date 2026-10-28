import assert from 'node:assert/strict'
import fs from 'node:fs'
import test from 'node:test'

test('rich admin confirmation messages are not nested inside a paragraph', () => {
  const source = fs.readFileSync(new URL('../src/components/admin/shell/AdminConfirm.tsx', import.meta.url), 'utf8')

  assert.match(source, /typeof message === 'string' \|\| typeof message === 'number'/)
  assert.match(source, /<Box>\{message\}<\/Box>/)
  assert.match(source, /\) : \(\s*\/\/ Rich callers[\s\S]*?<Box>\{message\}<\/Box>/)
})
