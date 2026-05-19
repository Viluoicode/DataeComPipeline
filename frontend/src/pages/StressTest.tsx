import { useRef, useState } from 'react'
import { ordersApi, adminApi } from '../api/orders'
import type { CreateOrderRequest } from '../types/api'

type Status = 'idle' | 'firing' | 'done' | 'error'

export function StressTest() {
  const [count, setCount] = useState(1000)
  const [concurrency, setConcurrency] = useState(20)
  const [status, setStatus] = useState<Status>('idle')
  const [progress, setProgress] = useState(0)
  const [success, setSuccess] = useState(0)
  const [failed, setFailed] = useState(0)
  const [elapsedMs, setElapsedMs] = useState(0)
  const [log, setLog] = useState<string[]>([])
  const cancelRef = useRef(false)

  const appendLog = (msg: string) =>
    setLog(prev => [`[${new Date().toLocaleTimeString()}] ${msg}`, ...prev].slice(0, 50))

  const fireOrders = async () => {
    cancelRef.current = false
    setStatus('firing')
    setProgress(0); setSuccess(0); setFailed(0); setElapsedMs(0)
    appendLog(`🚀 Bắn ${count} orders với concurrency ${concurrency}...`)

    const start = performance.now()
    let s = 0, f = 0

    // pool-based concurrency runner
    const indices = Array.from({ length: count }, (_, i) => i)
    const inFlight: Promise<void>[] = []

    const fireOne = async (i: number) => {
      const req: CreateOrderRequest = {
        customerId: (i % 5000) + 1,
        items: [
          { productId: (i % 1000) + 1, quantity: 1 + (i % 3) },
          { productId: ((i + 7) % 1000) + 1, quantity: 1 + (i % 2) },
        ],
      }
      try {
        await ordersApi.create(req)
        s++
      } catch {
        f++
      } finally {
        setProgress(s + f); setSuccess(s); setFailed(f)
      }
    }

    while (indices.length > 0 && !cancelRef.current) {
      while (inFlight.length < concurrency && indices.length > 0) {
        const i = indices.shift()!
        const p = fireOne(i).finally(() => {
          inFlight.splice(inFlight.indexOf(p), 1)
        })
        inFlight.push(p)
      }
      await Promise.race(inFlight)
    }
    await Promise.all(inFlight)

    const dt = Math.round(performance.now() - start)
    setElapsedMs(dt)
    setStatus(cancelRef.current ? 'idle' : 'done')
    appendLog(`✅ Done. Success=${s}, Failed=${f}, Time=${dt} ms (~${Math.round(s / (dt / 1000))} ord/s)`)
  }

  const triggerEtl = async () => {
    try {
      const r = await adminApi.triggerEtl()
      appendLog(`📦 ETL enqueued. JobId=${r.jobId}`)
    } catch (e) {
      appendLog(`❌ ETL trigger failed: ${e}`)
    }
  }

  const compress = async () => {
    try {
      const r = await adminApi.compressColumnstore()
      appendLog(`🗜️ Compress columnstore enqueued. JobId=${r.jobId}`)
    } catch (e) {
      appendLog(`❌ Compress failed: ${e}`)
    }
  }

  const reset = async () => {
    if (!confirm('Wipe Orders + OLAP fact + watermark?')) return
    try {
      await adminApi.reset()
      appendLog('🧹 Reset done.')
    } catch (e) {
      appendLog(`❌ Reset failed: ${e}`)
    }
  }

  const pct = count > 0 ? Math.round((progress / count) * 100) : 0

  return (
    <div className="stress">
      <h2>⚡ Stress Test</h2>
      <p className="muted">
        Bắn N orders song song vào OLTP, sau đó trigger ETL để xem dữ liệu chảy sang OLAP.
      </p>

      <div className="control-row">
        <label>
          Số orders:{' '}
          <input type="number" value={count} min={1} max={100000}
                 onChange={e => setCount(Number(e.target.value))} disabled={status === 'firing'} />
        </label>
        <label>
          Concurrency:{' '}
          <input type="number" value={concurrency} min={1} max={200}
                 onChange={e => setConcurrency(Number(e.target.value))} disabled={status === 'firing'} />
        </label>
        <button onClick={fireOrders} disabled={status === 'firing'}>
          🚀 Fire
        </button>
        <button onClick={() => { cancelRef.current = true }} disabled={status !== 'firing'}>
          ⏹ Stop
        </button>
        <button onClick={triggerEtl}>📦 Trigger ETL</button>
        <button onClick={compress}>🗜️ Force Compress</button>
        <button onClick={reset} className="danger">🧹 Reset</button>
      </div>

      <div className="progress-container">
        <div className="progress-bar" style={{ width: `${pct}%` }}>{pct}%</div>
      </div>

      <div className="stats-row">
        <div className="stat"><span className="stat-label">Progress</span> <span>{progress} / {count}</span></div>
        <div className="stat success"><span className="stat-label">Success</span> <span>{success}</span></div>
        <div className="stat error"><span className="stat-label">Failed</span> <span>{failed}</span></div>
        <div className="stat"><span className="stat-label">Elapsed</span> <span>{elapsedMs} ms</span></div>
        <div className="stat">
          <span className="stat-label">Throughput</span>
          <span>{elapsedMs > 0 ? Math.round(success / (elapsedMs / 1000)) : 0} ord/s</span>
        </div>
      </div>

      <h3>Activity log</h3>
      <pre className="log">{log.join('\n')}</pre>
    </div>
  )
}
