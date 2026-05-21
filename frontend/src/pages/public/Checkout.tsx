import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import toast from 'react-hot-toast'
import {
  ShoppingBagIcon,
  CheckCircleIcon,
  ArrowLeftIcon,
} from '@heroicons/react/24/outline'
import { useCart } from '../../contexts/CartContext'
import { useAuth } from '../../contexts/AuthContext'
import { ordersApi } from '../../api/orders'
import { formatVnd, productImage } from '../../lib/format'

export function Checkout() {
  const { items, totalValue, clear } = useCart()
  const { user } = useAuth()
  const nav = useNavigate()
  const [submitting, setSubmitting] = useState(false)
  const [success, setSuccess] = useState<{ orderId: number; orderNumber: string; total: number } | null>(null)

  const [form, setForm] = useState({
    fullName: user?.fullName ?? '',
    email:    user?.email ?? '',
    phone:    '',
    address:  '',
    note:     '',
  })

  if (success) {
    return (
      <div className="max-w-2xl mx-auto px-4 py-16 text-center">
        <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-emerald-900/40 mb-4">
          <CheckCircleIcon className="w-10 h-10 text-emerald-400" />
        </div>
        <h1 className="text-3xl font-bold text-gray-50">Đặt hàng thành công!</h1>
        <p className="mt-3 text-gray-400">
          Order <span className="font-mono text-gray-200">{success.orderNumber}</span> đã được tạo trong OLTP.
          Trong vòng 5 phút, ETL sẽ sync sang OLAP — lúc đó dashboard sẽ thấy đơn này.
        </p>
        <div className="mt-6 inline-block bg-gray-900 border border-gray-800 rounded-lg px-6 py-4">
          <div className="text-sm text-gray-400">Tổng đơn</div>
          <div className="text-2xl font-bold text-blue-400 mt-1">{formatVnd(success.total)}</div>
        </div>
        <div className="mt-8 flex justify-center gap-3">
          {user && (
            <Link to="/my-orders" className="px-6 py-2.5 rounded-md bg-blue-500 hover:bg-blue-600 text-white font-medium">
              Xem My Orders
            </Link>
          )}
          <Link to="/shop" className="px-6 py-2.5 rounded-md bg-gray-800 hover:bg-gray-700 text-gray-100 font-medium border border-gray-700">
            Tiếp tục shopping
          </Link>
        </div>
      </div>
    )
  }

  if (items.length === 0) {
    return (
      <div className="max-w-2xl mx-auto px-4 py-16 text-center">
        <ShoppingBagIcon className="w-16 h-16 mx-auto text-gray-600 mb-4" />
        <h1 className="text-2xl font-bold text-gray-50">Cart trống</h1>
        <p className="mt-2 text-gray-400">Thêm sản phẩm vào cart trước khi checkout.</p>
        <Link
          to="/shop"
          className="inline-block mt-6 px-6 py-2.5 rounded-md bg-blue-500 hover:bg-blue-600 text-white font-medium"
        >
          Browse Shop
        </Link>
      </div>
    )
  }

  const submit = async () => {
    if (!user) {
      toast.error('Bạn cần đăng nhập trước khi checkout')
      nav('/login')
      return
    }
    if (!form.fullName.trim() || !form.address.trim()) {
      toast.error('Vui lòng điền họ tên + địa chỉ')
      return
    }

    setSubmitting(true)
    try {
      const res = await ordersApi.create({
        customerId: user.customerId,
        items: items.map(i => ({ productId: i.product.id, quantity: i.quantity })),
      })
      setSuccess({ orderId: res.orderId, orderNumber: res.orderNumber, total: res.totalAmount })
      clear()
      toast.success('Order placed!')
    } catch (e: unknown) {
      const err = e as { response?: { data?: { detail?: string; errors?: Record<string, string[]> } } }
      const validation = err?.response?.data?.errors
        ? Object.values(err.response.data.errors).flat().join('; ')
        : null
      toast.error(validation ?? err?.response?.data?.detail ?? 'Đặt hàng thất bại')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      <Link to="/shop" className="inline-flex items-center gap-1 text-sm text-blue-400 hover:text-blue-300 mb-4">
        <ArrowLeftIcon className="w-4 h-4" /> Tiếp tục shopping
      </Link>
      <h1 className="text-3xl font-bold text-gray-50">Checkout</h1>

      <div className="grid lg:grid-cols-3 gap-8 mt-8">
        {/* Form */}
        <div className="lg:col-span-2 space-y-6">
          {!user && (
            <div className="bg-amber-900/30 border border-amber-700 rounded-lg p-4 text-sm text-amber-200">
              Bạn chưa đăng nhập.{' '}
              <Link to="/login" className="underline font-medium">Login</Link>{' '}
              hoặc{' '}
              <Link to="/register" className="underline font-medium">đăng ký</Link>{' '}
              để hoàn tất đơn.
            </div>
          )}

          <div className="bg-gray-900 border border-gray-800 rounded-lg p-6">
            <h2 className="text-lg font-semibold text-gray-50 mb-4">Thông tin giao hàng</h2>
            <div className="grid sm:grid-cols-2 gap-4">
              <Field label="Họ và tên *" value={form.fullName} onChange={v => setForm({ ...form, fullName: v })} />
              <Field label="Email *" type="email" value={form.email} onChange={v => setForm({ ...form, email: v })} />
              <Field label="Số điện thoại" value={form.phone} onChange={v => setForm({ ...form, phone: v })} />
              <Field label="Địa chỉ *" value={form.address} onChange={v => setForm({ ...form, address: v })} />
            </div>
            <div className="mt-4">
              <label className="block text-sm font-medium text-gray-300 mb-1">Ghi chú đơn hàng</label>
              <textarea
                value={form.note}
                onChange={e => setForm({ ...form, note: e.target.value })}
                rows={3}
                className="w-full px-3 py-2 rounded-md bg-gray-800 border border-gray-700 text-gray-100 placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
                placeholder="Optional..."
              />
            </div>
          </div>

          <div className="bg-gray-900 border border-gray-800 rounded-lg p-6">
            <h2 className="text-lg font-semibold text-gray-50 mb-4">Phương thức thanh toán</h2>
            <div className="space-y-2 text-sm text-gray-300">
              <label className="flex items-center gap-3 p-3 rounded-md bg-gray-800 border border-gray-700 cursor-pointer">
                <input type="radio" name="payment" defaultChecked className="accent-blue-500" />
                COD — Thanh toán khi nhận hàng <span className="text-xs text-gray-500 ml-auto">(mock)</span>
              </label>
              <label className="flex items-center gap-3 p-3 rounded-md bg-gray-800/30 border border-gray-800 cursor-not-allowed opacity-50">
                <input type="radio" name="payment" disabled className="accent-blue-500" />
                Thẻ tín dụng — Coming soon
              </label>
            </div>
          </div>
        </div>

        {/* Summary */}
        <aside className="lg:col-span-1">
          <div className="bg-gray-900 border border-gray-800 rounded-lg p-6 sticky top-24">
            <h2 className="text-lg font-semibold text-gray-50 mb-4">Đơn hàng ({items.length})</h2>
            <div className="space-y-3 max-h-72 overflow-y-auto">
              {items.map(i => (
                <div key={i.product.id} className="flex gap-3 text-sm">
                  <img
                    src={productImage(i.product.id, 80, 80)}
                    alt={i.product.name}
                    className="w-12 h-12 rounded object-cover flex-shrink-0"
                  />
                  <div className="flex-1 min-w-0">
                    <div className="text-gray-100 truncate">{i.product.name}</div>
                    <div className="text-gray-500 text-xs">× {i.quantity}</div>
                  </div>
                  <div className="text-gray-300 whitespace-nowrap">{formatVnd(i.product.price * i.quantity)}</div>
                </div>
              ))}
            </div>

            <div className="mt-4 pt-4 border-t border-gray-800 space-y-2 text-sm">
              <div className="flex justify-between text-gray-400">
                <span>Subtotal</span>
                <span>{formatVnd(totalValue)}</span>
              </div>
              <div className="flex justify-between text-gray-400">
                <span>Phí vận chuyển</span>
                <span>Free</span>
              </div>
              <div className="flex justify-between text-base font-semibold text-gray-50 pt-2 border-t border-gray-800">
                <span>Total</span>
                <span className="text-blue-400">{formatVnd(totalValue)}</span>
              </div>
            </div>

            <button
              onClick={submit}
              disabled={submitting}
              className="mt-6 w-full px-6 py-3 rounded-md bg-blue-500 hover:bg-blue-600 disabled:opacity-50 text-white font-medium transition"
            >
              {submitting ? 'Đang xử lý...' : 'Đặt hàng'}
            </button>

            <p className="mt-3 text-xs text-gray-500 text-center">
              Đơn hàng sẽ ghi vào OLTP DB ngay. Đợi 5 phút để thấy trong Admin Dashboard.
            </p>
          </div>
        </aside>
      </div>
    </div>
  )
}

function Field({
  label, value, onChange, type = 'text',
}: {
  label: string
  value: string
  onChange: (v: string) => void
  type?: string
}) {
  return (
    <div>
      <label className="block text-sm font-medium text-gray-300 mb-1">{label}</label>
      <input
        type={type}
        value={value}
        onChange={e => onChange(e.target.value)}
        className="w-full px-3 py-2 rounded-md bg-gray-800 border border-gray-700 text-gray-100 placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
      />
    </div>
  )
}
