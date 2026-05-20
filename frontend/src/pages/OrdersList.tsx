import { useEffect, useState, useCallback } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import {
  Card,
  Title,
  Text,
  Flex,
  Table,
  TableHead,
  TableHeaderCell,
  TableBody,
  TableRow,
  TableCell,
  TextInput,
  Select,
  SelectItem,
  Button,
  Badge,
  type Color,
} from '@tremor/react'
import { ordersApi, type OrderListQuery } from '../api/orders'
import { OrderStatusLabel, type OrderListItem } from '../types/api'
import { DateInput } from '../components/DateInput'

const statusColor: Record<number, Color> = {
  1: 'amber',    // Pending
  2: 'blue',     // Confirmed
  3: 'indigo',   // Shipped
  4: 'emerald',  // Delivered
  5: 'rose',     // Cancelled
}

function formatVnd(n: number): string {
  return n.toLocaleString('vi-VN', { maximumFractionDigits: 0 }) + ' ₫'
}

export function OrdersList() {
  const nav = useNavigate()
  const [query, setQuery] = useState<OrderListQuery>({ page: 1, pageSize: 20 })
  const [items, setItems] = useState<OrderListItem[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [searchInput, setSearchInput] = useState('')

  const load = useCallback(async (q: OrderListQuery) => {
    setLoading(true)
    setError(null)
    try {
      const res = await ordersApi.list(q)
      setItems(res.items)
      setTotal(res.total)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load orders')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { load(query) }, [load, query])

  const totalPages = Math.max(1, Math.ceil(total / (query.pageSize ?? 20)))
  const currentPage = query.page ?? 1

  return (
    <div className="p-6 space-y-6">
      <Flex justifyContent="between" alignItems="center">
        <div>
          <Title className="!text-2xl">Orders</Title>
          <Text>All orders from OLTP database — paged & filterable</Text>
        </div>
        <Link to="/orders/new">
          <Button>+ New Order</Button>
        </Link>
      </Flex>

      <Card>
        <Flex justifyContent="start" className="gap-3 flex-wrap">
          <div className="w-64">
            <Text>Search (order # / customer)</Text>
            <TextInput
              placeholder="ORD-... or customer name"
              value={searchInput}
              onValueChange={setSearchInput}
              onKeyDown={(e) => {
                if (e.key === 'Enter')
                  setQuery({ ...query, page: 1, search: searchInput || undefined })
              }}
            />
          </div>

          <div className="w-40">
            <Text>Status</Text>
            <Select
              value={query.status?.toString() ?? ''}
              onValueChange={(v) =>
                setQuery({ ...query, page: 1, status: v ? Number(v) : undefined })
              }
            >
              <SelectItem value="">All</SelectItem>
              <SelectItem value="1">Pending</SelectItem>
              <SelectItem value="2">Confirmed</SelectItem>
              <SelectItem value="3">Shipped</SelectItem>
              <SelectItem value="4">Delivered</SelectItem>
              <SelectItem value="5">Cancelled</SelectItem>
            </Select>
          </div>

          <div className="w-40">
            <Text>From</Text>
            <DateInput
              value={query.from ?? ''}
              onChange={(v) => setQuery({ ...query, page: 1, from: v || undefined })}
            />
          </div>

          <div className="w-40">
            <Text>To</Text>
            <DateInput
              value={query.to ?? ''}
              onChange={(v) => setQuery({ ...query, page: 1, to: v || undefined })}
            />
          </div>

          <div className="self-end">
            <Button
              variant="secondary"
              onClick={() => {
                setSearchInput('')
                setQuery({ page: 1, pageSize: 20 })
              }}
            >
              Clear
            </Button>
          </div>
        </Flex>
      </Card>

      {error && (
        <Card decoration="left" decorationColor="rose">
          <Text color="rose">⚠️ {error}</Text>
        </Card>
      )}

      <Card>
        <Flex justifyContent="between" className="mb-4">
          <Text>
            Showing {items.length === 0 ? 0 : (currentPage - 1) * (query.pageSize ?? 20) + 1}
            {' - '}
            {Math.min(currentPage * (query.pageSize ?? 20), total)} of {total.toLocaleString()} orders
          </Text>
          {loading && <Text color="blue">Loading...</Text>}
        </Flex>

        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>Order #</TableHeaderCell>
              <TableHeaderCell>Customer</TableHeaderCell>
              <TableHeaderCell>Date</TableHeaderCell>
              <TableHeaderCell>Status</TableHeaderCell>
              <TableHeaderCell className="text-right">Items</TableHeaderCell>
              <TableHeaderCell className="text-right">Total</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {items.map(o => (
              <TableRow
                key={o.id}
                className="cursor-pointer hover:bg-gray-100 dark:hover:bg-gray-800"
                onClick={() => nav(`/orders/${o.id}`)}
              >
                <TableCell className="font-mono text-xs">{o.orderNumber}</TableCell>
                <TableCell>{o.customerName}</TableCell>
                <TableCell>{new Date(o.orderDate).toLocaleString('vi-VN')}</TableCell>
                <TableCell>
                  <Badge color={statusColor[o.status] ?? 'gray'}>
                    {OrderStatusLabel[o.status] ?? `#${o.status}`}
                  </Badge>
                </TableCell>
                <TableCell className="text-right">{o.itemCount}</TableCell>
                <TableCell className="text-right font-medium">{formatVnd(o.totalAmount)}</TableCell>
              </TableRow>
            ))}
            {items.length === 0 && !loading && (
              <TableRow>
                <TableCell colSpan={6} className="text-center py-8 text-gray-500">
                  No orders match the filters
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>

        <Flex justifyContent="end" className="mt-4 gap-2">
          <Button
            size="xs"
            variant="secondary"
            disabled={currentPage <= 1 || loading}
            onClick={() => setQuery({ ...query, page: currentPage - 1 })}
          >
            ← Previous
          </Button>
          <Text className="self-center px-3">
            Page {currentPage} of {totalPages}
          </Text>
          <Button
            size="xs"
            variant="secondary"
            disabled={currentPage >= totalPages || loading}
            onClick={() => setQuery({ ...query, page: currentPage + 1 })}
          >
            Next →
          </Button>
        </Flex>
      </Card>
    </div>
  )
}
