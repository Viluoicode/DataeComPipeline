import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import toast from 'react-hot-toast'
import { MapPinIcon, TrashIcon, PencilSquareIcon } from '@heroicons/react/24/outline'
import { addressesApi, type Address, type SaveAddressRequest } from '../../api/addresses'
import { useAuth } from '../../contexts/AuthContext'

const empty: SaveAddressRequest = { fullName: '', phone: '', address: '', isDefault: false }

export function Addresses() {
  const { user } = useAuth()
  const [items, setItems] = useState<Address[]>([])
  const [editing, setEditing] = useState<number | null>(null)   // null = closed, 0 = new
  const [form, setForm] = useState<SaveAddressRequest>(empty)
  const [saving, setSaving] = useState(false)

  async function load() { setItems(await addressesApi.list()) }
  useEffect(() => { if (user) load() }, [user])

  if (!user) {
    return (
      <div className="max-w-2xl mx-auto px-4 py-16 text-center">
        <p className="text-gray-300">Cần đăng nhập để quản lý địa chỉ.</p>
        <Link to="/login" className="inline-block mt-4 text-blue-400 hover:underline">Đăng nhập</Link>
      </div>
    )
  }

  function startNew() { setEditing(0); setForm(empty) }
  function startEdit(a: Address) { setEditing(a.id); setForm({ fullName: a.fullName, phone: a.phone, address: a.address, isDefault: a.isDefault }) }

  async function save() {
    if (!form.fullName.trim() || !form.phone.trim() || !form.address.trim()) {
      toast.error('Điền đủ họ tên, SĐT, địa chỉ'); return
    }
    setSaving(true)
    try {
      if (editing === 0) await addressesApi.create(form)
      else if (editing != null) await addressesApi.update(editing, form)
      toast.success('Đã lưu')
      setEditing(null)
      await load()
    } catch { toast.error('Lưu thất bại') } finally { setSaving(false) }
  }

  async function remove(a: Address) {
    if (!confirm('Xoá địa chỉ này?')) return
    try { await addressesApi.remove(a.id); await load() } catch { toast.error('Không xoá được') }
  }

  return (
    <div className="max-w-3xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-3xl font-bold text-gray-50">Sổ địa chỉ</h1>
          <p className="text-gray-400 mt-1">Địa chỉ giao hàng đã lưu</p>
        </div>
        <button onClick={startNew} className="px-4 py-2 rounded-md bg-blue-500 hover:bg-blue-600 text-white font-medium">
          + Thêm địa chỉ
        </button>
      </div>

      {editing !== null && (
        <div className="bg-gray-900 border border-gray-800 rounded-lg p-6 mb-6 space-y-3">
          <h2 className="font-semibold text-gray-50">{editing === 0 ? 'Thêm địa chỉ' : 'Sửa địa chỉ'}</h2>
          <Input label="Họ và tên" value={form.fullName} onChange={v => setForm({ ...form, fullName: v })} />
          <Input label="Số điện thoại" value={form.phone} onChange={v => setForm({ ...form, phone: v })} />
          <Input label="Địa chỉ" value={form.address} onChange={v => setForm({ ...form, address: v })} />
          <label className="flex items-center gap-2 text-sm text-gray-300">
            <input type="checkbox" className="accent-blue-500" checked={form.isDefault ?? false}
              onChange={e => setForm({ ...form, isDefault: e.target.checked })} />
            Đặt làm mặc định
          </label>
          <div className="flex justify-end gap-2 pt-2">
            <button onClick={() => setEditing(null)} className="px-4 py-2 rounded-md bg-gray-800 text-gray-200 border border-gray-700">Huỷ</button>
            <button disabled={saving} onClick={save} className="px-4 py-2 rounded-md bg-blue-500 hover:bg-blue-600 text-white disabled:opacity-50">Lưu</button>
          </div>
        </div>
      )}

      {items.length === 0 ? (
        <div className="text-center py-16 bg-gray-900 border border-gray-800 rounded-lg">
          <MapPinIcon className="w-12 h-12 mx-auto text-gray-600 mb-3" />
          <p className="text-gray-400">Chưa có địa chỉ nào.</p>
        </div>
      ) : (
        <div className="space-y-3">
          {items.map(a => (
            <div key={a.id} className="flex items-start justify-between gap-4 bg-gray-900 border border-gray-800 rounded-lg p-4">
              <div>
                <div className="flex items-center gap-2">
                  <span className="font-medium text-gray-100">{a.fullName}</span>
                  {a.isDefault && <span className="text-xs px-2 py-0.5 rounded bg-blue-900/40 text-blue-300">Mặc định</span>}
                </div>
                <div className="text-sm text-gray-400">{a.phone}</div>
                <div className="text-sm text-gray-400">{a.address}</div>
              </div>
              <div className="flex gap-2 flex-shrink-0">
                <button onClick={() => startEdit(a)} className="p-2 rounded text-gray-400 hover:bg-gray-800 hover:text-gray-100"><PencilSquareIcon className="w-5 h-5" /></button>
                <button onClick={() => remove(a)} className="p-2 rounded text-rose-400 hover:bg-rose-900/30"><TrashIcon className="w-5 h-5" /></button>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

function Input({ label, value, onChange }: { label: string; value: string; onChange: (v: string) => void }) {
  return (
    <div>
      <label className="block text-sm font-medium text-gray-300 mb-1">{label}</label>
      <input value={value} onChange={e => onChange(e.target.value)}
        className="w-full px-3 py-2 rounded-md bg-gray-800 border border-gray-700 text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500" />
    </div>
  )
}
