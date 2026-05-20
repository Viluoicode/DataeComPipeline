// Types match the C# DTOs in the backend Application layer.
// Keep these in sync with:
//   src/ECommerPipeline.Application/Reports/DTOs/SalesReportDtos.cs
//   src/ECommerPipeline.Application/Orders/DTOs/*.cs
//   src/ECommerPipeline.Application/Customers/DTOs/CustomerDtos.cs
//   src/ECommerPipeline.Application/Products/DTOs/ProductDtos.cs

// ---------------- Reports (OLAP read path) ----------------
export interface SalesByCategoryRow {
  category: string
  orderCount: number
  totalRevenue: number
}

export interface SalesByDayRow {
  day: string // ISO date string
  orderCount: number
  totalRevenue: number
}

export interface TopProductRow {
  productId: number
  sku: string
  name: string
  totalQuantity: number
  totalRevenue: number
}

// ---------------- Orders ----------------
export type OrderStatus = 'Pending' | 'Confirmed' | 'Shipped' | 'Delivered' | 'Cancelled'

// Backend serializes enums as number by default
export const OrderStatusLabel: Record<number, OrderStatus> = {
  1: 'Pending',
  2: 'Confirmed',
  3: 'Shipped',
  4: 'Delivered',
  5: 'Cancelled',
}

export interface CreateOrderRequest {
  customerId: number
  items: { productId: number; quantity: number }[]
}

export interface OrderCreatedResponse {
  orderId: number
  orderNumber: string
  totalAmount: number
}

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
  totalPages: number
}

export interface OrderListItem {
  id: number
  orderNumber: string
  customerId: number
  customerName: string
  orderDate: string
  status: number
  totalAmount: number
  itemCount: number
}

export interface OrderItemDetail {
  productId: number
  productSku: string
  productName: string
  quantity: number
  unitPrice: number
  lineTotal: number
}

export interface OrderDetail {
  id: number
  orderNumber: string
  customerId: number
  customerName: string
  customerEmail: string
  orderDate: string
  status: number
  totalAmount: number
  items: OrderItemDetail[]
}

// ---------------- Customers / Products ----------------
export interface CustomerLookup {
  id: number
  fullName: string
  email: string
  phone?: string
  city?: string
}

export interface ProductLookup {
  id: number
  sku: string
  name: string
  category: string
  brand?: string
  price: number
  stockQuantity: number
}

// ---------------- Admin ----------------
export interface EtlEnqueuedResponse {
  status: string
  jobId: string
  dashboard: string
  at: string
}

export interface ImportResult {
  totalRows: number
  successCount: number
  errorCount: number
  errors: { row: number; message: string }[]
}
