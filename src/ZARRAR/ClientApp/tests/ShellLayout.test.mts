import assert from 'node:assert/strict'
import fs from 'node:fs'
import test from 'node:test'

test('ZARRAR main content preserves the fixed header and navbar offsets', () => {
  const css = fs.readFileSync(new URL('../src/styles/zarrar-v2-shell.css', import.meta.url), 'utf8')
  const rule = css.match(/\.zarrar-shell-main-content\s*\{([^}]+)\}/)?.[1]

  assert.ok(rule, 'expected the ZARRAR main-content compatibility rule')
  assert.doesNotMatch(rule, /\bpadding\s*:\s*0\b/)
  assert.match(rule, /padding-block-start:\s*var\(--app-shell-header-offset/)
  assert.match(rule, /padding-inline-start:\s*var\(--app-shell-navbar-offset/)
})
