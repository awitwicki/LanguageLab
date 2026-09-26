import { describe, expect, it } from 'vitest'

/**
 * Component CSS is plain CSS, not modules: every file is loaded into one global sheet, so
 * two components that pick the same class name silently share it, and whichever stylesheet
 * the bundler puts last wins for both. That is not a theoretical hazard — `.scope-row` was
 * defined by both `ScopeRow` (the home screen's resume row: scope on the left, its action
 * on the right) and `ScopeProgress` (a 60px label beside a bar), and the label column won,
 * squeezing book titles into 60 pixels on every screen that showed one.
 *
 * Media queries make it worse rather than better: they add no specificity, so a phone
 * fallback in the losing file loses too, and the layout cannot be rescued by narrowing it.
 *
 * `index.css` is the shared layer and is meant to be reused (`.btn`, `.field`, `.footnote`),
 * so it is not part of this check — only one component claiming another's name is.
 */
const sheets = import.meta.glob('./**/*.css', { query: '?raw', import: 'default', eager: true }) as Record<
  string,
  string
>

/** A selector that is nothing but a class — `.scope-row`, not `.reader .scope-row`. */
const BARE_CLASS = /^\.[A-Za-z0-9_-]+$/

function bareClassesOf(css: string): Set<string> {
  const withoutComments = css.replace(/\/\*[\s\S]*?\*\//g, '')
  const found = new Set<string>()

  for (const [, selector] of withoutComments.matchAll(/([^{}]+)\{[^{}]*\}/g)) {
    for (const part of selector.split(',')) {
      const trimmed = part.trim()
      if (BARE_CLASS.test(trimmed)) found.add(trimmed.slice(1))
    }
  }

  return found
}

describe('component stylesheets', () => {
  it('reads bare class selectors and ignores scoped ones', () => {
    const classes = bareClassesOf(`
      /* .commented-out { color: red } */
      .card { padding: 8px }
      .card:hover, .tile { color: blue }
      .reader .scoped { color: green }
      @media (max-width: 800px) { .card { padding: 0 } }
    `)

    expect([...classes].sort()).toEqual(['card', 'tile'])
  })

  it('never lets two components claim the same class name', () => {
    const owners = new Map<string, string[]>()

    for (const [path, css] of Object.entries(sheets)) {
      if (path.endsWith('/index.css')) continue

      for (const name of bareClassesOf(css)) {
        owners.set(name, [...(owners.get(name) ?? []), path])
      }
    }

    const shared = [...owners.entries()]
      .filter(([, files]) => files.length > 1)
      .map(([name, files]) => `.${name} is defined by ${files.sort().join(' and ')}`)

    expect(shared).toEqual([])
  })
})
