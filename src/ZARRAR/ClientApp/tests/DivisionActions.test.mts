import assert from 'node:assert/strict'
import fs from 'node:fs'
import test from 'node:test'

test('division icon actions expose stable accessible names', () => {
  const card = fs.readFileSync(new URL('../src/components/admin/DivisionCard.tsx', import.meta.url), 'utf8')
  const drawer = fs.readFileSync(new URL('../src/components/admin/DivisionEditDrawer.tsx', import.meta.url), 'utf8')
  const confirm = fs.readFileSync(new URL('../src/components/ActionIconWithConfirm.tsx', import.meta.url), 'utf8')

  assert.match(card, /aria-label=\{t\('admin\.button\.divisions\.edit'\)\}/)
  assert.match(card, /aria-label=\{t\('common\.button\.copy'\)\}/)
  assert.match(drawer, /defaultValue: 'Generate invite code'/)
  assert.match(confirm, /aria-label=\{props\.ariaLabel \?\? props\.message\}/)
})
