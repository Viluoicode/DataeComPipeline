import { NavLink, Outlet } from 'react-router-dom'
import {
  ChartBarSquareIcon,
  ShoppingCartIcon,
  PlusCircleIcon,
  BoltIcon,
  ArrowUpTrayIcon,
  ArrowTopRightOnSquareIcon,
} from '@heroicons/react/24/outline'
import clsx from 'clsx'

const nav = [
  { to: '/',            label: 'Dashboard',     icon: ChartBarSquareIcon },
  { to: '/orders',      label: 'Orders',        icon: ShoppingCartIcon },
  { to: '/orders/new',  label: 'New Order',     icon: PlusCircleIcon },
  { to: '/import',      label: 'Import Excel',  icon: ArrowUpTrayIcon },
  { to: '/stress',      label: 'Stress Test',   icon: BoltIcon },
]

export function AppLayout() {
  return (
    <div className="min-h-screen flex bg-gray-50 dark:bg-gray-950 dark">
      <aside className="w-60 bg-white dark:bg-gray-900 border-r border-gray-200 dark:border-gray-800 flex flex-col">
        <div className="px-5 py-5 border-b border-gray-200 dark:border-gray-800">
          <div className="flex items-center gap-2">
            <div className="w-8 h-8 rounded-md bg-gradient-to-br from-blue-500 to-indigo-600 flex items-center justify-center text-white font-bold text-sm">
              EC
            </div>
            <div>
              <div className="font-semibold text-gray-900 dark:text-gray-50">ECommerPipeline</div>
              <div className="text-xs text-gray-500">OLTP → ETL → OLAP</div>
            </div>
          </div>
        </div>

        <nav className="flex-1 px-3 py-4 space-y-1">
          {nav.map(({ to, label, icon: Icon }) => (
            <NavLink
              key={to}
              to={to}
              end={to === '/'}
              className={({ isActive }) =>
                clsx(
                  'flex items-center gap-3 px-3 py-2 rounded-md text-sm transition',
                  isActive
                    ? 'bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300 font-medium'
                    : 'text-gray-700 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-800'
                )
              }
            >
              <Icon className="w-5 h-5" />
              {label}
            </NavLink>
          ))}

          <a
            href="/hangfire"
            target="_blank"
            rel="noreferrer"
            className="flex items-center gap-3 px-3 py-2 rounded-md text-sm text-gray-700 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-800 transition"
          >
            <ArrowTopRightOnSquareIcon className="w-5 h-5" />
            Hangfire
          </a>
          <a
            href="/scalar/v1"
            target="_blank"
            rel="noreferrer"
            className="flex items-center gap-3 px-3 py-2 rounded-md text-sm text-gray-700 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-800 transition"
          >
            <ArrowTopRightOnSquareIcon className="w-5 h-5" />
            API Docs
          </a>
        </nav>

        <div className="px-5 py-3 border-t border-gray-200 dark:border-gray-800 text-xs text-gray-500">
          v1.0 — .NET 9 + React
        </div>
      </aside>

      <main className="flex-1 overflow-x-hidden">
        <Outlet />
      </main>
    </div>
  )
}
