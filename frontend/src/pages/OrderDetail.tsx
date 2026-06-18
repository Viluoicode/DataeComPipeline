import { useEffect, useState } from 'react'
import { useParams, Link } from 'react-router-dom'
import toast from 'react-hot-toast'
import {
  Card,
  Title,
  Text,
  Flex,
  Table,
  TableHead,
  TableHeaderCell,
  TableBody,
  TableRow,
  TableCell,
  Badge,
  Button,
  type Color,
} from '@tremor/react'
import { ordersApi } from '../api/orders'
import {
  OrderStatusLabel,
  PaymentMethodLabel,
  PaymentStatusLabel,
  type OrderDetail as Detail,
} from '../types/api'

const statusColor: Record<number, Color> = {
  1: 'amber', 2: 'blue', 3: 'indigo', 4: 'emerald', 5: 'rose',
}

const paymentColor: Record<number, Color> = {
  1: 'gray', 2: 'amber', 3: 'emerald', 4: 'rose', 5: 'violet',
}

function formatVnd(n: number): string {
  return n.toLocaleString('vi-VN', { maximumFractionDigits: 0 }) + ' ₫'
}

export function OrderDetail() {
  const { id } = useParams<{ id: string }>()
  const [order, setOrder] = useState<Detail | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (!id) return
    setLoading(true)
    ordersApi.getById(Number(id))
      .then(setOrder)
      .catch(e => setError(e?.response?.status === 404
        ? 'Order not found'
        : e?.message ?? 'Failed to load order'))
      .finally(() => setLoading(false))
  }, [id])

  async function advance(status: number) {
    if (!order) return
    setSaving(true)
    try {
      const updated = await ordersApi.updateStatus(order.id, status)
      setOrder(updated)
      toast.success(`Đã chuyển sang ${OrderStatusLabel[status] ?? `#${status}`}`)
    } catch (e: unknown) {
      const err = e as { response?: { data?: { detail?: string } } }
      toast.error(err?.response?.data?.detail ?? 'Không đổi được trạng thái')
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <div className="p-6"><Text>Loading...</Text></div>
  if (error)   return <div className="p-6"><Card decoration="left" decorationColor="rose"><Text color="rose">{error}</Text></Card></div>
  if (!order)  return null

  return (
    <div className="p-6 space-y-6">
      <Flex justifyContent="between" alignItems="start">
        <div>
          <Link to="/admin/orders" className="text-sm text-blue-500 hover:underline">← Back to orders</Link>
          <Title className="!text-2xl mt-1">{order.orderNumber}</Title>
          <Text>Placed {new Date(order.orderDate).toLocaleString('vi-VN')}</Text>
        </div>
        <div className="flex flex-col items-end gap-2">
          <Badge size="lg" color={statusColor[order.status] ?? 'gray'}>
            {OrderStatusLabel[order.status] ?? `Status #${order.status}`}
          </Badge>
          <Badge color={paymentColor[order.paymentStatus] ?? 'gray'}>
            {PaymentMethodLabel[order.paymentMethod] ?? '?'} · {PaymentStatusLabel[order.paymentStatus] ?? '?'}
          </Badge>
        </div>
      </Flex>

      {/* Fulfilment actions — advance the order along the state machine */}
      {order.nextStatuses.length > 0 && (
        <Card>
          <Text>Cập nhật trạng thái</Text>
          <Flex justifyContent="start" className="gap-2 mt-3 flex-wrap">
            {order.nextStatuses.map(s => (
              <Button
                key={s}
                size="xs"
                loading={saving}
                color={s === 5 ? 'rose' : 'blue'}
                variant={s === 5 ? 'secondary' : 'primary'}
                onClick={() => advance(s)}
              >
                → {OrderStatusLabel[s] ?? `#${s}`}
              </Button>
            ))}
          </Flex>
        </Card>
      )}

      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        <Card>
          <Text>Customer</Text>
          <Title className="!text-lg">{order.customerName}</Title>
          <Text className="text-xs">{order.customerEmail}</Text>
        </Card>
        <Card>
          <Text>Items</Text>
          <Title className="!text-lg">{order.items.length}</Title>
        </Card>
        <Card decoration="top" decorationColor="emerald">
          <Text>Total</Text>
          <Title className="!text-lg">{formatVnd(order.totalAmount)}</Title>
        </Card>
      </div>

      {(order.shipFullName || order.shipAddress) && (
        <Card>
          <Title>Giao hàng</Title>
          <div className="mt-3 space-y-1 text-sm">
            {order.shipFullName && <div><span className="text-gray-500">Người nhận:</span> {order.shipFullName}</div>}
            {order.shipPhone && <div><span className="text-gray-500">SĐT:</span> {order.shipPhone}</div>}
            {order.shipAddress && <div><span className="text-gray-500">Địa chỉ:</span> {order.shipAddress}</div>}
            {order.note && <div><span className="text-gray-500">Ghi chú:</span> {order.note}</div>}
          </div>
        </Card>
      )}

      <Card>
        <Title>Line Items</Title>
        <Table className="mt-4">
          <TableHead>
            <TableRow>
              <TableHeaderCell>SKU</TableHeaderCell>
              <TableHeaderCell>Product</TableHeaderCell>
              <TableHeaderCell className="text-right">Qty</TableHeaderCell>
              <TableHeaderCell className="text-right">Unit Price</TableHeaderCell>
              <TableHeaderCell className="text-right">Line Total</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {order.items.map(i => (
              <TableRow key={`${i.productId}`}>
                <TableCell className="font-mono text-xs">{i.productSku}</TableCell>
                <TableCell>{i.productName}</TableCell>
                <TableCell className="text-right">{i.quantity}</TableCell>
                <TableCell className="text-right">{formatVnd(i.unitPrice)}</TableCell>
                <TableCell className="text-right font-medium">{formatVnd(i.lineTotal)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Card>

      {order.events.length > 0 && (
        <Card>
          <Title>Lịch sử trạng thái</Title>
          <div className="mt-4 space-y-3">
            {order.events.map((ev, idx) => (
              <div key={idx} className="flex items-start gap-3 text-sm">
                <div className="mt-1 w-2 h-2 rounded-full bg-blue-500 flex-shrink-0" />
                <div>
                  <div className="text-gray-200">
                    {ev.fromStatus ? `${OrderStatusLabel[ev.fromStatus]} → ` : ''}
                    <span className="font-medium">{OrderStatusLabel[ev.toStatus] ?? `#${ev.toStatus}`}</span>
                  </div>
                  <div className="text-xs text-gray-500">
                    {new Date(ev.at).toLocaleString('vi-VN')}
                    {ev.reason ? ` · ${ev.reason}` : ''}
                  </div>
                </div>
              </div>
            ))}
          </div>
        </Card>
      )}

      <Flex justifyContent="end">
        <Link to="/admin/orders">
          <Button variant="secondary">← Back</Button>
        </Link>
      </Flex>
    </div>
  )
}
