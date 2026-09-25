import { readFileSync } from 'node:fs'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

/// The app's version comes from the repo's Directory.Build.props — the same element MSBuild
/// gives the API and the bot — so the SPA and the assemblies never disagree. Read at build
/// time and baked in as the `__APP_VERSION__` constant (see src/lib/version.ts).
function readAppVersion(): string {
  const props = readFileSync(new URL('../Directory.Build.props', import.meta.url), 'utf8')
  const match = /<Version>\s*([^<]+?)\s*<\/Version>/.exec(props)
  if (!match) throw new Error('Directory.Build.props has no <Version> element')
  return match[1]
}

export default defineConfig({
  plugins: [react()],
  define: {
    __APP_VERSION__: JSON.stringify(readAppVersion()),
  },
  server: {
    proxy: {
      '/api': 'http://localhost:5080',
    },
  },
})
