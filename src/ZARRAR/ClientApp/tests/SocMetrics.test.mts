import assert from 'node:assert/strict'
import test from 'node:test'
import { formatSocMegabytes, formatSocPercent } from '../src/utils/SocMetrics.ts'

test('nullable Windows SOC samples render as unavailable instead of throwing', () => {
  assert.equal(formatSocPercent(null), '—')
  assert.equal(formatSocPercent(undefined), '—')
  assert.equal(formatSocMegabytes(null), '—')
  assert.equal(formatSocMegabytes(undefined), '—')
})

test('available SOC samples retain the expected operator formatting', () => {
  assert.equal(formatSocPercent(12.345), '12.3%')
  assert.equal(formatSocMegabytes(64 * 1024 ** 2), '64 MB')
})
