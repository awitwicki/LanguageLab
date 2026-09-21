import { describe, expect, it } from 'vitest'
import { canImport, ROLES, roleLabel } from './roles'

describe('roles', () => {
  it('lets uploaders and admins import, not regular users', () => {
    expect(canImport('user')).toBe(false)
    expect(canImport('uploader')).toBe(true)
    expect(canImport('admin')).toBe(true)
  })

  it('names every role for the screen', () => {
    expect(roleLabel('user')).toBe('User')
    expect(roleLabel('uploader')).toBe('Uploader')
    expect(roleLabel('admin')).toBe('Admin')
  })

  // The picker lists roles from least to most trusted, so the eye reads it as a ladder.
  it('orders the roles by trust', () => {
    expect(ROLES).toEqual(['user', 'uploader', 'admin'])
  })
})
