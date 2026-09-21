import type { UserRole } from '../api/client'

/** Least to most trusted — the order the admin's role picker lists them in. */
export const ROLES: readonly UserRole[] = ['user', 'uploader', 'admin']

const labels: Record<UserRole, string> = {
  user: 'User',
  uploader: 'Uploader',
  admin: 'Admin',
}

export function roleLabel(role: UserRole) {
  return labels[role]
}

/** Mirrors the API's Importer policy: uploaders import books, admins can do anything. */
export function canImport(role: UserRole) {
  return role === 'uploader' || role === 'admin'
}
