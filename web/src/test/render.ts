import { act, type ReactElement } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach } from 'vitest'

;(globalThis as unknown as { IS_REACT_ACT_ENVIRONMENT: boolean }).IS_REACT_ACT_ENVIRONMENT = true

// Components mounted through render() sometimes attach global listeners
// (e.g. window.addEventListener('keydown', …)) — without an explicit unmount() between
// tests they stay alive across it() blocks within a file (document/window are shared
// here) and catch events from later tests. So every render() is tracked and unmounted
// automatically after its test.
const mounted = new Set<{ root: Root; container: HTMLElement }>()

afterEach(async () => {
  const entries = [...mounted]
  mounted.clear()

  for (const entry of entries) {
    await act(async () => {
      entry.root.unmount()
    })
    entry.container.remove()
  }
})

/// Minimal render for component tests: no testing-library, bare
/// react-dom + act, in the same style as useSortingQueue.test.ts.
export async function render(element: ReactElement) {
  const container = document.createElement('div')
  document.body.appendChild(container)
  const root = createRoot(container)
  const entry = { root, container }
  mounted.add(entry)

  await act(async () => {
    root.render(element)
  })

  return {
    container,
    rerender: (next: ReactElement) =>
      act(async () => {
        root.render(next)
      }),
    unmount: () =>
      act(async () => {
        root.unmount()
        container.remove()
        mounted.delete(entry)
      }),
  }
}

/// Skips a macrotask: every api-mock promise and setState gets through.
export async function flush() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0))
  })
}

export function click(element: Element) {
  return act(async () => {
    element.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
  })
}
