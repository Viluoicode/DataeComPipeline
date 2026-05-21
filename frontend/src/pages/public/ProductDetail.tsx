import { useEffect, useState } from 'react'
import { Link, useParams, useNavigate } from 'react-router-dom'
import { ShoppingCartIcon, ArrowLeftIcon } from '@heroicons/react/24/outline'
import toast from 'react-hot-toast'
import { productsApi } from '../../api/lookups'
import { useCart } from '../../contexts/CartContext'
import type { ProductLookup } from '../../types/api'
import { formatVnd, productImage } from '../../lib/format'

export function ProductDetail() {
  const { id } = useParams<{ id: string }>()
  const nav = useNavigate()
  const { add } = useCart()
  const [product, setProduct] = useState<ProductLookup | null>(null)
  const [loading, setLoading] = useState(true)
  const [qty, setQty] = useState(1)
  const [related, setRelated] = useState<ProductLookup[]>([])

  useEffect(() => {
    if (!id) return
    setLoading(true)
    // No /api/products/{id} endpoint yet — work around by searching with id as page
    productsApi.search(undefined, undefined, 1, 500).then(r => {
      const found = r.items.find(p => p.id === Number(id))
      setProduct(found ?? null)
      if (found) {
        setRelated(r.items.filter(p => p.category === found.category && p.id !== found.id).slice(0, 4))
      }
    }).finally(() => setLoading(false))
  }, [id])

  if (loading) {
    return (
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
        <div className="grid lg:grid-cols-2 gap-8">
          <div className="aspect-square bg-gray-900 rounded-lg animate-pulse" />
          <div className="space-y-4">
            <div className="h-8 bg-gray-900 rounded animate-pulse w-3/4" />
            <div className="h-6 bg-gray-900 rounded animate-pulse w-1/2" />
            <div className="h-32 bg-gray-900 rounded animate-pulse" />
          </div>
        </div>
      </div>
    )
  }

  if (!product) {
    return (
      <div className="max-w-7xl mx-auto px-4 py-20 text-center">
        <p className="text-gray-400 mb-4">Sản phẩm không tồn tại.</p>
        <Link to="/shop" className="text-blue-400 hover:text-blue-300">← Back to shop</Link>
      </div>
    )
  }

  const handleAdd = () => {
    add(product, qty)
    toast.success(`Đã thêm ${qty} × ${product.name}`)
  }

  const handleBuyNow = () => {
    add(product, qty)
    nav('/checkout')
  }

  return (
    <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      <Link to="/shop" className="inline-flex items-center gap-1 text-sm text-blue-400 hover:text-blue-300 mb-6">
        <ArrowLeftIcon className="w-4 h-4" /> Back to shop
      </Link>

      <div className="grid lg:grid-cols-2 gap-8 lg:gap-12">
        {/* Image */}
        <div className="aspect-square rounded-lg overflow-hidden bg-gray-900 border border-gray-800">
          <img
            src={productImage(product.id, 800, 800)}
            alt={product.name}
            className="w-full h-full object-cover"
          />
        </div>

        {/* Info */}
        <div>
          <div className="text-xs text-gray-500 uppercase tracking-wider">{product.category}</div>
          <h1 className="text-3xl font-bold text-gray-50 mt-1">{product.name}</h1>
          <div className="mt-2 flex items-center gap-3">
            <span className="text-sm text-gray-400 font-mono">SKU: {product.sku}</span>
            {product.brand && <span className="px-2 py-0.5 rounded bg-gray-800 text-xs text-gray-300">{product.brand}</span>}
          </div>

          <div className="mt-6 text-3xl font-bold text-blue-400">{formatVnd(product.price)}</div>

          <div className="mt-4 text-sm">
            {product.stockQuantity > 10 ? (
              <span className="text-emerald-400">● Còn hàng ({product.stockQuantity})</span>
            ) : product.stockQuantity > 0 ? (
              <span className="text-amber-400">● Sắp hết ({product.stockQuantity} sản phẩm)</span>
            ) : (
              <span className="text-rose-400">● Hết hàng</span>
            )}
          </div>

          <div className="mt-6 prose prose-invert max-w-none text-gray-300">
            <p>
              {product.brand ? `${product.brand} ` : ''}{product.name} là sản phẩm trong danh mục{' '}
              <strong>{product.category}</strong>. Sản phẩm này là một phần của dataset mock được seed
              tự động bằng Bogus — không phải product thật. Mua thử để xem flow OLTP → OLAP của project.
            </p>
          </div>

          {/* Quantity + Actions */}
          <div className="mt-8 flex items-center gap-3">
            <div className="flex items-center bg-gray-800 rounded-md border border-gray-700">
              <button
                onClick={() => setQty(Math.max(1, qty - 1))}
                className="w-10 h-10 text-gray-400 hover:text-gray-50"
              >
                −
              </button>
              <input
                type="number"
                value={qty}
                min={1}
                max={product.stockQuantity}
                onChange={e => setQty(Math.max(1, Number(e.target.value) || 1))}
                className="w-14 h-10 text-center bg-transparent text-gray-100 border-x border-gray-700 focus:outline-none"
              />
              <button
                onClick={() => setQty(qty + 1)}
                className="w-10 h-10 text-gray-400 hover:text-gray-50"
              >
                +
              </button>
            </div>

            <button
              onClick={handleAdd}
              disabled={product.stockQuantity === 0}
              className="flex-1 inline-flex items-center justify-center gap-2 px-6 py-2.5 rounded-md bg-gray-800 hover:bg-gray-700 text-gray-100 font-medium disabled:opacity-50"
            >
              <ShoppingCartIcon className="w-5 h-5" />
              Add to Cart
            </button>
            <button
              onClick={handleBuyNow}
              disabled={product.stockQuantity === 0}
              className="flex-1 px-6 py-2.5 rounded-md bg-blue-500 hover:bg-blue-600 text-white font-medium disabled:opacity-50"
            >
              Buy Now
            </button>
          </div>
        </div>
      </div>

      {/* Related */}
      {related.length > 0 && (
        <div className="mt-16">
          <h2 className="text-xl font-semibold text-gray-50 mb-4">Liên quan</h2>
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
            {related.map(p => (
              <Link
                key={p.id}
                to={`/shop/${p.id}`}
                className="group block bg-gray-900 border border-gray-800 rounded-lg overflow-hidden hover:border-gray-700 transition"
              >
                <div className="aspect-square bg-gray-800 overflow-hidden">
                  <img
                    src={productImage(p.id, 200, 200)}
                    alt={p.name}
                    loading="lazy"
                    className="w-full h-full object-cover group-hover:scale-105 transition"
                  />
                </div>
                <div className="p-3">
                  <div className="text-sm font-medium text-gray-100 line-clamp-2">{p.name}</div>
                  <div className="text-sm text-blue-400 mt-1">{formatVnd(p.price)}</div>
                </div>
              </Link>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}
