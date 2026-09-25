import { defineConfig, mergeConfig } from 'vitest/config'
import viteConfig from './vite.config'

// On top of the app's Vite config, so tests see the same `define` constants (the app version).
export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      // chapters.ts needs DOMParser; plain node does not have one.
      environment: 'jsdom',
      include: ['src/**/*.test.{ts,tsx}'],
      // Off by default, which also empties `?raw` imports of a stylesheet —
      // index.contrast.test.ts reads the design tokens out of the CSS itself.
      css: true,
    },
  }),
)
