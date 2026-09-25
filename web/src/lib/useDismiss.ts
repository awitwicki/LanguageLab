import { useEffect, type RefObject } from 'react'

/**
 * Closes an open overlay the two ways a user expects it to close: Escape, and a pointer
 * press outside `root`. Leave `root` out when something else already covers the outside —
 * a full-screen backdrop with its own click handler, say — and only the key is missing.
 */
export function useDismiss(open: boolean, onDismiss: () => void, root?: RefObject<HTMLElement | null>) {
  useEffect(() => {
    if (!open) {
      return
    }

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onDismiss()
      }
    }

    const onPointerDown = (event: MouseEvent) => {
      if (!root?.current?.contains(event.target as Node)) {
        onDismiss()
      }
    }

    document.addEventListener('keydown', onKeyDown)
    if (root) {
      document.addEventListener('mousedown', onPointerDown)
    }

    return () => {
      document.removeEventListener('keydown', onKeyDown)
      document.removeEventListener('mousedown', onPointerDown)
    }
  }, [open, onDismiss, root])
}
