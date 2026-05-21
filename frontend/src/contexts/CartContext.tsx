import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import type { ProductLookup } from '../types/api'

export interface CartItem {
  product: ProductLookup
  quantity: number
}

interface CartContextValue {
  items: CartItem[]
  itemCount: number          // sum of quantities
  totalValue: number         // sum of price * qty
  add: (product: ProductLookup, qty?: number) => void
  setQuantity: (productId: number, qty: number) => void
  remove: (productId: number) => void
  clear: () => void
}

const CartContext = createContext<CartContextValue | null>(null)
const STORAGE_KEY = 'ecom.cart'

export function CartProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<CartItem[]>(() => {
    try {
      const raw = localStorage.getItem(STORAGE_KEY)
      return raw ? JSON.parse(raw) as CartItem[] : []
    } catch { return [] }
  })

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(items))
  }, [items])

  const value = useMemo<CartContextValue>(() => ({
    items,
    itemCount:  items.reduce((s, i) => s + i.quantity, 0),
    totalValue: items.reduce((s, i) => s + i.product.price * i.quantity, 0),
    add: (product, qty = 1) => setItems(prev => {
      const existing = prev.find(x => x.product.id === product.id)
      if (existing) return prev.map(x => x.product.id === product.id
        ? { ...x, quantity: x.quantity + qty } : x)
      return [...prev, { product, quantity: qty }]
    }),
    setQuantity: (productId, qty) => setItems(prev =>
      prev.map(x => x.product.id === productId
        ? { ...x, quantity: Math.max(1, qty) } : x)),
    remove: (productId) => setItems(prev => prev.filter(x => x.product.id !== productId)),
    clear: () => setItems([]),
  }), [items])

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>
}

export function useCart() {
  const ctx = useContext(CartContext)
  if (!ctx) throw new Error('useCart must be used within CartProvider')
  return ctx
}
