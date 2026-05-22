import { useState } from 'react'
import { Link, useNavigate, useLocation } from 'react-router-dom'
import toast from 'react-hot-toast'
import { LockClosedIcon } from '@heroicons/react/24/outline'
import { useAuth } from '../../contexts/AuthContext'

export function Login() {
  const { login } = useAuth()
  const nav = useNavigate()
  const loc = useLocation() as { state?: { from?: string } }
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!email.trim() || !password) {
      setError('Vui lòng nhập đủ email + password')
      return
    }
    setSubmitting(true)
    setError(null)
    try {
      await login(email.trim(), password)
      toast.success('Đăng nhập thành công')
      nav(loc.state?.from ?? '/')
    } catch (e: unknown) {
      const err = e as { response?: { status?: number; data?: { detail?: string } } }
      setError(err?.response?.status === 401
        ? 'Email hoặc mật khẩu không đúng'
        : err?.response?.data?.detail ?? 'Đăng nhập thất bại')
    } finally {
      setSubmitting(false)
    }
  }

  const fillDemoAdmin = () => {
    setEmail('admin@ecom.com'); setPassword('admin123')
  }
  const fillDemoCustomer = () => {
    setEmail('demo@ecom.com'); setPassword('demo123')
  }

  return (
    <div className="max-w-md mx-auto px-4 py-16">
      <div className="bg-gray-900 border border-gray-800 rounded-lg p-8">
        <div className="text-center mb-6">
          <div className="inline-flex items-center justify-center w-12 h-12 rounded-full bg-blue-900/40 mb-3">
            <LockClosedIcon className="w-6 h-6 text-blue-400" />
          </div>
          <h1 className="text-2xl font-bold text-gray-50">Đăng nhập</h1>
          <p className="mt-2 text-sm text-gray-400">
            Có 2 tài khoản demo được seed sẵn — click để fill nhanh.
          </p>
        </div>

        <form onSubmit={submit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-1">Email</label>
            <input
              type="email"
              value={email}
              onChange={e => setEmail(e.target.value)}
              autoComplete="username"
              className="w-full px-3 py-2 rounded-md bg-gray-800 border border-gray-700 text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-1">Password</label>
            <input
              type="password"
              value={password}
              onChange={e => setPassword(e.target.value)}
              autoComplete="current-password"
              className="w-full px-3 py-2 rounded-md bg-gray-800 border border-gray-700 text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
          </div>

          {error && (
            <div className="bg-rose-900/30 border border-rose-700 rounded-md p-3 text-sm text-rose-200">
              {error}
            </div>
          )}

          <button
            type="submit"
            disabled={submitting}
            className="w-full px-6 py-2.5 rounded-md bg-blue-500 hover:bg-blue-600 disabled:opacity-50 text-white font-medium transition"
          >
            {submitting ? 'Đang đăng nhập...' : 'Đăng nhập'}
          </button>
        </form>

        {/* Demo account quick-fill */}
        <div className="mt-6 pt-6 border-t border-gray-800">
          <div className="text-xs text-gray-500 mb-2 text-center">⚡ Quick fill for demo:</div>
          <div className="grid grid-cols-2 gap-2">
            <button
              onClick={fillDemoAdmin}
              type="button"
              className="px-3 py-2 text-xs rounded-md bg-gray-800 hover:bg-gray-700 text-gray-200 border border-gray-700"
            >
              👑 Admin<br />
              <span className="font-mono text-gray-500">admin@ecom.com</span>
            </button>
            <button
              onClick={fillDemoCustomer}
              type="button"
              className="px-3 py-2 text-xs rounded-md bg-gray-800 hover:bg-gray-700 text-gray-200 border border-gray-700"
            >
              🛒 Customer<br />
              <span className="font-mono text-gray-500">demo@ecom.com</span>
            </button>
          </div>
        </div>

        <div className="mt-6 text-center text-sm text-gray-400">
          Chưa có tài khoản?{' '}
          <Link to="/register" className="text-blue-400 hover:text-blue-300 font-medium">
            Đăng ký mới
          </Link>
        </div>
      </div>
    </div>
  )
}
