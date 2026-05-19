import axios from 'axios'

// Base axios instance.
// In dev, requests to /api/* are proxied by Vite to http://localhost:5193 (see vite.config.ts).
// In prod, point this to your deployed API origin via env var.
export const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL ?? '',
  headers: { 'Content-Type': 'application/json' },
})
