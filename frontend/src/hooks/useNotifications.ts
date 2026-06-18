import { useEffect } from 'react'
import * as signalR from '@microsoft/signalr'
import toast from 'react-hot-toast'

export interface OrderNotification {
  orderId: number
  orderNumber: string
  status: number
  paymentStatus: number
  message: string
}

const AUTH_KEY = 'ecom.auth'

function readToken(): string | null {
  try {
    const raw = localStorage.getItem(AUTH_KEY)
    return raw ? (JSON.parse(raw).accessToken ?? null) : null
  } catch {
    return null
  }
}

/**
 * Subscribes a logged-in customer to their own order notifications pushed by the
 * backend outbox dispatcher (/hub/notifications, group keyed by customer id).
 * Re-connects when the user changes (login/logout). No-op when logged out.
 *
 * @param userId current customer id (null when logged out) — used to (re)trigger.
 */
export function useNotifications(userId: number | null | undefined) {
  useEffect(() => {
    if (!userId) return
    const token = readToken()
    if (!token) return

    const conn = new signalR.HubConnectionBuilder()
      .withUrl('/hub/notifications', { accessTokenFactory: () => readToken() ?? '' })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    conn.on('order-notification', (n: OrderNotification) => {
      toast(n.message, { icon: '🔔', duration: 5000 })
    })

    let cancelled = false
    conn.start().catch(err => {
      if (cancelled || err?.name === 'AbortError') return
      console.error('Notifications hub connect failed', err)
    })

    return () => {
      cancelled = true
      conn.stop().catch(() => { /* swallow abort during cleanup */ })
    }
  }, [userId])
}
