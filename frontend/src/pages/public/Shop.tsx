import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { MagnifyingGlassIcon, ShoppingCartIcon } from '@heroicons/react/24/outline'
import toast from 'react-hot-toast'
import { productsApi } from '../../api/lookups'
import { useCart } from '../../contexts/CartContext'
import type { ProductLookup } from '../../types/api'
import { formatVnd, productImage } from '../../lib/format'

export function Shop() {
  const { add } = useCart()
  const [products, setProducts] = useState<ProductLookup[]>([])
  const [categories, setCategories] = useState<string[]>([])
  const [search, setSearch] = useState('')
  const [category, setCategory] = useState('')
  const [page, setPage] = useState(1)
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(false)
  const pageSize = 24

  useEffect(() => { productsApi.categories().then(setCategories) }, [])

  useEffect(() => {
    const t = setTimeout(() => {
      setLoading(true)
      productsApi.search(search || undefined, category || undefined, page, pageSize)
        .then(r => { setProducts(r.items); setTotal(r.total) })
        .finally(() => setLoading(false))
    }, 250)
    return () => clearTimeout(t)
  }, [search, category, page])

  const totalPages = Math.max(1, Math.ceil(total / pageSize))

  const handleAdd = (p: ProductLookup) => {
    add(p)
    toast.success(`Đã thêm ${p.name} vào giỏ`)
  }

  return (
    <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      {/* Header */}
      <div className="mb-6">
        <h1 className="text-3xl font-bold text-gray-50">Shop</h1>
        <p className="mt-1 text-gray-400">{total.toLocaleString()} sản phẩm</p>
      </div>

      {/* Filters */}
      <div className="flex flex-col sm:flex-row gap-3 mb-6">
        <div className="relative flex-1">
          <MagnifyingGlassIcon className="w-5 h-5 absolute left-3 top-1/2 -translate-y-1/2 text-gray-500" />
          <input
            type="text"
            placeholder="Tìm theo SKU hoặc tên sản phẩm..."
            value={search}
            onChange={e => { setSearch(e.target.value); setPage(1) }}
            className="w-full pl-10 pr-3 py-2 rounded-md bg-gray-900 border border-gray-800 text-gray-100 placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
        <div className="flex flex-wrap gap-2">
          <CategoryChip label="All" active={category === ''} onClick={() => { setCategory(''); setPage(1) }} />
          {categories.map(c => (
            <CategoryChip
              key={c}
              label={c}
              active={category === c}
              onClick={() => { setCategory(c); setPage(1) }}
            />
          ))}
        </div>
      </div>

      {/* Grid */}
      {loading && products.length === 0 ? (
        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-4">
          {Array.from({ length: 8 }).map((_, i) => (
            <div key={i} className="h-72 bg-gray-900 border border-gray-800 rounded-lg animate-pulse" />
          ))}
        </div>
      ) : products.length === 0 ? (
        <div className="text-center py-20 text-gray-500">
          Không tìm thấy sản phẩm phù hợp.
        </div>
      ) : (
        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-4">
          {products.map(p => (
            <ProductCard key={p.id} product={p} onAdd={() => handleAdd(p)} />
          ))}
        </div>
      )}

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="mt-8 flex items-center justify-center gap-2">
          <button
            disabled={page <= 1}
            onClick={() => setPage(page - 1)}
            className="px-3 py-2 text-sm rounded-md bg-gray-800 disabled:opacity-50 disabled:cursor-not-allowed hover:bg-gray-700"
          >
            ← Previous
          </button>
          <span className="text-sm text-gray-400 px-3">
            Page {page} of {totalPages}
          </span>
          <button
            disabled={page >= totalPages}
            onClick={() => setPage(page + 1)}
            className="px-3 py-2 text-sm rounded-md bg-gray-800 disabled:opacity-50 disabled:cursor-not-allowed hover:bg-gray-700"
          >
            Next →
          </button>
        </div>
      )}
    </div>
  )
}

function CategoryChip({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      className={`px-3 py-2 rounded-md text-sm font-medium transition ${
        active
          ? 'bg-blue-500 text-white'
          : 'bg-gray-800 text-gray-300 hover:bg-gray-700 hover:text-gray-50'
      }`}
    >
      {label}
    </button>
  )
}

function ProductCard({ product, onAdd }: { product: ProductLookup; onAdd: () => void }) {
  return (
    <div className="group bg-gray-900 border border-gray-800 rounded-lg overflow-hidden hover:border-gray-700 transition flex flex-col">
      <Link to={`/shop/${product.id}`} className="block aspect-square overflow-hidden bg-gray-800">
        <img
          src={productImage(product.id, 300, 300)}
          alt={product.name}
          loading="lazy"
          className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
        />
      </Link>
      <div className="p-3 flex flex-col flex-1">
        <div className="text-xs text-gray-500 uppercase tracking-wider">{product.category}</div>
        <Link
          to={`/shop/${product.id}`}
          className="font-medium text-gray-100 hover:text-blue-400 line-clamp-2 mt-1 text-sm"
        >
          {product.name}
        </Link>
        <div className="mt-auto pt-3 flex items-center justify-between">
          <div className="text-blue-400 font-semibold text-sm">{formatVnd(product.price)}</div>
          <button
            onClick={onAdd}
            className="p-2 rounded-md bg-blue-500 hover:bg-blue-600 text-white transition"
            title="Add to cart"
          >
            <ShoppingCartIcon className="w-4 h-4" />
          </button>
        </div>
      </div>
    </div>
  )
}
