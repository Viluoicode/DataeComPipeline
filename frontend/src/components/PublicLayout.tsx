import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useState } from 'react'
import {
  ShoppingBagIcon,
  ShoppingCartIcon,
  UserCircleIcon,
  Cog6ToothIcon,
  ArrowRightOnRectangleIcon,
  ClipboardDocumentListIcon,
  Bars3Icon,
  XMarkIcon,
} from '@heroicons/react/24/outline'
import clsx from 'clsx'
import { useAuth } from '../contexts/AuthContext'
import { useCart } from '../contexts/CartContext'
import { CartDrawer } from './CartDrawer'

const navItems = [
  { to: '/',     label: 'Home' },
  { to: '/shop', label: 'Shop' },
]

export function PublicLayout() {
  const { user, logout } = useAuth()
  const { itemCount } = useCart()
  const [cartOpen, setCartOpen] = useState(false)
  const [menuOpen, setMenuOpen] = useState(false)
  const [mobileNav, setMobileNav] = useState(false)
  const nav = useNavigate()

  return (
    <div className="min-h-screen flex flex-col bg-gray-950 text-gray-100">
      {/* ── Header ───────────────────────────────────────── */}
      <header className="sticky top-0 z-30 bg-gray-900/80 backdrop-blur border-b border-gray-800">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 h-16 flex items-center justify-between">
          {/* Left: hamburger (mobile) + logo */}
          <div className="flex items-center gap-1">
            <button
              onClick={() => setMobileNav(o => !o)}
              className="md:hidden p-2 -ml-2 rounded-md text-gray-300 hover:bg-gray-800 hover:text-gray-50"
              aria-label="Menu"
            >
              {mobileNav ? <XMarkIcon className="w-6 h-6" /> : <Bars3Icon className="w-6 h-6" />}
            </button>
            <Link to="/" className="flex items-center gap-2 group">
              <div className="w-8 h-8 rounded-md bg-gradient-to-br from-blue-500 to-indigo-600 flex items-center justify-center text-white font-bold text-sm group-hover:scale-105 transition">
                EC
              </div>
              <span className="font-semibold text-gray-50 text-base sm:text-lg">ECommerPipeline</span>
            </Link>
          </div>

          {/* Center nav */}
          <nav className="hidden md:flex items-center gap-6">
            {navItems.map(({ to, label }) => (
              <NavLink
                key={to}
                to={to}
                end={to === '/'}
                className={({ isActive }) =>
                  clsx(
                    'text-sm font-medium transition',
                    isActive ? 'text-blue-400' : 'text-gray-300 hover:text-gray-50'
                  )
                }
              >
                {label}
              </NavLink>
            ))}
          </nav>

          {/* Right actions */}
          <div className="flex items-center gap-2">
            {/* Cart */}
            <button
              onClick={() => setCartOpen(true)}
              className="relative p-2 rounded-md text-gray-300 hover:bg-gray-800 hover:text-gray-50"
              title="Cart"
            >
              <ShoppingCartIcon className="w-6 h-6" />
              {itemCount > 0 && (
                <span className="absolute -top-1 -right-1 bg-blue-500 text-white text-xs font-bold rounded-full w-5 h-5 flex items-center justify-center">
                  {itemCount > 99 ? '99+' : itemCount}
                </span>
              )}
            </button>

            {/* User menu */}
            {user ? (
              <div className="relative">
                <button
                  onClick={() => setMenuOpen(o => !o)}
                  className="flex items-center gap-2 p-2 rounded-md text-gray-300 hover:bg-gray-800 hover:text-gray-50"
                >
                  <UserCircleIcon className="w-6 h-6" />
                  <span className="hidden sm:inline text-sm font-medium">{user.fullName.split(' ').slice(-1)[0]}</span>
                </button>
                {menuOpen && (
                  <>
                    <div className="fixed inset-0 z-20" onClick={() => setMenuOpen(false)} />
                    <div className="absolute right-0 mt-2 w-56 bg-gray-900 border border-gray-800 rounded-md shadow-lg z-30 overflow-hidden">
                      <div className="px-4 py-3 border-b border-gray-800">
                        <div className="text-sm font-medium text-gray-50">{user.fullName}</div>
                        <div className="text-xs text-gray-400 truncate">{user.email}</div>
                      </div>
                      <Link
                        to="/my-orders"
                        onClick={() => setMenuOpen(false)}
                        className="flex items-center gap-2 px-4 py-2 text-sm text-gray-300 hover:bg-gray-800 hover:text-gray-50"
                      >
                        <ClipboardDocumentListIcon className="w-5 h-5" />
                        My Orders
                      </Link>
                      <Link
                        to="/admin"
                        onClick={() => setMenuOpen(false)}
                        className="flex items-center gap-2 px-4 py-2 text-sm text-gray-300 hover:bg-gray-800 hover:text-gray-50"
                      >
                        <Cog6ToothIcon className="w-5 h-5" />
                        Admin Console
                      </Link>
                      <button
                        onClick={() => { logout(); setMenuOpen(false); nav('/') }}
                        className="flex items-center gap-2 px-4 py-2 text-sm text-rose-400 hover:bg-rose-900/30 w-full text-left border-t border-gray-800"
                      >
                        <ArrowRightOnRectangleIcon className="w-5 h-5" />
                        Logout
                      </button>
                    </div>
                  </>
                )}
              </div>
            ) : (
              <Link
                to="/login"
                className="text-sm font-medium text-gray-300 hover:text-gray-50 px-3 py-2"
              >
                Login
              </Link>
            )}
          </div>
        </div>

        {/* Mobile nav dropdown (Home / Shop) */}
        {mobileNav && (
          <div className="md:hidden border-t border-gray-800 bg-gray-900">
            <nav className="max-w-7xl mx-auto px-4 py-2 flex flex-col">
              {navItems.map(({ to, label }) => (
                <NavLink
                  key={to}
                  to={to}
                  end={to === '/'}
                  onClick={() => setMobileNav(false)}
                  className={({ isActive }) =>
                    clsx(
                      'px-2 py-3 rounded-md text-sm font-medium transition',
                      isActive ? 'text-blue-400 bg-blue-900/20' : 'text-gray-300 hover:bg-gray-800 hover:text-gray-50'
                    )
                  }
                >
                  {label}
                </NavLink>
              ))}
            </nav>
          </div>
        )}
      </header>

      {/* ── Main ─────────────────────────────────────────── */}
      <main className="flex-1">
        <Outlet />
      </main>

      {/* ── Footer ───────────────────────────────────────── */}
      <footer className="bg-gray-900 border-t border-gray-800 py-8 mt-12">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 grid grid-cols-1 md:grid-cols-3 gap-6 text-sm">
          <div>
            <div className="flex items-center gap-2 mb-3">
              <ShoppingBagIcon className="w-5 h-5 text-blue-400" />
              <span className="font-semibold text-gray-50">ECommerPipeline</span>
            </div>
            <p className="text-gray-400">
              Demo e-commerce platform với OLTP/OLAP split + real-time analytics.
              Project học tập, không phải shop thật.
            </p>
          </div>
          <div>
            <h4 className="font-semibold text-gray-50 mb-3">For Customers</h4>
            <ul className="space-y-1 text-gray-400">
              <li><Link to="/shop" className="hover:text-gray-50">Shop</Link></li>
              <li><Link to="/my-orders" className="hover:text-gray-50">My Orders</Link></li>
              <li><Link to="/login" className="hover:text-gray-50">Login</Link></li>
            </ul>
          </div>
          <div>
            <h4 className="font-semibold text-gray-50 mb-3">For Operators</h4>
            <ul className="space-y-1 text-gray-400">
              <li><Link to="/admin" className="hover:text-gray-50">Admin Dashboard</Link></li>
              <li><a href="/hangfire" target="_blank" rel="noreferrer" className="hover:text-gray-50">Hangfire ↗</a></li>
              <li><a href="/scalar/v1" target="_blank" rel="noreferrer" className="hover:text-gray-50">API Docs ↗</a></li>
            </ul>
          </div>
        </div>
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 mt-6 pt-6 border-t border-gray-800 text-xs text-gray-500">
          v1.0 — .NET 9 + React + Tremor · Built as a learning project
        </div>
      </footer>

      <CartDrawer open={cartOpen} onClose={() => setCartOpen(false)} />
    </div>
  )
}
