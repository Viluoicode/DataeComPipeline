import { useRef, useState } from 'react'
import toast from 'react-hot-toast'
import {
  Card,
  Title,
  Text,
  Flex,
  Grid,
  Metric,
  Button,
  NumberInput,
  ProgressBar,
  Callout,
} from '@tremor/react'
import {
  BoltIcon,
  StopIcon,
  ArrowPathIcon,
  CubeTransparentIcon,
  TrashIcon,
} from '@heroicons/react/24/outline'
import { ordersApi, adminApi } from '../api/orders'
import { ConfirmDialog } from '../components/ConfirmDialog'
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
  const [confirmReset, setConfirmReset] = useState(false)
  const cancelRef = useRef(false)

  const appendLog = (msg: string) =>
    setLog(prev => [`[${new Date().toLocaleTimeString()}] ${msg}`, ...prev].slice(0, 80))

  async function fireOrders() {
    cancelRef.current = false
    setStatus('firing')
    setProgress(0); setSuccess(0); setFailed(0); setElapsedMs(0)
    appendLog(`🚀 Firing ${count} orders with concurrency ${concurrency}...`)

    const start = performance.now()
    let s = 0, f = 0

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
      try { await ordersApi.create(req); s++ }
      catch { f++ }
      finally {
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

  async function triggerEtl() {
    try {
      const r = await adminApi.triggerEtl()
      appendLog(`📦 ETL enqueued. JobId=${r.jobId}`)
      toast.success(`ETL job ${r.jobId} đã được enqueue`)
    } catch (e) {
      appendLog(`❌ ETL trigger failed: ${e}`)
      toast.error('ETL trigger failed')
    }
  }
  async function compress() {
    try {
      const r = await adminApi.compressColumnstore()
      appendLog(`🗜️ Compress enqueued. JobId=${r.jobId}`)
      toast.success(`Compress job ${r.jobId} đã được enqueue`)
    } catch (e) {
      appendLog(`❌ Compress failed: ${e}`)
      toast.error('Compress failed')
    }
  }
  async function doReset() {
    setConfirmReset(false)
    try {
      await adminApi.reset()
      appendLog('🧹 Reset done.')
      toast.success('Đã xoá Orders + OLAP fact + watermark')
    } catch (e) {
      appendLog(`❌ Reset failed: ${e}`)
      toast.error('Reset failed')
    }
  }

  const pct = count > 0 ? Math.round((progress / count) * 100) : 0
  const throughput = elapsedMs > 0 ? Math.round(success / (elapsedMs / 1000)) : 0

  return (
    <div className="p-6 space-y-6">
      <div>
        <Title className="!text-2xl">Stress Test</Title>
        <Text>Bắn N orders song song vào OLTP, sau đó trigger ETL để xem data chảy sang OLAP.</Text>
      </div>

      <Card>
        <Title>Controls</Title>
        <Grid numItemsMd={4} className="gap-4 mt-3">
          <div>
            <Text>Orders to fire</Text>
            <NumberInput
              min={1} max={100_000} value={count}
              onValueChange={setCount}
              disabled={status === 'firing'}
            />
          </div>
          <div>
            <Text>Concurrency</Text>
            <NumberInput
              min={1} max={200} value={concurrency}
              onValueChange={setConcurrency}
              disabled={status === 'firing'}
            />
          </div>
        </Grid>

        <Flex justifyContent="start" className="gap-3 mt-4 flex-wrap">
          <Button icon={BoltIcon} onClick={fireOrders} disabled={status === 'firing'}>Fire</Button>
          <Button icon={StopIcon} variant="secondary" color="rose"
                  onClick={() => { cancelRef.current = true }} disabled={status !== 'firing'}>
            Stop
          </Button>
          <Button icon={ArrowPathIcon} variant="secondary" onClick={triggerEtl}>Trigger ETL</Button>
          <Button icon={CubeTransparentIcon} variant="secondary" onClick={compress}>Force Compress</Button>
          <Button icon={TrashIcon} variant="light" color="rose" onClick={() => setConfirmReset(true)}>Reset Data</Button>
        </Flex>
      </Card>

      <Card>
        <Flex justifyContent="between" className="mb-2">
          <Text>Progress</Text>
          <Text>{progress.toLocaleString()} / {count.toLocaleString()} ({pct}%)</Text>
        </Flex>
        <ProgressBar value={pct} color="blue" />
      </Card>

      <Grid numItemsMd={4} className="gap-6">
        <Card decoration="top" decorationColor="emerald">
          <Text>Success</Text>
          <Metric>{success.toLocaleString()}</Metric>
        </Card>
        <Card decoration="top" decorationColor={failed > 0 ? 'rose' : 'gray'}>
          <Text>Failed</Text>
          <Metric>{failed.toLocaleString()}</Metric>
        </Card>
        <Card decoration="top" decorationColor="blue">
          <Text>Elapsed</Text>
          <Metric>{elapsedMs.toLocaleString()} ms</Metric>
        </Card>
        <Card decoration="top" decorationColor="indigo">
          <Text>Throughput</Text>
          <Metric>{throughput} <span className="text-base font-normal">ord/s</span></Metric>
        </Card>
      </Grid>

      {status === 'done' && (
        <Callout title="Run completed" color="emerald">
          Data đã ghi vào OLTP. Bấm <strong>Trigger ETL</strong> để đẩy sang OLAP,
          rồi <strong>Force Compress</strong> để rowgroups chuyển sang COMPRESSED → reports nhanh tối đa.
        </Callout>
      )}

      <Card>
        <Title>Activity log</Title>
        <pre className="mt-3 max-h-80 overflow-auto text-xs font-mono bg-gray-50 dark:bg-gray-900 p-3 rounded-md border border-gray-200 dark:border-gray-800">
{log.join('\n') || '(no activity yet)'}
        </pre>
      </Card>

      <ConfirmDialog
        open={confirmReset}
        title="Reset toàn bộ data?"
        message="Sẽ xoá hết Orders + OrderItems trong OLTP, fact.SalesOrderItem trong OLAP, và watermark. Customers/Products giữ nguyên. Không undo được."
        confirmLabel="Reset Data"
        destructive
        onConfirm={doReset}
        onCancel={() => setConfirmReset(false)}
      />
    </div>
  )
}
