import assert from 'node:assert/strict'
import test from 'node:test'
import { connectWsrxSafely } from '../src/components/WsrxConnection.ts'

test('optional unavailable WSRX daemon is swallowed without an error notification', async () => {
  const unavailable = new Error('optional daemon unavailable')
  let reported = 0

  await connectWsrxSafely(
    async () => { throw unavailable },
    { isDaemonUnavailable: (error) => error === unavailable, onError: () => { reported += 1 } }
  )

  assert.equal(reported, 0)
})

test('unexpected WSRX failures are reported once', async () => {
  const unexpected = new Error('unexpected')
  const reported: unknown[] = []

  await connectWsrxSafely(
    async () => { throw unexpected },
    { isDaemonUnavailable: () => false, onError: (error) => { reported.push(error) } }
  )

  assert.deepEqual(reported, [unexpected])
})
