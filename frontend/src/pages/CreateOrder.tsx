import { useEffect, useState } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import toast from 'react-hot-toast'
import {
  Card,
  Title,
  Text,
  Flex,
  Button,
  TextInput,
  Select,
  SelectItem,
  Table,
  TableHead,
  TableHeaderCell,
  TableBody,
  TableRow,
  TableCell,
  NumberInput,
  Badge,
} from '@tremor/react'
import { TrashIcon } from '@heroicons/react/24/outline'
import { customersApi, productsApi } from '../api/lookups'
import { ordersApi } from '../api/orders'
import type { CustomerLookup, ProductLookup } from '../types/api'

interface LineItem {
  product: ProductLookup
  quantity: number
}

function formatVnd(n: number): string {
  return n.toLocaleString('vi-VN', { maximumFractionDigits: 0 }) + ' ₫'
}

export function CreateOrder() {
  const nav = useNavigate()

  // Customer search
  const [customerSearch, setCustomerSearch] = useState('')
  const [customers, setCustomers] = useState<CustomerLookup[]>([])
  const [selectedCustomer, setSelectedCustomer] = useState<CustomerLookup | null>(null)

  // Product search
  const [productSearch, setProductSearch] = useState('')
  const [category, setCategory] = useState<string>('')
  const [categories, setCategories] = useState<string[]>([])
  const [products, setProducts] = useState<ProductLookup[]>([])

  // Order items
  const [items, setItems] = useState<LineItem[]>([])

  // UI state
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<{ orderId: number; orderNumber: string; total: number } | null>(null)

  // Load categories once
  useEffect(() => { productsApi.categories().then(setCategories) }, [])

  // Debounced customer search
  useEffect(() => {
    const t = setTimeout(() => {
      customersApi.search(customerSearch || undefined, 1, 10).then(r => setCustomers(r.items))
    }, 250)
    return () => clearTimeout(t)
  }, [customerSearch])

  // Debounced product search
  useEffect(() => {
    const t = setTimeout(() => {
      productsApi.search(productSearch || undefined, category || undefined, 1, 20).then(r => setProducts(r.items))
    }, 250)
    return () => clearTimeout(t)
  }, [productSearch, category])

  function addProduct(p: ProductLookup) {
    setItems(prev => {
      const existing = prev.find(x => x.product.id === p.id)
      if (existing) return prev.map(x => x.product.id === p.id ? { ...x, quantity: x.quantity + 1 } : x)
      return [...prev, { product: p, quantity: 1 }]
    })
  }

  function removeItem(productId: number) {
    setItems(prev => prev.filter(x => x.product.id !== productId))
  }

  function updateQty(productId: number, qty: number) {
    setItems(prev => prev.map(x => x.product.id === productId ? { ...x, quantity: Math.max(1, qty) } : x))
  }

  const total = items.reduce((s, i) => s + i.product.price * i.quantity, 0)

  async function submit() {
    if (!selectedCustomer) { setError('Please select a customer'); return }
    if (items.length === 0) { setError('Please add at least 1 product'); return }
    setSubmitting(true)
    setError(null)
    try {
      const res = await ordersApi.create({
        customerId: selectedCustomer.id,
        items: items.map(i => ({ productId: i.product.id, quantity: i.quantity })),
      })
      setSuccess({ orderId: res.orderId, orderNumber: res.orderNumber, total: res.totalAmount })
      setItems([])
      setSelectedCustomer(null)
      toast.success(`Đơn ${res.orderNumber} đã tạo`)
    } catch (e: unknown) {
      const err = e as { response?: { data?: { detail?: string; errors?: Record<string, string[]> } } }
      const detail = err?.response?.data?.detail
      const validation = err?.response?.data?.errors
        ? Object.values(err.response.data.errors).flat().join('; ')
        : null
      setError(validation ?? detail ?? 'Failed to create order')
    } finally {
      setSubmitting(false)
    }
  }

  if (success) {
    return (
      <div className="p-6 space-y-6">
        <Card decoration="top" decorationColor="emerald">
          <Title>✅ Order Created</Title>
          <Text className="mt-2">
            Order <span className="font-mono">{success.orderNumber}</span> (#{success.orderId}) with total {formatVnd(success.total)}.
          </Text>
          <Text className="mt-2 text-xs">
            Data is now in OLTP. Wait up to 5 minutes for the recurring ETL to sync to OLAP, or trigger ETL manually from <Link to="/stress" className="text-blue-500 hover:underline">Stress Test</Link>.
          </Text>
          <Flex justifyContent="start" className="gap-3 mt-4">
            <Link to={`/orders/${success.orderId}`}><Button>View Order Detail</Button></Link>
            <Link to="/admin/orders"><Button variant="secondary">Back to Orders</Button></Link>
            <Button variant="light" onClick={() => setSuccess(null)}>Create Another</Button>
          </Flex>
        </Card>
      </div>
    )
  }

  return (
    <div className="p-6 space-y-6">
      <div>
        <Link to="/admin/orders" className="text-sm text-blue-500 hover:underline">← Back to orders</Link>
        <Title className="!text-2xl mt-1">New Order</Title>
        <Text>Fill the form and submit. Backend will validate via FluentValidation, write to OLTP, then ETL pushes to OLAP.</Text>
      </div>

      {error && (
        <Card decoration="left" decorationColor="rose">
          <Text color="rose">⚠️ {error}</Text>
        </Card>
      )}

      {/* Customer picker */}
      <Card>
        <Title>1. Choose Customer</Title>
        {selectedCustomer ? (
          <Flex justifyContent="between" className="mt-3">
            <div>
              <Text className="font-medium">{selectedCustomer.fullName}</Text>
              <Text className="text-xs">{selectedCustomer.email} · {selectedCustomer.city ?? '—'}</Text>
            </div>
            <Button variant="light" onClick={() => setSelectedCustomer(null)}>Change</Button>
          </Flex>
        ) : (
          <div className="mt-3">
            <TextInput
              placeholder="Search by name, email, or city..."
              value={customerSearch}
              onValueChange={setCustomerSearch}
            />
            <div className="mt-2 max-h-60 overflow-auto border rounded-md divide-y divide-gray-200 dark:divide-gray-800">
              {customers.map(c => (
                <div
                  key={c.id}
                  className="px-3 py-2 cursor-pointer hover:bg-gray-100 dark:hover:bg-gray-800"
                  onClick={() => { setSelectedCustomer(c); setCustomerSearch('') }}
                >
                  <Text className="font-medium">{c.fullName}</Text>
                  <Text className="text-xs">{c.email} · {c.city ?? '—'}</Text>
                </div>
              ))}
              {customers.length === 0 && <div className="px-3 py-4 text-center text-gray-500 text-sm">No customers</div>}
            </div>
          </div>
        )}
      </Card>

      {/* Product picker */}
      <Card>
        <Title>2. Add Products</Title>
        <Flex justifyContent="start" className="gap-3 mt-3 flex-wrap">
          <div className="flex-1 min-w-[200px]">
            <Text>Search products</Text>
            <TextInput
              placeholder="SKU or product name..."
              value={productSearch}
              onValueChange={setProductSearch}
            />
          </div>
          <div className="w-48">
            <Text>Category</Text>
            <Select value={category} onValueChange={setCategory}>
              <SelectItem value="">All</SelectItem>
              {categories.map(c => <SelectItem key={c} value={c}>{c}</SelectItem>)}
            </Select>
          </div>
        </Flex>

        <div className="mt-3 max-h-72 overflow-auto border rounded-md">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>SKU</TableHeaderCell>
                <TableHeaderCell>Name</TableHeaderCell>
                <TableHeaderCell>Category</TableHeaderCell>
                <TableHeaderCell className="text-right">Price</TableHeaderCell>
                <TableHeaderCell className="text-right">Stock</TableHeaderCell>
                <TableHeaderCell></TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {products.map(p => (
                <TableRow key={p.id}>
                  <TableCell className="font-mono text-xs">{p.sku}</TableCell>
                  <TableCell>{p.name}</TableCell>
                  <TableCell><Badge>{p.category}</Badge></TableCell>
                  <TableCell className="text-right">{formatVnd(p.price)}</TableCell>
                  <TableCell className="text-right">{p.stockQuantity}</TableCell>
                  <TableCell>
                    <Button size="xs" variant="light" onClick={() => addProduct(p)}>+ Add</Button>
                  </TableCell>
                </TableRow>
              ))}
              {products.length === 0 && (
                <TableRow>
                  <TableCell colSpan={6} className="text-center py-4 text-gray-500">No products</TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </div>
      </Card>

      {/* Cart */}
      <Card>
        <Flex justifyContent="between">
          <Title>3. Review Items ({items.length})</Title>
          <Text className="text-lg font-medium">Total: {formatVnd(total)}</Text>
        </Flex>
        <Table className="mt-4">
          <TableHead>
            <TableRow>
              <TableHeaderCell>SKU</TableHeaderCell>
              <TableHeaderCell>Product</TableHeaderCell>
              <TableHeaderCell className="text-right">Unit Price</TableHeaderCell>
              <TableHeaderCell className="text-right w-32">Qty</TableHeaderCell>
              <TableHeaderCell className="text-right">Line Total</TableHeaderCell>
              <TableHeaderCell></TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {items.map(i => (
              <TableRow key={i.product.id}>
                <TableCell className="font-mono text-xs">{i.product.sku}</TableCell>
                <TableCell>{i.product.name}</TableCell>
                <TableCell className="text-right">{formatVnd(i.product.price)}</TableCell>
                <TableCell className="text-right">
                  <NumberInput
                    min={1}
                    value={i.quantity}
                    onValueChange={v => updateQty(i.product.id, v)}
                    className="w-24 ml-auto"
                  />
                </TableCell>
                <TableCell className="text-right font-medium">
                  {formatVnd(i.product.price * i.quantity)}
                </TableCell>
                <TableCell>
                  <Button
                    size="xs"
                    variant="light"
                    color="rose"
                    icon={TrashIcon}
                    onClick={() => removeItem(i.product.id)}
                  />
                </TableCell>
              </TableRow>
            ))}
            {items.length === 0 && (
              <TableRow>
                <TableCell colSpan={6} className="text-center py-6 text-gray-500">
                  No items yet — add products above
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </Card>

      <Flex justifyContent="end" className="gap-3">
        <Button
          variant="secondary"
          onClick={() => nav('/admin/orders')}
          disabled={submitting}
        >
          Cancel
        </Button>
        <Button
          onClick={submit}
          loading={submitting}
          disabled={!selectedCustomer || items.length === 0}
        >
          Create Order
        </Button>
      </Flex>
    </div>
  )
}
