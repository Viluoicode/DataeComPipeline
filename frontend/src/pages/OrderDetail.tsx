import { useEffect, useState } from 'react'
import { useParams, Link } from 'react-router-dom'
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
  Badge,
  Button,
  type Color,
} from '@tremor/react'
import { ordersApi } from '../api/orders'
import { OrderStatusLabel, type OrderDetail as Detail } from '../types/api'

const statusColor: Record<number, Color> = {
  1: 'amber', 2: 'blue', 3: 'indigo', 4: 'emerald', 5: 'rose',
}

function formatVnd(n: number): string {
  return n.toLocaleString('vi-VN', { maximumFractionDigits: 0 }) + ' ₫'
}

export function OrderDetail() {
  const { id } = useParams<{ id: string }>()
  const [order, setOrder] = useState<Detail | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!id) return
    setLoading(true)
    ordersApi.getById(Number(id))
      .then(setOrder)
      .catch(e => setError(e?.response?.status === 404
        ? 'Order not found'
        : e?.message ?? 'Failed to load order'))
      .finally(() => setLoading(false))
  }, [id])

  if (loading) return <div className="p-6"><Text>Loading...</Text></div>
  if (error)   return <div className="p-6"><Card decoration="left" decorationColor="rose"><Text color="rose">{error}</Text></Card></div>
  if (!order)  return null

  return (
    <div className="p-6 space-y-6">
      <Flex justifyContent="between" alignItems="start">
        <div>
          <Link to="/orders" className="text-sm text-blue-500 hover:underline">← Back to orders</Link>
          <Title className="!text-2xl mt-1">{order.orderNumber}</Title>
          <Text>Placed {new Date(order.orderDate).toLocaleString('vi-VN')}</Text>
        </div>
        <Badge size="lg" color={statusColor[order.status] ?? 'gray'}>
          {OrderStatusLabel[order.status] ?? `Status #${order.status}`}
        </Badge>
      </Flex>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        <Card>
          <Text>Customer</Text>
          <Title className="!text-lg">{order.customerName}</Title>
          <Text className="text-xs">{order.customerEmail}</Text>
        </Card>
        <Card>
          <Text>Items</Text>
          <Title className="!text-lg">{order.items.length}</Title>
        </Card>
        <Card decoration="top" decorationColor="emerald">
          <Text>Total</Text>
          <Title className="!text-lg">{formatVnd(order.totalAmount)}</Title>
        </Card>
      </div>

      <Card>
        <Title>Line Items</Title>
        <Table className="mt-4">
          <TableHead>
            <TableRow>
              <TableHeaderCell>SKU</TableHeaderCell>
              <TableHeaderCell>Product</TableHeaderCell>
              <TableHeaderCell className="text-right">Qty</TableHeaderCell>
              <TableHeaderCell className="text-right">Unit Price</TableHeaderCell>
              <TableHeaderCell className="text-right">Line Total</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {order.items.map(i => (
              <TableRow key={`${i.productId}`}>
                <TableCell className="font-mono text-xs">{i.productSku}</TableCell>
                <TableCell>{i.productName}</TableCell>
                <TableCell className="text-right">{i.quantity}</TableCell>
                <TableCell className="text-right">{formatVnd(i.unitPrice)}</TableCell>
                <TableCell className="text-right font-medium">{formatVnd(i.lineTotal)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Card>

      <Flex justifyContent="end">
        <Link to="/orders">
          <Button variant="secondary">← Back</Button>
        </Link>
      </Flex>
    </div>
  )
}
