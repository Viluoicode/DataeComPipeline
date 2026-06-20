import { useEffect, useState, useCallback, type ReactNode } from 'react'
import {
  Card, Title, Text, Flex, Button, TextInput, NumberInput,
  Table, TableHead, TableHeaderCell, TableBody, TableRow, TableCell, Badge,
} from '@tremor/react'
import toast from 'react-hot-toast'
import { productsApi, type CreateProductRequest } from '../api/lookups'
import type { ProductLookup } from '../types/api'
import { productImage } from '../lib/format'

const empty: CreateProductRequest = { sku: '', name: '', category: '', brand: '', price: 0, stockQuantity: 0 }

function formatVnd(n: number): string {
  return n.toLocaleString('vi-VN', { maximumFractionDigits: 0 }) + ' ₫'
}

export function ProductsAdmin() {
  const [items, setItems] = useState<ProductLookup[]>([])
  const [search, setSearch] = useState('')
  const [loading, setLoading] = useState(false)
  const [editing, setEditing] = useState<number | null>(null)   // null = not editing, 0 = creating
  const [form, setForm] = useState<CreateProductRequest>(empty)
  const [saving, setSaving] = useState(false)

  const load = useCallback(async (s: string) => {
    setLoading(true)
    try { setItems((await productsApi.search(s || undefined, undefined, 1, 50)).items) }
    finally { setLoading(false) }
  }, [])

  useEffect(() => { load(search) }, [load, search])

  function startCreate() { setEditing(0); setForm(empty) }
  function startEdit(p: ProductLookup) {
    setEditing(p.id)
    setForm({ sku: p.sku, name: p.name, category: p.category, brand: p.brand ?? '', price: p.price, stockQuantity: p.stockQuantity })
  }
  function cancel() { setEditing(null); setForm(empty) }

  async function save() {
    setSaving(true)
    try {
      if (editing === 0) {
        await productsApi.create(form)
        toast.success('Đã tạo sản phẩm')
      } else if (editing != null) {
        await productsApi.update(editing, {
          name: form.name, category: form.category, brand: form.brand,
          price: form.price, stockQuantity: form.stockQuantity,
        })
        toast.success('Đã cập nhật')
      }
      cancel()
      await load(search)
    } catch (e: unknown) {
      const err = e as { response?: { data?: { detail?: string; errors?: Record<string, string[]> } } }
      const v = err?.response?.data?.errors ? Object.values(err.response.data.errors).flat().join('; ') : null
      toast.error(v ?? err?.response?.data?.detail ?? 'Lưu thất bại')
    } finally { setSaving(false) }
  }

  async function remove(p: ProductLookup) {
    if (!confirm(`Xoá sản phẩm "${p.name}"?`)) return
    try {
      await productsApi.remove(p.id)
      toast.success('Đã xoá')
      await load(search)
    } catch (e: unknown) {
      const err = e as { response?: { data?: { detail?: string } } }
      toast.error(err?.response?.data?.detail ?? 'Không xoá được')
    }
  }

  async function uploadImg(id: number, file?: File) {
    if (!file) return
    try {
      await productsApi.uploadImage(id, file)
      toast.success('Đã tải ảnh')
      await load(search)
    } catch {
      toast.error('Tải ảnh thất bại (PNG/JPG/WEBP/GIF, ≤ 5MB)')
    }
  }

  return (
    <div className="p-6 space-y-6">
      <Flex justifyContent="between" alignItems="center">
        <div>
          <Title className="!text-2xl">Sản phẩm</Title>
          <Text>Quản lý catalog (tạo / sửa / xoá)</Text>
        </div>
        <Button onClick={startCreate}>+ Sản phẩm mới</Button>
      </Flex>

      {editing !== null && (
        <Card>
          <Title>{editing === 0 ? 'Tạo sản phẩm' : 'Sửa sản phẩm'}</Title>
          <div className="grid sm:grid-cols-2 gap-4 mt-4">
            <Field label="SKU">
              <TextInput value={form.sku} disabled={editing !== 0}
                onChange={e => setForm({ ...form, sku: e.target.value })} placeholder="SKU-000123" />
            </Field>
            <Field label="Tên">
              <TextInput value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} />
            </Field>
            <Field label="Danh mục">
              <TextInput value={form.category} onChange={e => setForm({ ...form, category: e.target.value })} />
            </Field>
            <Field label="Thương hiệu">
              <TextInput value={form.brand ?? ''} onChange={e => setForm({ ...form, brand: e.target.value })} />
            </Field>
            <Field label="Giá (₫)">
              <NumberInput value={form.price} min={0} onValueChange={v => setForm({ ...form, price: v ?? 0 })} />
            </Field>
            <Field label="Tồn kho">
              <NumberInput value={form.stockQuantity} min={0} onValueChange={v => setForm({ ...form, stockQuantity: v ?? 0 })} />
            </Field>
          </div>
          <Flex justifyContent="end" className="gap-2 mt-4">
            <Button variant="secondary" onClick={cancel}>Huỷ</Button>
            <Button loading={saving} onClick={save}>Lưu</Button>
          </Flex>
        </Card>
      )}

      <Card>
        <TextInput className="max-w-sm" placeholder="Tìm theo tên / SKU..."
          value={search} onValueChange={setSearch} />
        <Table className="mt-4">
          <TableHead>
            <TableRow>
              <TableHeaderCell>SKU</TableHeaderCell>
              <TableHeaderCell>Tên</TableHeaderCell>
              <TableHeaderCell>Danh mục</TableHeaderCell>
              <TableHeaderCell className="text-right">Giá</TableHeaderCell>
              <TableHeaderCell className="text-right">Tồn</TableHeaderCell>
              <TableHeaderCell className="text-right">Thao tác</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {items.map(p => (
              <TableRow key={p.id}>
                <TableCell className="font-mono text-xs">{p.sku}</TableCell>
                <TableCell>
                  <div className="flex items-center gap-2">
                    <img src={productImage(p.id, 40, 40, p.imageUrl)} alt=""
                      className="w-8 h-8 rounded object-cover flex-shrink-0" />
                    <span>{p.name}</span>
                  </div>
                </TableCell>
                <TableCell>{p.category}</TableCell>
                <TableCell className="text-right">{formatVnd(p.price)}</TableCell>
                <TableCell className="text-right">
                  <Badge color={p.stockQuantity < 20 ? 'rose' : 'gray'}>{p.stockQuantity}</Badge>
                </TableCell>
                <TableCell className="text-right space-x-2 whitespace-nowrap">
                  <label className="inline-flex items-center px-2 py-1 text-xs rounded border border-gray-700 text-gray-300 hover:bg-gray-800 cursor-pointer">
                    Ảnh
                    <input type="file" accept="image/*" className="hidden"
                      onChange={e => uploadImg(p.id, e.target.files?.[0])} />
                  </label>
                  <Button size="xs" variant="secondary" onClick={() => startEdit(p)}>Sửa</Button>
                  <Button size="xs" variant="secondary" color="rose" onClick={() => remove(p)}>Xoá</Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        {loading && <Text className="mt-3">Đang tải...</Text>}
        {!loading && items.length === 0 && <Text className="mt-3">Không có sản phẩm.</Text>}
      </Card>
    </div>
  )
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <Text className="mb-1">{label}</Text>
      {children}
    </div>
  )
}
