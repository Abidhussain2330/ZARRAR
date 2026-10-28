import { Center, Loader } from '@mantine/core'
import React, { FC, useEffect } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { useUserRole } from '@Hooks/useUser'
import { Role } from '@Api'

interface WithRoleProps {
  requiredRole: Role
  children?: React.ReactNode
  /**
   * Rendered instead of redirecting to `/404` when the signed-in user is
   * authenticated but under-privileged.
   *
   * Omitted, the historical behaviour is kept: bounce to `/404`, which does not
   * disclose that the route exists. Administration passes a real
   * "access denied" screen instead, because there the operator already knows
   * the section exists — they can see it in the navigation — and a 404 reads as
   * a broken link rather than as a missing permission. Either way the decision
   * is presentation only; every endpoint behind the route is still guarded by
   * `[RequireAdmin]` / `[RequireMonitor]` server-side.
   */
  fallback?: React.ReactNode
}

export const RoleMap = new Map<Role, number>([
  [Role.Admin, 3],
  [Role.Monitor, 1],
  [Role.User, 0],
  [Role.Banned, -1],
])

export const RequireRole = (requiredRole: Role, role?: Role | null) =>
  RoleMap.get(role ?? Role.User)! >= RoleMap.get(requiredRole)!

export const WithRole: FC<WithRoleProps> = ({ requiredRole, children, fallback }) => {
  const { role, error } = useUserRole()
  const navigate = useNavigate()
  const location = useLocation()

  const required = RoleMap.get(requiredRole)!
  const denied = !!role && RoleMap.get(role)! < required

  useEffect(() => {
    if (error && error.status === 401) {
      navigate(`/account/login?from=${location.pathname}`, { replace: true })
    }

    if (!role) return

    // With a fallback the caller renders the denial itself; without one, keep
    // the non-disclosing bounce to /404.
    if (denied && !fallback) navigate('/404')
  }, [role, error, required, denied, fallback, navigate, location.pathname])

  // Fail closed while the account profile (and therefore the role) is still
  // being resolved. Rendering children here used to mount protected admin
  // pages briefly and allowed their data requests to start for normal players.
  if (!role) {
    return (
      <Center h="calc(100vh - 32px)">
        <Loader />
      </Center>
    )
  }

  if (denied) {
    return fallback ? (
      <>{fallback}</>
    ) : (
      <Center h="calc(100vh - 32px)">
        {/* Loader, not the content: the redirect above is already in flight. */}
        <Loader />
      </Center>
    )
  }

  return <>{children}</>
}
