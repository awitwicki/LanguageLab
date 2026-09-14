import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    // chapters.ts needs DOMParser; plain node does not have one.
    environment: 'jsdom',
    include: ['src/**/*.test.{ts,tsx}'],
  },
})
