import assert from 'node:assert/strict'
import fs from 'node:fs'
import test from 'node:test'

test('BURAQ main content preserves the fixed header and navbar offsets', () => {
  const css = fs.readFileSync(new URL('../src/styles/buraq-v2-shell.css', import.meta.url), 'utf8')
  const rule = css.match(/\.buraq-shell-main-content\s*\{([^}]+)\}/)?.[1]

  assert.ok(rule, 'expected the BURAQ main-content compatibility rule')
  assert.doesNotMatch(rule, /\bpadding\s*:\s*0\b/)
  assert.match(rule, /padding-block-start:\s*var\(--app-shell-header-offset/)
  assert.match(rule, /padding-inline-start:\s*var\(--app-shell-navbar-offset/)
})
