import assert from 'node:assert/strict'
import fs from 'node:fs'
import test from 'node:test'

const read = (path: string) => fs.readFileSync(new URL(path, import.meta.url), 'utf8')
const exists = (path: string) => fs.existsSync(new URL(path, import.meta.url))

test('the legacy Command Centre page is gone and players cannot be linked to it', () => {
  assert.equal(exists('../src/pages/command-centre.tsx'), false)
  assert.equal(exists('../src/components/buraq/shell/BuraqSidebar.tsx'), false)
  assert.equal(exists('../src/components/buraq/shell/BuraqMobileNav.tsx'), false)
  assert.equal(exists('../src/components/buraq/shell/useBuraqNavigation.ts'), false)
})

test('BuraqShell no longer has a player mode', () => {
  const shell = read('../src/components/buraq/shell/BuraqShell.tsx')
  assert.doesNotMatch(shell, /playerMode/)
  assert.doesNotMatch(shell, /BuraqSidebar|BuraqMobileNav/)
})

test('notice authoring lives inside the admin console', () => {
  assert.ok(exists('../src/pages/admin/notices/[postId].tsx'))
  const legacy = read('../src/pages/posts/[postId]/Edit.tsx')
  assert.match(legacy, /<Navigate to=\{`\/admin\/notices\//)
  assert.doesNotMatch(legacy, /AdminLayout|WithNavBar|Textarea/)
})

test('the notice editor never sends isPinned together with content fields', () => {
  // The backend treats any body carrying isPinned as a pin-only update and drops
  // title/summary/content/tags (Post.Update), so save bodies must omit it.
  const editor = read('../src/pages/admin/notices/[postId].tsx')
  const body = editor.match(/const buildBody = \(\): PostEditModel => \(\{([\s\S]*?)\}\)/)?.[1]
  assert.ok(body, 'expected buildBody')
  assert.doesNotMatch(body.replace(/\/\/.*$/gm, ''), /isPinned/)
})
