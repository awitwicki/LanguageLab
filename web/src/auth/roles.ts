import type { UserRole } from '../api/client'

/** Least to most trusted — the order the admin's role picker lists them in. */
export const ROLES: readonly UserRole[] = ['user', 'admin']

const labels: Record<UserRole, string> = {
  user: 'User',
  admin: 'Admin',
}

export function roleLabel(role: UserRole) {
  return labels[role]
}

/** Mirrors UserRoles.CanPublishDirectly: only an admin's import skips the moderation queue. */
export function canPublishDirectly(role: UserRole) {
  return role === 'admin'
}
