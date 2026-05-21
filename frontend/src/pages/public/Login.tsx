import { useState, useEffect } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import toast from 'react-hot-toast'
import { LockClosedIcon } from '@heroicons/react/24/outline'
import { customersApi } from '../../api/lookups'
import { useAuth } from '../../contexts/AuthContext'
import type { CustomerLookup } from '../../types/api'

export function Login() {
  const { login } = useAuth()
  const nav = useNavigate()
  const [search, setSearch] = useState('')
  const [results, setResults] = useState<CustomerLookup[]>([])
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    const t = setTimeout(() => {
      setLoading(true)
      customersApi.search(search || undefined, 1, 10)
        .then(r => setResults(r.items))
        .finally(() => setLoading(false))
    }, 250)
    return () => clearTimeout(t)
  }, [search])

  const pick = (c: CustomerLookup) => {
    login({ customerId: c.id, fullName: c.fullName, email: c.email })
    toast.success(`Welcome back, ${c.fullName.split(' ').slice(-1)[0]}!`)
    nav('/shop')
  }

  return (
    <div className="max-w-md mx-auto px-4 py-16">
      <div className="bg-gray-900 border border-gray-800 rounded-lg p-8">
        <div className="text-center mb-6">
          <div className="inline-flex items-center justify-center w-12 h-12 rounded-full bg-blue-900/40 mb-3">
            <LockClosedIcon className="w-6 h-6 text-blue-400" />
          </div>
          <h1 className="text-2xl font-bold text-gray-50">Đăng nhập (mock)</h1>
          <p className="mt-2 text-sm text-gray-400">
            Project demo — chọn 1 khách hàng từ database để "login as".
            Không có password vì không phải auth thật.
          </p>
        </div>

        <label className="block text-sm font-medium text-gray-300 mb-2">
          Search customer hiện có (5,000 khách trong seed)
        </label>
        <input
          type="text"
          placeholder="Gõ tên, email, hoặc thành phố..."
          value={search}
          onChange={e => setSearch(e.target.value)}
          className="w-full px-3 py-2 rounded-md bg-gray-800 border border-gray-700 text-gray-100 placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
        />

        <div className="mt-3 max-h-72 overflow-y-auto border border-gray-800 rounded-md divide-y divide-gray-800">
          {loading && results.length === 0 && (
            <div className="px-3 py-6 text-center text-gray-500 text-sm">Đang tải...</div>
          )}
          {!loading && results.length === 0 && (
            <div className="px-3 py-6 text-center text-gray-500 text-sm">Không tìm thấy</div>
          )}
          {results.map(c => (
            <button
              key={c.id}
              onClick={() => pick(c)}
              className="w-full text-left px-3 py-3 hover:bg-gray-800 transition"
            >
              <div className="text-sm font-medium text-gray-100">{c.fullName}</div>
              <div className="text-xs text-gray-400">{c.email} · {c.city ?? '—'}</div>
            </button>
          ))}
        </div>

        <div className="mt-6 text-center text-sm text-gray-400">
          Chưa có account?{' '}
          <Link to="/register" className="text-blue-400 hover:text-blue-300 font-medium">
            Đăng ký mới
          </Link>
        </div>

        <div className="mt-6 pt-6 border-t border-gray-800 text-xs text-gray-500 text-center">
          ⚠️ Đây là mock auth — không có password, không có JWT. <br />
          Production sẽ cần proper authentication (xem README §12).
        </div>
      </div>
    </div>
  )
}
