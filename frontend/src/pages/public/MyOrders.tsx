import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  ClipboardDocumentListIcon,
  ChevronRightIcon,
  ShoppingBagIcon,
} from '@heroicons/react/24/outline'
import { ordersApi } from '../../api/orders'
import { useAuth } from '../../contexts/AuthContext'
import { OrderStatusLabel, type OrderListItem } from '../../types/api'
import { formatVnd, formatDateTime } from '../../lib/format'

const statusBadgeColor: Record<number, string> = {
  1: 'bg-amber-900/40 text-amber-300',     // Pending
  2: 'bg-blue-900/40 text-blue-300',       // Confirmed
  3: 'bg-indigo-900/40 text-indigo-300',   // Shipped
  4: 'bg-emerald-900/40 text-emerald-300', // Delivered
  5: 'bg-rose-900/40 text-rose-300',       // Cancelled
}

export function MyOrders() {
  const { user } = useAuth()
  const [orders, setOrders] = useState<OrderListItem[]>([])
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    if (!user) return
    setLoading(true)
    ordersApi.list({ customerId: user.customerId, pageSize: 50 })
      .then(r => setOrders(r.items))
      .finally(() => setLoading(false))
  }, [user])

  if (!user) {
    return (
      <div className="max-w-2xl mx-auto px-4 py-16 text-center">
        <ClipboardDocumentListIcon className="w-16 h-16 mx-auto text-gray-600 mb-4" />
        <h1 className="text-2xl font-bold text-gray-50">Cần đăng nhập</h1>
        <p className="mt-2 text-gray-400">Bạn cần login để xem lịch sử đơn hàng.</p>
        <Link
          to="/login"
          className="inline-block mt-6 px-6 py-2.5 rounded-md bg-blue-500 hover:bg-blue-600 text-white font-medium"
        >
          Đăng nhập
        </Link>
      </div>
    )
  }

  return (
    <div className="max-w-4xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      <div className="mb-6">
        <h1 className="text-3xl font-bold text-gray-50">My Orders</h1>
        <p className="mt-1 text-gray-400">Lịch sử đơn hàng của {user.fullName}</p>
      </div>

      {loading ? (
        <div className="space-y-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="h-24 bg-gray-900 border border-gray-800 rounded-lg animate-pulse" />
          ))}
        </div>
      ) : orders.length === 0 ? (
        <div className="text-center py-20 bg-gray-900 border border-gray-800 rounded-lg">
          <ShoppingBagIcon className="w-16 h-16 mx-auto text-gray-600 mb-4" />
          <p className="text-gray-400 mb-4">Chưa có đơn hàng nào.</p>
          <Link
            to="/shop"
            className="inline-block px-6 py-2.5 rounded-md bg-blue-500 hover:bg-blue-600 text-white font-medium"
          >
            Bắt đầu shopping
          </Link>
        </div>
      ) : (
        <div className="space-y-3">
          {orders.map(o => (
            <Link
              key={o.id}
              to={`/admin/orders/${o.id}`}
              className="block bg-gray-900 border border-gray-800 rounded-lg p-4 hover:border-gray-700 hover:bg-gray-800/40 transition"
            >
              <div className="flex items-start justify-between gap-4">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2 flex-wrap">
                    <span className="font-mono text-sm text-gray-200">{o.orderNumber}</span>
                    <span className={`text-xs px-2 py-0.5 rounded ${statusBadgeColor[o.status] ?? 'bg-gray-800 text-gray-300'}`}>
                      {OrderStatusLabel[o.status] ?? `#${o.status}`}
                    </span>
                  </div>
                  <div className="mt-1 text-xs text-gray-400">{formatDateTime(o.orderDate)}</div>
                  <div className="mt-2 text-sm text-gray-300">{o.itemCount} sản phẩm</div>
                </div>
                <div className="text-right">
                  <div className="font-semibold text-blue-400">{formatVnd(o.totalAmount)}</div>
                  <ChevronRightIcon className="w-5 h-5 text-gray-500 ml-auto mt-2" />
                </div>
              </div>
            </Link>
          ))}
        </div>
      )}
    </div>
  )
}
