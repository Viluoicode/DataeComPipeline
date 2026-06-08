import { NavLink, Outlet } from 'react-router-dom'
import { useState } from 'react'
import {
  ChartBarSquareIcon,
  ShoppingCartIcon,
  PlusCircleIcon,
  BoltIcon,
  ArrowUpTrayIcon,
  ArrowTopRightOnSquareIcon,
  SparklesIcon,
  Bars3Icon,
  XMarkIcon,
} from '@heroicons/react/24/outline'
import clsx from 'clsx'

const nav = [
  { to: '/admin',            label: 'Dashboard',     icon: ChartBarSquareIcon, end: true },
  { to: '/admin/ask',        label: 'Ask Data (AI)', icon: SparklesIcon,       end: false },
  { to: '/admin/orders',     label: 'Orders',        icon: ShoppingCartIcon,   end: false },
  { to: '/admin/orders/new', label: 'New Order',     icon: PlusCircleIcon,     end: false },
  { to: '/admin/import',     label: 'Import Excel',  icon: ArrowUpTrayIcon,    end: false },
  { to: '/admin/stress',     label: 'Stress Test',   icon: BoltIcon,           end: false },
]

export function AppLayout() {
  // Sidebar is a slide-in drawer on mobile, static on md+.
  const [open, setOpen] = useState(false)
  const close = () => setOpen(false)

  return (
    <div className="min-h-screen md:flex bg-gray-950 text-gray-100">
      {/* Mobile top bar (hidden on md+) */}
      <div className="md:hidden sticky top-0 z-30 flex items-center gap-3 h-14 px-4 bg-gray-900 border-b border-gray-800">
        <button
          onClick={() => setOpen(true)}
          className="p-2 -ml-2 rounded-md text-gray-300 hover:bg-gray-800"
          aria-label="Open menu"
        >
          <Bars3Icon className="w-6 h-6" />
        </button>
        <div className="flex items-center gap-2">
          <div className="w-7 h-7 rounded-md bg-gradient-to-br from-blue-500 to-indigo-600 flex items-center justify-center text-white font-bold text-xs">
            EC
          </div>
          <span className="font-semibold text-gray-50">ECommerPipeline</span>
        </div>
      </div>

      {/* Backdrop when the drawer is open (mobile only) */}
      {open && (
        <div className="fixed inset-0 z-40 bg-black/50 md:hidden" onClick={close} aria-hidden="true" />
      )}

      {/* Sidebar / drawer */}
      <aside
        className={clsx(
          'fixed inset-y-0 left-0 z-50 w-60 bg-gray-900 border-r border-gray-800 flex flex-col',
          'transform transition-transform duration-200 ease-out',
          'md:static md:z-auto md:translate-x-0',
          open ? 'translate-x-0' : '-translate-x-full'
        )}
      >
        <div className="px-5 py-5 border-b border-gray-800 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <div className="w-8 h-8 rounded-md bg-gradient-to-br from-blue-500 to-indigo-600 flex items-center justify-center text-white font-bold text-sm">
              EC
            </div>
            <div>
              <div className="font-semibold text-gray-50">ECommerPipeline</div>
              <div className="text-xs text-gray-400">OLTP → ETL → OLAP</div>
            </div>
          </div>
          <button
            onClick={close}
            className="md:hidden p-1 rounded text-gray-400 hover:bg-gray-800 hover:text-gray-100"
            aria-label="Close menu"
          >
            <XMarkIcon className="w-5 h-5" />
          </button>
        </div>

        <nav className="flex-1 px-3 py-4 space-y-1 overflow-y-auto">
          {nav.map(({ to, label, icon: Icon, end }) => (
            <NavLink
              key={to}
              to={to}
              end={end}
              onClick={close}
              className={({ isActive }) =>
                clsx(
                  'flex items-center gap-3 px-3 py-2 rounded-md text-sm transition',
                  isActive
                    ? 'bg-blue-900/40 text-blue-300 font-medium ring-1 ring-blue-800/60'
                    : 'text-gray-300 hover:bg-gray-800 hover:text-gray-100'
                )
              }
            >
              <Icon className="w-5 h-5 flex-shrink-0" />
              {label}
            </NavLink>
          ))}

          <div className="pt-2 mt-2 border-t border-gray-800">
            <NavLink
              to="/"
              onClick={close}
              className="flex items-center gap-3 px-3 py-2 rounded-md text-sm text-gray-300 hover:bg-gray-800 hover:text-gray-100 transition"
            >
              <ArrowTopRightOnSquareIcon className="w-5 h-5 flex-shrink-0" />
              Storefront
            </NavLink>
            <a
              href="/hangfire"
              target="_blank"
              rel="noreferrer"
              className="flex items-center gap-3 px-3 py-2 rounded-md text-sm text-gray-300 hover:bg-gray-800 hover:text-gray-100 transition"
            >
              <ArrowTopRightOnSquareIcon className="w-5 h-5 flex-shrink-0" />
              Hangfire
            </a>
            <a
              href="/scalar/v1"
              target="_blank"
              rel="noreferrer"
              className="flex items-center gap-3 px-3 py-2 rounded-md text-sm text-gray-300 hover:bg-gray-800 hover:text-gray-100 transition"
            >
              <ArrowTopRightOnSquareIcon className="w-5 h-5 flex-shrink-0" />
              API Docs
            </a>
          </div>
        </nav>

        <div className="px-5 py-3 border-t border-gray-800 text-xs text-gray-500">
          v1.0 — .NET 9 + React
        </div>
      </aside>

      {/* min-w-0 lets wide tables scroll instead of blowing out the layout */}
      <main className="flex-1 min-w-0 overflow-x-hidden">
        <Outlet />
      </main>
    </div>
  )
}
