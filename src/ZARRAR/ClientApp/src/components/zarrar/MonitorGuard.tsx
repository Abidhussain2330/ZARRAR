import { FC, ReactNode, useEffect } from 'react'
import { Link, useLocation, useNavigate } from 'react-router'
import { RequireRole } from '@Components/WithRole'
import { useUser } from '@Hooks/useUser'
import { Role } from '@Api'
import classes from '@Styles/zarrar/Monitor.module.css'

/**
 * Lets Monitor and Admin accounts through to the ZARRAR monitor screens. Everyone else is sent to
 * the monitor login. Presentation only — every endpoint behind these screens is guarded by
 * [RequireMonitor] / [RequireAdmin] on the server.
 */
export const MonitorGuard: FC<{ children: ReactNode }> = ({ children }) => {
  const { user, error } = useUser()
  const navigate = useNavigate()
  const location = useLocation()

  useEffect(() => {
    if (error && (error as { status?: number }).status === 401)
      navigate(`/monitor/login?from=${encodeURIComponent(location.pathname)}`, { replace: true })
  }, [error, navigate, location.pathname])

  if (!user) return <div className={classes.center}>LOADING…</div>

  if (!RequireRole(Role.Monitor, user.role)) {
    return (
      <div className={classes.center} style={{ flexDirection: 'column', gap: 18, textAlign: 'center' }}>
        <div>MONITOR ACCESS ONLY</div>
        <Link to="/monitor/login" style={{ color: '#eef1f5', letterSpacing: '0.1em', fontSize: 15 }}>
          Sign in with a monitor account
        </Link>
      </div>
    )
  }

  return <>{children}</>
}

export default MonitorGuard
