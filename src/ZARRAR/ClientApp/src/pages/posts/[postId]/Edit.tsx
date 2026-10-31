import { FC } from 'react'
import { Navigate, useParams } from 'react-router'

/**
 * Legacy URL. The notice editor moved into the admin console at
 * `/admin/notices/:postId`; this keeps old bookmarks and links working.
 * The target route is itself admin-only.
 */
const LegacyPostEditRedirect: FC = () => {
  const { postId } = useParams()
  return <Navigate to={`/admin/notices/${postId ?? 'new'}`} replace />
}

export default LegacyPostEditRedirect
