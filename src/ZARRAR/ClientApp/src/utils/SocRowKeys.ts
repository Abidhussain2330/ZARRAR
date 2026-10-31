export interface SocFlagRowIdentity {
  fingerprint?: string | null
  teamId?: number | null
  challengeId?: number | null
  instanceId?: string | null
  assignmentId?: string | null
  artifactHash?: string | null
}

/**
 * Produces a stable, collision-free React key for one row returned by the SOC
 * flag-intelligence endpoint.
 *
 * A fingerprint is intentionally not a unique identifier: the same flag can
 * have historical and current rows, and a personalized assignment can appear
 * beside a retired challenge record. The row index is therefore the final
 * discriminator after the safe, non-secret identity fields.
 */
export const socFlagRowKey = (row: SocFlagRowIdentity, rowIndex: number): string =>
  [
    row.teamId ?? 'team',
    row.challengeId ?? 'challenge',
    row.instanceId ?? row.assignmentId ?? row.artifactHash ?? 'historical',
    row.fingerprint ?? 'no-fingerprint',
    rowIndex,
  ].join(':')
