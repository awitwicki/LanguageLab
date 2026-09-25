import { describe, expect, it } from 'vitest'
import theme from './index.css?raw'
import sorting from './screens/SortingScreen.css?raw'
import { contrastRatio, darkTokensOf, declarationOf, over, resolveColor, tokensOf, type Tokens } from './test/contrast'

const THEMES: { name: string; tokens: Tokens }[] = [
  { name: 'light', tokens: tokensOf(theme, ':root') },
  { name: 'dark', tokens: darkTokensOf(theme) },
]

/**
 * Every button whose label sits on a filled background. `page` is what the fill composites
 * over, which matters only for the translucent ones: the sorting buttons live on `.card`.
 */
const BUTTONS = [
  { selector: '.btn-known', css: sorting, page: 'var(--surface)' },
  { selector: '.btn-unknown', css: sorting, page: 'var(--surface)' },
  { selector: '.btn-primary', css: theme, page: 'var(--bg)' },
]

/** WCAG 2.1 AA for text below 18.66px bold / 24px regular — every button here. */
const AA = 4.5

describe('contrast helpers', () => {
  it('scores the reference pairs WCAG gives', () => {
    const white = resolveColor('#ffffff', {})
    const black = resolveColor('#000000', {})

    expect(contrastRatio(white, black)).toBeCloseTo(21, 5)
    expect(contrastRatio(white, white)).toBeCloseTo(1, 5)
  })

  it('reads a translucent color-mix as the source color at that alpha', () => {
    const mixed = resolveColor('color-mix(in srgb, var(--known) 18%, transparent)', { '--known': '#34c759' })

    expect(mixed).toEqual({ r: 0x34, g: 0xc7, b: 0x59, a: 0.18 })
  })
})

describe('button text contrast', () => {
  for (const { name, tokens } of THEMES) {
    for (const { selector, css, page } of BUTTONS) {
      const color = resolveColor(declarationOf(css, selector, 'color'), tokens)
      const background = (rule: string) => resolveColor(declarationOf(css, rule, 'background'), tokens)
      const onPage = (fill: ReturnType<typeof resolveColor>) => over(fill, resolveColor(page, tokens))

      it(`${selector} clears AA in the ${name} theme`, () => {
        expect(contrastRatio(color, onPage(background(selector)))).toBeGreaterThanOrEqual(AA)
      })

      it(`${selector} clears AA while hovered in the ${name} theme`, () => {
        expect(contrastRatio(color, onPage(background(`${selector}:hover:not(:disabled)`)))).toBeGreaterThanOrEqual(AA)
      })
    }
  }
})
