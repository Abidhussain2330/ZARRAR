import { showNotification } from '@mantine/notifications'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { tryGetErrorMsg } from '@Utils/Shared'
import api, { TeamInfoModel } from '@Api'
import classes from '@Styles/player/Teams.module.css'

interface PlayerTeamCardProps {
  team: TeamInfoModel
  isCaptain: boolean
  onEdit: () => void
}

/**
 * A team as it appears on the Teams page.
 *
 * Replaces the Mantine TeamCard for the player surface. The substantive change is the invite
 * code: it used to live inside the edit modal, two clicks deep. Handing out invite codes is a
 * step in the core journey — create a team, invite people, then join an event — so for captains
 * the code is surfaced on the card itself, masked until revealed and copyable in one action.
 *
 * Members see no code, because only captains can issue them.
 */
export const PlayerTeamCard: FC<PlayerTeamCardProps> = ({ team, isCaptain, onEdit }) => {
  const { t } = useTranslation()
  const [code, setCode] = useState<string | null>(null)
  const [revealed, setRevealed] = useState(false)
  const [busy, setBusy] = useState(false)

  const teamName = team.name || 'Unnamed team'
  const members = team.members ?? []
  const visible = members.slice(0, 5)
  const overflow = Math.max(members.length - visible.length, 0)

  // Fetched lazily and only for captains: a non-captain has no use for it, and pulling codes
  // for every team on the page would be a pointless burst of requests.
  useEffect(() => {
    if (!isCaptain || !team.id || code !== null) return
    let active = true
    api.team
      .teamInviteCode(team.id)
      .then((res) => {
        if (active) setCode(res.data)
      })
      .catch(() => {
        /* leave the code hidden; the edit modal remains a fallback */
      })
    return () => {
      active = false
    }
  }, [isCaptain, team.id, code])

  const copy = async () => {
    if (!code) return
    try {
      await navigator.clipboard.writeText(code)
      showNotification({ color: 'teal', message: t('team.notification.invite_code.copied') })
    } catch {
      // Clipboard access is refused in some browsers over plain HTTP. Reveal the code so it
      // can still be selected by hand rather than failing silently.
      setRevealed(true)
      showNotification({ color: 'orange', message: 'Copy blocked — select the code manually.' })
    }
  }

  const regenerate = async () => {
    if (!team.id) return
    setBusy(true)
    try {
      const res = await api.team.teamUpdateInviteToken(team.id)
      setCode(res.data)
      setRevealed(true)
      showNotification({ color: 'teal', message: t('team.notification.invite_code.updated') })
    } catch (e) {
      showNotification({ color: 'red', message: tryGetErrorMsg(e, t) })
    } finally {
      setBusy(false)
    }
  }

  return (
    <article className={`${classes.card} ${isCaptain ? classes.cardCaptain : ''}`}>
      <button type="button" className={`${classes.cardOpen} bqFocusRing`} onClick={onEdit}>
        <span className={classes.identity}>
          {team.avatar ? (
            <img className={classes.teamAvatar} src={team.avatar} alt="" />
          ) : (
            <span className={classes.teamAvatarFallback} aria-hidden="true">
              {teamName.slice(0, 2).toUpperCase()}
            </span>
          )}

          <span className={classes.identityCopy}>
            <span className={classes.roleLine}>
              {isCaptain ? 'CAPTAIN' : 'MEMBER'}
              {team.locked && <span className={classes.lockTag}>LOCKED — ACTIVE GAME</span>}
            </span>
            <h2 className={classes.teamName} title={teamName}>
              {teamName}
            </h2>
            <p className={classes.teamBio}>{team.bio || 'No team biography yet.'}</p>
          </span>
        </span>
      </button>

      <div className={classes.roster}>
        <span className={classes.memberCount}>
          {members.length} {members.length === 1 ? 'MEMBER' : 'MEMBERS'}
        </span>
        <span className={classes.avatarGroup}>
          {visible.map((m) =>
            m.avatar ? (
              <img key={m.id} className={classes.memberAvatar} src={m.avatar} alt="" title={m.userName ?? ''} />
            ) : (
              <span key={m.id} className={classes.memberAvatarFallback} title={m.userName ?? ''}>
                {m.userName?.slice(0, 1).toUpperCase() || 'U'}
              </span>
            )
          )}
          {overflow > 0 && <span className={classes.memberOverflow}>+{overflow}</span>}
        </span>
      </div>

      {isCaptain && (
        <div className={classes.inviteBar}>
          <span className={classes.inviteLabel}>INVITE CODE</span>

          <code className={`${classes.inviteCode} ${revealed ? '' : classes.inviteMasked}`}>
            {code ?? 'loading…'}
          </code>

          <button
            type="button"
            className={`${classes.inviteBtn} bqFocusRing`}
            onClick={() => setRevealed((v) => !v)}
            disabled={!code}
          >
            {revealed ? 'HIDE' : 'SHOW'}
          </button>
          <button
            type="button"
            className={`${classes.inviteBtn} bqFocusRing`}
            onClick={copy}
            disabled={!code}
          >
            COPY
          </button>
          <button
            type="button"
            className={`${classes.inviteBtn} bqFocusRing`}
            onClick={regenerate}
            disabled={busy}
            title="Issues a new code and invalidates the old one"
          >
            {busy ? '…' : 'NEW'}
          </button>
        </div>
      )}
    </article>
  )
}
