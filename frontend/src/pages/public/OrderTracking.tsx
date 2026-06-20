import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ArrowLeftIcon, CheckCircleIcon } from '@heroicons/react/24/outline'
import { ordersApi } from '../../api/orders'
import {
  OrderStatusLabel, PaymentMethodLabel, PaymentStatusLabel, type OrderDetail,
} from '../../types/api'
import { formatVnd, formatDateTime } from '../../lib/format'

const statusBadge: Record<number, string> = {
  1: 'bg-amber-900/40 text-amber-300',
  2: 'bg-blue-900/40 text-blue-300',
  3: 'bg-indigo-900/40 text-indigo-300',
  4: 'bg-emerald-900/40 text-emerald-300',
  5: 'bg-rose-900/40 text-rose-300',
}

export function OrderTracking() {
  const { id } = useParams<{ id: string }>()
  const [order, setOrder] = useState<OrderDetail | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!id) return
    setLoading(true)
    ordersApi.getById(Number(id))
      .then(setOrder)
      .catch(e => setError(e?.response?.status === 404 ? 'Không tìm thấy đơn hàng' : 'Lỗi tải đơn'))
      .finally(() => setLoading(false))
  }, [id])

  if (loading) return <div className="max-w-3xl mx-auto px-4 py-16 text-gray-400">Đang tải...</div>
  if (error || !order) return (
    <div className="max-w-3xl mx-auto px-4 py-16 text-center">
      <p className="text-gray-300">{error ?? 'Không có dữ liệu'}</p>
      <Link to="/my-orders" className="inline-block mt-4 text-blue-400 hover:underline">← Về đơn hàng của tôi</Link>
    </div>
  )

  return (
    <div className="max-w-3xl mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-6">
      <Link to="/my-orders" className="inline-flex items-center gap-1 text-sm text-blue-400 hover:text-blue-300">
        <ArrowLeftIcon className="w-4 h-4" /> Đơn hàng của tôi
      </Link>

      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-2xl font-bold text-gray-50 font-mono">{order.orderNumber}</h1>
          <p className="text-sm text-gray-400 mt-1">Đặt lúc {formatDateTime(order.orderDate)}</p>
        </div>
        <div className="flex flex-col items-end gap-2">
          <span className={`text-sm px-3 py-1 rounded ${statusBadge[order.status] ?? 'bg-gray-800 text-gray-300'}`}>
            {OrderStatusLabel[order.status] ?? `#${order.status}`}
          </span>
          <span className="text-xs text-gray-400">
            {PaymentMethodLabel[order.paymentMethod]} · {PaymentStatusLabel[order.paymentStatus]}
          </span>
        </div>
      </div>

      {/* Timeline from OrderEvent history */}
      <div className="bg-gray-900 border border-gray-800 rounded-lg p-6">
        <h2 className="text-lg font-semibold text-gray-50 mb-4">Tiến trình đơn hàng</h2>
        <ol className="relative border-l border-gray-700 ml-2">
          {order.events.map((ev, i) => (
            <li key={i} className="ml-6 pb-6 last:pb-0">
              <span className="absolute -left-[9px] flex items-center justify-center w-4 h-4 rounded-full bg-blue-600 ring-4 ring-gray-900">
                <CheckCircleIcon className="w-3 h-3 text-white" />
              </span>
              <div className="text-gray-100 text-sm font-medium">
                {OrderStatusLabel[ev.toStatus] ?? `#${ev.toStatus}`}
              </div>
              <div className="text-xs text-gray-500">
                {formatDateTime(ev.at)}{ev.reason ? ` · ${ev.reason}` : ''}
              </div>
            </li>
          ))}
        </ol>
      </div>

      <div className="grid sm:grid-cols-2 gap-4">
        {(order.shipFullName || order.shipAddress) && (
          <div className="bg-gray-900 border border-gray-800 rounded-lg p-5 text-sm">
            <h3 className="font-semibold text-gray-50 mb-2">Giao hàng</h3>
            {order.shipFullName && <div className="text-gray-300">{order.shipFullName}</div>}
            {order.shipPhone && <div className="text-gray-400">{order.shipPhone}</div>}
            {order.shipAddress && <div className="text-gray-400">{order.shipAddress}</div>}
            {order.note && <div className="text-gray-500 mt-1 italic">{order.note}</div>}
          </div>
        )}
        <div className="bg-gray-900 border border-gray-800 rounded-lg p-5 text-sm">
          <h3 className="font-semibold text-gray-50 mb-2">Sản phẩm ({order.items.length})</h3>
          <div className="space-y-1">
            {order.items.map(i => (
              <div key={i.productId} className="flex justify-between text-gray-300">
                <span className="truncate mr-2">{i.productName} × {i.quantity}</span>
                <span className="whitespace-nowrap">{formatVnd(i.lineTotal)}</span>
              </div>
            ))}
          </div>
          <div className="mt-3 pt-3 border-t border-gray-800 space-y-1 text-xs text-gray-400">
            <div className="flex justify-between"><span>Tạm tính</span><span>{formatVnd(order.subtotal)}</span></div>
            <div className="flex justify-between"><span>Vận chuyển</span><span>{order.shippingFee === 0 ? 'Miễn phí' : formatVnd(order.shippingFee)}</span></div>
            {order.taxAmount > 0 && <div className="flex justify-between"><span>VAT</span><span>{formatVnd(order.taxAmount)}</span></div>}
          </div>
          <div className="flex justify-between mt-2 pt-2 border-t border-gray-800 font-semibold text-gray-50">
            <span>Tổng</span>
            <span className="text-blue-400">{formatVnd(order.totalAmount)}</span>
          </div>
        </div>
      </div>
    </div>
  )
}
