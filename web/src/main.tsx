import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// Inter (400–700), self-hosted and loaded once for the whole app; unicode-range means only the subsets in use download.
import '@fontsource/inter/400.css'
import '@fontsource/inter/500.css'
import '@fontsource/inter/600.css'
import '@fontsource/inter/700.css'
import './index.css'
import App from './App.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
