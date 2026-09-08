import { act, type ReactElement } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach } from 'vitest'

;(globalThis as unknown as { IS_REACT_ACT_ENVIRONMENT: boolean }).IS_REACT_ACT_ENVIRONMENT = true

// Компоненти, змонтовані через render(), інколи вішають глобальні слухачі
// (напр. window.addEventListener('keydown', …)) — без явного unmount() між тестами
// вони лишаються живими між it() в межах файлу (document/window тут спільні) і
// ловлять події з наступних тестів. Тому кожен render() трекається й автоматично
// демонтується після відповідного тесту.
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

/// Мінімальний рендер для тестів компонентів: без testing-library, на голому
/// react-dom + act, у тому ж стилі, що й useSortingQueue.test.ts.
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
