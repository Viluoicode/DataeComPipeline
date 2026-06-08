import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
const proxy = {
  '/api': {
    target: 'http://localhost:5193',
    changeOrigin: true,
  },
  '/hangfire': {
    target: 'http://localhost:5193',
    changeOrigin: true,
  },
  '/hub': {
    target: 'http://localhost:5193',
    changeOrigin: true,
    ws: true, // websocket cho SignalR
  },
}

export default defineConfig({
  plugins: [react()],
  // allowedHosts: true → accept the random *.trycloudflare.com host so the
  // Cloudflare Tunnel demo isn't rejected with "host not allowed".
  server: { port: 5173, proxy, allowedHosts: true },
  preview: { port: 5173, proxy, allowedHosts: true },
})
