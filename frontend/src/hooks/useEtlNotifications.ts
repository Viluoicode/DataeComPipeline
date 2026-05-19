import { useEffect, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'

export interface EtlCompletedEvent {
  totalRowsProcessed: number
  watermark: number
  completedAt: string
  durationMs: number
}

type Status = 'disconnected' | 'connecting' | 'connected' | 'reconnecting'

/**
 * Subscribes to ETL completion events from the backend SignalR hub.
 * StrictMode-safe: ignores AbortError when the effect's cleanup races with start().
 *
 * @param onEtlCompleted Callback fired when backend pushes "etl-completed".
 *                       The latest callback is always used (stored in a ref).
 */
export function useEtlNotifications(onEtlCompleted?: (evt: EtlCompletedEvent) => void) {
  const [status, setStatus] = useState<Status>('disconnected')
  const [lastEvent, setLastEvent] = useState<EtlCompletedEvent | null>(null)

  // Keep latest callback in a ref so the effect doesn't re-run when the
  // caller passes a new function reference each render.
  const cbRef = useRef(onEtlCompleted)
  useEffect(() => { cbRef.current = onEtlCompleted }, [onEtlCompleted])

  useEffect(() => {
    const conn = new signalR.HubConnectionBuilder()
      .withUrl('/hub/etl')
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    conn.on('etl-completed', (evt: EtlCompletedEvent) => {
      setLastEvent(evt)
      cbRef.current?.(evt)
    })

    conn.onreconnecting(() => setStatus('reconnecting'))
    conn.onreconnected(()  => setStatus('connected'))
    conn.onclose(()         => setStatus('disconnected'))

    let cancelled = false
    setStatus('connecting')
    conn.start()
      .then(() => { if (!cancelled) setStatus('connected') })
      .catch(err => {
        // Ignore abort errors caused by StrictMode unmount or fast navigation
        if (cancelled) return
        if (err?.name === 'AbortError') return
        console.error('SignalR connect failed', err)
        setStatus('disconnected')
      })

    return () => {
      cancelled = true
      conn.stop().catch(() => { /* swallow abort during cleanup */ })
    }
  }, [])

  return { status, lastEvent }
}
