import assert from 'node:assert/strict'
import fs from 'node:fs'
import test from 'node:test'

test('GitHub Actions validates ZARRAR before merging or pushing', () => {
  const workflow = fs.readFileSync(new URL('../../../../.github/workflows/ci.yml', import.meta.url), 'utf8')

  assert.match(workflow, /name:\s*ZARRAR CI/)
  assert.match(workflow, /pull_request:/)
  assert.match(workflow, /actions\/checkout@v4/)
  assert.match(workflow, /dotnet test src\/ZARRAR\.Test\/ZARRAR\.Test\.csproj/)
  assert.match(workflow, /node --test tests\/\*\.test\.mts/)
  assert.match(workflow, /pnpm run build/)
  assert.match(workflow, /docker build --file deploy\/production\/Dockerfile/)
  assert.match(workflow, /docker compose .* config --quiet/)
})
