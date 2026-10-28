import assert from 'node:assert/strict'
import test from 'node:test'
import { socFlagRowKey } from '../src/utils/SocRowKeys.ts'

test('duplicate SOC flag fingerprints still receive distinct React row keys', () => {
  const repeatedHistoricalRow = {
    teamId: 2,
    challengeId: 9,
    fingerprint: 'fp:example-fingerprint',
    assignmentId: 'local-assignment',
  }

  const first = socFlagRowKey(repeatedHistoricalRow, 0)
  const second = socFlagRowKey(repeatedHistoricalRow, 1)

  assert.notEqual(first, second)
  assert.equal(first, socFlagRowKey(repeatedHistoricalRow, 0))
})

test('instance identity participates in the SOC flag row key', () => {
  const shared = { teamId: 2, challengeId: 7, fingerprint: 'fp:safe-fingerprint' }

  assert.notEqual(
    socFlagRowKey({ ...shared, instanceId: 'instance-a' }, 0),
    socFlagRowKey({ ...shared, instanceId: 'instance-b' }, 0),
  )
})
