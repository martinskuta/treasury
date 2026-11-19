import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// API_HTTPS / API_HTTP are injected by the AppHost (.WithReference(api), resource name "api")
const apiTarget = process.env.API_HTTPS || process.env.API_HTTP

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: apiTarget ? { '/api': { target: apiTarget, changeOrigin: true, secure: false } } : undefined,
  },
})
