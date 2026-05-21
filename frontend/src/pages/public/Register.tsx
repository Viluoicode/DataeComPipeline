import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import toast from 'react-hot-toast'
import { UserPlusIcon } from '@heroicons/react/24/outline'
import { useAuth } from '../../contexts/AuthContext'
import { customersApi } from '../../api/lookups'

/**
 * Mock register — for demo we just use the import endpoint via a tiny
 * one-row Excel-equivalent payload. Simpler path: ask backend to create
 * the customer via... well, we don't have a public POST /api/customers
 * endpoint. So we re-use search to verify uniqueness, then call the
 * existing import endpoint with an in-memory .xlsx blob.
 *
 * For the demo, we keep it really simple: do nothing on the backend
 * (the user is "logged in" using the form data), but show how a real
 * register flow would work in v2.
 */
export function Register() {
  const { login } = useAuth()
  const nav = useNavigate()
  const [form, setForm] = useState({ fullName: '', email: '', phone: '', city: '' })
  const [submitting, setSubmitting] = useState(false)

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!form.fullName.trim() || !form.email.trim()) {
      toast.error('Vui lòng điền Họ tên + Email')
      return
    }

    setSubmitting(true)
    try {
      // Check if customer already exists with this email
      const res = await customersApi.search(form.email)
      const existing = res.items.find(c => c.email.toLowerCase() === form.email.toLowerCase())

      if (existing) {
        login({ customerId: existing.id, fullName: existing.fullName, email: existing.email })
        toast.success(`Account đã tồn tại — đã login as ${existing.fullName}`)
        nav('/shop')
        return
      }

      // Real registration would POST to /api/customers — backend doesn't expose
      // that yet for the public storefront (only via import). For demo, just
      // simulate by logging in with a fake customerId.
      // The order won't FK-link to a real customer until backend exposes register.
      toast.error('Tài khoản chưa có trong DB. Hãy thử "Login" với 1 khách hàng có sẵn (Login page → search).')
    } catch (e: unknown) {
      const err = e as { message?: string }
      toast.error(err?.message ?? 'Đăng ký thất bại')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="max-w-md mx-auto px-4 py-16">
      <div className="bg-gray-900 border border-gray-800 rounded-lg p-8">
        <div className="text-center mb-6">
          <div className="inline-flex items-center justify-center w-12 h-12 rounded-full bg-blue-900/40 mb-3">
            <UserPlusIcon className="w-6 h-6 text-blue-400" />
          </div>
          <h1 className="text-2xl font-bold text-gray-50">Đăng ký</h1>
          <p className="mt-2 text-sm text-gray-400">
            Mock — sẽ kiểm tra email trong DB, nếu có thì login luôn.
          </p>
        </div>

        <form onSubmit={submit} className="space-y-4">
          <Field label="Họ và tên *" value={form.fullName} onChange={v => setForm({ ...form, fullName: v })} />
          <Field label="Email *" type="email" value={form.email} onChange={v => setForm({ ...form, email: v })} />
          <Field label="Số điện thoại" value={form.phone} onChange={v => setForm({ ...form, phone: v })} />
          <Field label="Thành phố" value={form.city} onChange={v => setForm({ ...form, city: v })} />

          <button
            type="submit"
            disabled={submitting}
            className="w-full px-6 py-2.5 rounded-md bg-blue-500 hover:bg-blue-600 disabled:opacity-50 text-white font-medium"
          >
            {submitting ? 'Checking...' : 'Đăng ký / Login'}
          </button>
        </form>

        <div className="mt-6 text-center text-sm text-gray-400">
          Đã có tài khoản?{' '}
          <Link to="/login" className="text-blue-400 hover:text-blue-300 font-medium">
            Đăng nhập
          </Link>
        </div>

        <div className="mt-6 pt-6 border-t border-gray-800 text-xs text-gray-500 text-center">
          💡 Quickstart: Thử login với email <span className="font-mono text-gray-400">@test.com</span> nào đó từ seed data.
        </div>
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
        className="w-full px-3 py-2 rounded-md bg-gray-800 border border-gray-700 text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500"
      />
    </div>
  )
}
