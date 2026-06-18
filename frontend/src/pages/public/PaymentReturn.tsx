import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { CheckCircleIcon, XCircleIcon } from '@heroicons/react/24/outline'
import { ordersApi } from '../../api/orders'
import { PaymentStatusLabel, type OrderDetail } from '../../types/api'
import { formatVnd } from '../../lib/format'

/// Landing page after a VNPay/MoMo redirect. The API return endpoint already
/// applied the (idempotent) callback before bouncing us here, so we just read
/// the outcome and confirm against the order's persisted payment status.
export function PaymentReturn() {
  const [sp] = useSearchParams()
  const success = sp.get('success') === 'true'
  const orderId = sp.get('orderId')
  const [order, setOrder] = useState<OrderDetail | null>(null)

  useEffect(() => {
    if (!orderId) return
    ordersApi.getById(Number(orderId)).then(setOrder).catch(() => { /* ignore */ })
  }, [orderId])

  return (
    <div className="max-w-2xl mx-auto px-4 py-16 text-center">
      <div className={`inline-flex items-center justify-center w-16 h-16 rounded-full mb-4 ${
        success ? 'bg-emerald-900/40' : 'bg-rose-900/40'}`}>
        {success
          ? <CheckCircleIcon className="w-10 h-10 text-emerald-400" />
          : <XCircleIcon className="w-10 h-10 text-rose-400" />}
      </div>

      <h1 className="text-3xl font-bold text-gray-50">
        {success ? 'Thanh toán thành công!' : 'Thanh toán chưa hoàn tất'}
      </h1>
      <p className="mt-3 text-gray-400">
        {success
          ? 'Đơn hàng của bạn đã được thanh toán và xác nhận.'
          : 'Giao dịch bị huỷ hoặc thất bại. Bạn có thể thử thanh toán lại từ trang đơn hàng.'}
      </p>

      {order && (
        <div className="mt-6 inline-block bg-gray-900 border border-gray-800 rounded-lg px-6 py-4 text-left">
          <div className="text-sm text-gray-400">Đơn hàng</div>
          <div className="font-mono text-gray-200">{order.orderNumber}</div>
          <div className="mt-2 text-sm text-gray-400">Tổng tiền</div>
          <div className="text-xl font-bold text-blue-400">{formatVnd(order.totalAmount)}</div>
          <div className="mt-2 text-sm text-gray-400">Trạng thái thanh toán</div>
          <div className="text-gray-200">{PaymentStatusLabel[order.paymentStatus] ?? '?'}</div>
        </div>
      )}

      <div className="mt-8 flex justify-center gap-3">
        <Link to="/my-orders" className="px-6 py-2.5 rounded-md bg-blue-500 hover:bg-blue-600 text-white font-medium">
          Xem đơn hàng
        </Link>
        <Link to="/shop" className="px-6 py-2.5 rounded-md bg-gray-800 hover:bg-gray-700 text-gray-100 font-medium border border-gray-700">
          Tiếp tục shopping
        </Link>
      </div>
    </div>
  )
}
