import { api } from './client'

export interface PaymentMethodsAvailability {
  vnpay: boolean
  momo: boolean
}

export interface PaymentCreatedResponse {
  redirectUrl: string
  providerTxnRef: string
}

export const paymentsApi = {
  // Which online gateways are configured (sandbox credentials present).
  methods: () =>
    api.get<PaymentMethodsAvailability>('/api/payments/methods').then(r => r.data),

  // Start an online payment for an order → returns the gateway redirect URL.
  create: (orderId: number) =>
    api.post<PaymentCreatedResponse>(`/api/payments/${orderId}/create`).then(r => r.data),
}
