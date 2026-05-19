// Types match the C# DTOs in the backend Application layer.
// Keep these in sync with:
//   src/ECommerPipeline.Application/Reports/DTOs/SalesReportDtos.cs
//   src/ECommerPipeline.Application/Orders/DTOs/CreateOrderRequest.cs

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

export interface CreateOrderRequest {
  customerId: number
  items: { productId: number; quantity: number }[]
}

export interface OrderCreatedResponse {
  orderId: number
  orderNumber: string
  totalAmount: number
}

export interface EtlEnqueuedResponse {
  status: string
  jobId: string
  dashboard: string
  at: string
}
