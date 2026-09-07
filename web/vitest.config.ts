import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    // DOMParser потрібен chapters.ts; у чистому node його немає.
    environment: 'jsdom',
    include: ['src/**/*.test.ts'],
  },
})
