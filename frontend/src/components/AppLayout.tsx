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
  { to: '/admin',            label: 'Dashboard',     icon: ChartBarSquareIcon, end: true },
  { to: '/admin/orders',     label: 'Orders',        icon: ShoppingCartIcon,   end: false },
  { to: '/admin/orders/new', label: 'New Order',     icon: PlusCircleIcon,     end: false },
  { to: '/admin/import',     label: 'Import Excel',  icon: ArrowUpTrayIcon,    end: false },
  { to: '/admin/stress',     label: 'Stress Test',   icon: BoltIcon,           end: false },
]

export function AppLayout() {
  return (
    <div className="min-h-screen flex bg-gray-950 text-gray-100">
      <aside className="w-60 bg-gray-900 border-r border-gray-800 flex flex-col">
        <div className="px-5 py-5 border-b border-gray-800">
          <div className="flex items-center gap-2">
            <div className="w-8 h-8 rounded-md bg-gradient-to-br from-blue-500 to-indigo-600 flex items-center justify-center text-white font-bold text-sm">
              EC
            </div>
            <div>
              <div className="font-semibold text-gray-50">ECommerPipeline</div>
              <div className="text-xs text-gray-400">OLTP → ETL → OLAP</div>
            </div>
          </div>
        </div>

        <nav className="flex-1 px-3 py-4 space-y-1">
          {nav.map(({ to, label, icon: Icon, end }) => (
            <NavLink
              key={to}
              to={to}
              end={end}
              className={({ isActive }) =>
                clsx(
                  'flex items-center gap-3 px-3 py-2 rounded-md text-sm transition',
                  isActive
                    ? 'bg-blue-900/40 text-blue-300 font-medium ring-1 ring-blue-800/60'
                    : 'text-gray-300 hover:bg-gray-800 hover:text-gray-100'
                )
              }
            >
              <Icon className="w-5 h-5" />
              {label}
            </NavLink>
          ))}

          <div className="pt-2 mt-2 border-t border-gray-800">
            <NavLink
              to="/"
              className="flex items-center gap-3 px-3 py-2 rounded-md text-sm text-gray-300 hover:bg-gray-800 hover:text-gray-100 transition"
            >
              <ArrowTopRightOnSquareIcon className="w-5 h-5" />
              Storefront
            </NavLink>
            <a
              href="/hangfire"
              target="_blank"
              rel="noreferrer"
              className="flex items-center gap-3 px-3 py-2 rounded-md text-sm text-gray-300 hover:bg-gray-800 hover:text-gray-100 transition"
            >
              <ArrowTopRightOnSquareIcon className="w-5 h-5" />
              Hangfire
            </a>
            <a
              href="/scalar/v1"
              target="_blank"
              rel="noreferrer"
              className="flex items-center gap-3 px-3 py-2 rounded-md text-sm text-gray-300 hover:bg-gray-800 hover:text-gray-100 transition"
            >
              <ArrowTopRightOnSquareIcon className="w-5 h-5" />
              API Docs
            </a>
          </div>
        </nav>

        <div className="px-5 py-3 border-t border-gray-800 text-xs text-gray-500">
          v1.0 — .NET 9 + React
        </div>
      </aside>

      <main className="flex-1 overflow-x-hidden">
        <Outlet />
      </main>
    </div>
  )
}
