import { Link } from 'react-router-dom'
import { XMarkIcon, TrashIcon, ShoppingBagIcon } from '@heroicons/react/24/outline'
import { useCart } from '../contexts/CartContext'
import { formatVnd, productImage } from '../lib/format'
import { Button, NumberInput } from '@tremor/react'

interface Props {
  open: boolean
  onClose: () => void
}

export function CartDrawer({ open, onClose }: Props) {
  const { items, totalValue, setQuantity, remove, clear } = useCart()

  return (
    <>
      {/* Backdrop */}
      <div
        className={`fixed inset-0 z-40 bg-black/60 transition-opacity ${
          open ? 'opacity-100' : 'opacity-0 pointer-events-none'
        }`}
        onClick={onClose}
      />

      {/* Drawer */}
      <aside
        className={`fixed top-0 right-0 z-50 h-full w-full sm:w-96 bg-gray-900 border-l border-gray-800 shadow-2xl
                    transition-transform duration-300 flex flex-col
                    ${open ? 'translate-x-0' : 'translate-x-full'}`}
      >
        {/* Header */}
        <div className="px-5 py-4 border-b border-gray-800 flex items-center justify-between">
          <h2 className="text-lg font-semibold text-gray-50 flex items-center gap-2">
            <ShoppingBagIcon className="w-5 h-5" />
            Your Cart
          </h2>
          <button
            onClick={onClose}
            className="p-1 rounded-md hover:bg-gray-800 text-gray-400 hover:text-gray-50"
          >
            <XMarkIcon className="w-6 h-6" />
          </button>
        </div>

        {/* Items */}
        <div className="flex-1 overflow-y-auto p-4 space-y-3">
          {items.length === 0 ? (
            <div className="text-center py-12">
              <ShoppingBagIcon className="w-12 h-12 mx-auto text-gray-600 mb-3" />
              <p className="text-gray-400">Cart trống.</p>
              <Link
                to="/shop"
                onClick={onClose}
                className="inline-block mt-4 text-blue-400 hover:text-blue-300 text-sm font-medium"
              >
                Browse shop →
              </Link>
            </div>
          ) : (
            items.map(item => (
              <div key={item.product.id} className="flex gap-3 p-3 bg-gray-800/50 rounded-md">
                <img
                  src={productImage(item.product.id, 80, 80)}
                  alt={item.product.name}
                  className="w-16 h-16 rounded-md object-cover"
                />
                <div className="flex-1 min-w-0">
                  <div className="text-sm font-medium text-gray-100 truncate">{item.product.name}</div>
                  <div className="text-xs text-gray-400">{item.product.category}</div>
                  <div className="text-sm text-blue-400 mt-1">{formatVnd(item.product.price)}</div>
                </div>
                <div className="flex flex-col items-end justify-between">
                  <button
                    onClick={() => remove(item.product.id)}
                    className="text-rose-400 hover:text-rose-300 p-1"
                    title="Remove"
                  >
                    <TrashIcon className="w-4 h-4" />
                  </button>
                  <NumberInput
                    min={1}
                    value={item.quantity}
                    onValueChange={v => setQuantity(item.product.id, v)}
                    className="w-20"
                  />
                </div>
              </div>
            ))
          )}
        </div>

        {/* Footer */}
        {items.length > 0 && (
          <div className="border-t border-gray-800 p-4 space-y-3">
            <div className="flex justify-between text-base">
              <span className="text-gray-300">Subtotal</span>
              <span className="font-semibold text-gray-50">{formatVnd(totalValue)}</span>
            </div>
            <Link to="/checkout" onClick={onClose} className="block">
              <Button className="w-full">Checkout</Button>
            </Link>
            <button
              onClick={clear}
              className="w-full text-xs text-gray-500 hover:text-rose-400 py-1"
            >
              Clear cart
            </button>
          </div>
        )}
      </aside>
    </>
  )
}
