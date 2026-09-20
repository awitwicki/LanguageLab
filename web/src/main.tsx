import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { markTelegramReady } from './auth/telegram'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)

// Telegram shows its own loading placeholder until told the page is up. A no-op elsewhere.
markTelegramReady()
