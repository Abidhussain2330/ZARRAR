import assert from 'node:assert/strict'
import fs from 'node:fs'
import test from 'node:test'

test('event-notice edit and delete icon controls have localized accessible names', () => {
  const source = fs.readFileSync(new URL('../src/components/admin/GameNoticeEditCard.tsx', import.meta.url), 'utf8')

  assert.match(source, /aria-label=\{t\('admin\.button\.notices\.edit'\)\}/)
  assert.match(source, /aria-label=\{t\('admin\.button\.notices\.delete'\)\}/)
})
