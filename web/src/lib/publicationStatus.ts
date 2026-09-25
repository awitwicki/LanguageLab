import type { PublicationStatus } from '../api/client'

/** The order the admin's status picker lists them in. */
export const PUBLICATION_STATUSES: readonly PublicationStatus[] = ['private', 'pending', 'published', 'rejected']

const labels: Record<PublicationStatus, string> = {
  private: 'Private',
  pending: 'Pending',
  published: 'Published',
  rejected: 'Rejected',
}

export function publicationStatusLabel(status: PublicationStatus) {
  return labels[status]
}
