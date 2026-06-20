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

// ---- Phase 4: business-state analytics ----
export interface PaymentMethodSalesRow {
  paymentMethod: number
  methodName: string
  orderCount: number
  paidOrderCount: number
  totalRevenue: number
  paidRevenue: number
}

export interface OrderFunnelRow {
  stage: string
  stageOrder: number
  orderCount: number
}

export interface ProductInventoryRow {
  productId: number
  sku: string
  productName: string
  category: string
  currentStock: number
  unitsSold: number
  lowStock: boolean
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

// Mirrors Domain/Enums/PaymentMethod.cs and PaymentStatus.cs (1-based).
export const PaymentMethod = { Cod: 1, VnPay: 2, Momo: 3 } as const
export type PaymentMethodValue = (typeof PaymentMethod)[keyof typeof PaymentMethod]

export const PaymentMethodLabel: Record<number, string> = {
  1: 'COD', 2: 'VNPay', 3: 'MoMo',
}

export const PaymentStatusLabel: Record<number, string> = {
  1: 'Chưa thanh toán',
  2: 'Đang chờ thanh toán',
  3: 'Đã thanh toán',
  4: 'Thanh toán thất bại',
  5: 'Đã hoàn tiền',
}

export interface CreateOrderRequest {
  customerId: number
  items: { productId: number; quantity: number }[]
  shipFullName?: string
  shipPhone?: string
  shipAddress?: string
  note?: string
  paymentMethod?: PaymentMethodValue
}

export interface OrderCreatedResponse {
  orderId: number
  orderNumber: string
  subtotal: number
  shippingFee: number
  taxAmount: number
  totalAmount: number
  paymentMethod: number
  paymentStatus: number
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
  paymentStatus: number
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

export interface OrderEvent {
  fromStatus: number | null
  toStatus: number
  actorCustomerId: number | null
  reason: string | null
  at: string
}

export interface OrderDetail {
  id: number
  orderNumber: string
  customerId: number
  customerName: string
  customerEmail: string
  orderDate: string
  status: number
  paymentMethod: number
  paymentStatus: number
  shipFullName: string | null
  shipPhone: string | null
  shipAddress: string | null
  note: string | null
  subtotal: number
  shippingFee: number
  taxAmount: number
  totalAmount: number
  items: OrderItemDetail[]
  events: OrderEvent[]
  nextStatuses: number[]
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
  imageUrl?: string | null
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
