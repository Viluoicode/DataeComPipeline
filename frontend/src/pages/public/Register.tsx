import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import toast from 'react-hot-toast'
import { UserPlusIcon } from '@heroicons/react/24/outline'
import { useAuth } from '../../contexts/AuthContext'

export function Register() {
  const { register } = useAuth()
  const nav = useNavigate()
  const [form, setForm] = useState({
    fullName: '', email: '', password: '', confirm: '',
    phone: '', city: '',
  })
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)

    if (!form.fullName.trim() || !form.email.trim() || !form.password) {
      setError('Họ tên, email, password đều bắt buộc')
      return
    }
    if (form.password.length < 6) {
      setError('Password tối thiểu 6 ký tự')
      return
    }
    if (form.password !== form.confirm) {
      setError('Password xác nhận không khớp')
      return
    }

    setSubmitting(true)
    try {
      await register({
        fullName: form.fullName.trim(),
        email:    form.email.trim(),
        password: form.password,
        phone:    form.phone.trim() || undefined,
        city:     form.city.trim() || undefined,
      })
      toast.success(`Welcome, ${form.fullName.split(' ').slice(-1)[0]}!`)
      nav('/shop')
    } catch (e: unknown) {
      const err = e as { response?: { data?: { detail?: string; errors?: Record<string, string[]> } } }
      const validation = err?.response?.data?.errors
        ? Object.values(err.response.data.errors).flat().join('; ')
        : null
      setError(validation ?? err?.response?.data?.detail ?? 'Đăng ký thất bại')
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
          <h1 className="text-2xl font-bold text-gray-50">Tạo tài khoản</h1>
          <p className="mt-2 text-sm text-gray-400">
            Sau khi đăng ký bạn sẽ tự động đăng nhập.
          </p>
        </div>

        <form onSubmit={submit} className="space-y-4">
          <Field label="Họ và tên *" value={form.fullName} onChange={v => setForm({ ...form, fullName: v })} />
          <Field label="Email *" type="email" value={form.email} onChange={v => setForm({ ...form, email: v })} autoComplete="username" />
          <Field label="Password * (≥ 6 ký tự)" type="password" value={form.password} onChange={v => setForm({ ...form, password: v })} autoComplete="new-password" />
          <Field label="Xác nhận password *" type="password" value={form.confirm} onChange={v => setForm({ ...form, confirm: v })} autoComplete="new-password" />
          <Field label="Số điện thoại" value={form.phone} onChange={v => setForm({ ...form, phone: v })} />
          <Field label="Thành phố" value={form.city} onChange={v => setForm({ ...form, city: v })} />

          {error && (
            <div className="bg-rose-900/30 border border-rose-700 rounded-md p-3 text-sm text-rose-200">
              {error}
            </div>
          )}

          <button
            type="submit"
            disabled={submitting}
            className="w-full px-6 py-2.5 rounded-md bg-blue-500 hover:bg-blue-600 disabled:opacity-50 text-white font-medium"
          >
            {submitting ? 'Đang tạo...' : 'Đăng ký'}
          </button>
        </form>

        <div className="mt-6 text-center text-sm text-gray-400">
          Đã có tài khoản?{' '}
          <Link to="/login" className="text-blue-400 hover:text-blue-300 font-medium">
            Đăng nhập
          </Link>
        </div>
      </div>
    </div>
  )
}

function Field({
  label, value, onChange, type = 'text', autoComplete,
}: {
  label: string
  value: string
  onChange: (v: string) => void
  type?: string
  autoComplete?: string
}) {
  return (
    <div>
      <label className="block text-sm font-medium text-gray-300 mb-1">{label}</label>
      <input
        type={type}
        value={value}
        onChange={e => onChange(e.target.value)}
        autoComplete={autoComplete}
        className="w-full px-3 py-2 rounded-md bg-gray-800 border border-gray-700 text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500"
      />
    </div>
  )
}
