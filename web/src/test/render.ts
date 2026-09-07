import { act, type ReactElement } from 'react'
import { createRoot } from 'react-dom/client'

;(globalThis as unknown as { IS_REACT_ACT_ENVIRONMENT: boolean }).IS_REACT_ACT_ENVIRONMENT = true

/// Мінімальний рендер для тестів компонентів: без testing-library, на голому
/// react-dom + act, у тому ж стилі, що й useSortingQueue.test.ts.
export async function render(element: ReactElement) {
  const container = document.createElement('div')
  document.body.appendChild(container)
  const root = createRoot(container)

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
      }),
  }
}

/// Пропускає макрозадачу: усі проміси api-моків і setState встигають доїхати.
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
