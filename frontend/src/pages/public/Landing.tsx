import { Link } from 'react-router-dom'
import {
  ShoppingBagIcon,
  BoltIcon,
  ChartBarIcon,
  CubeTransparentIcon,
  ArrowRightIcon,
} from '@heroicons/react/24/outline'

export function Landing() {
  return (
    <div>
      {/* Hero */}
      <section className="bg-gradient-to-br from-gray-900 via-blue-950 to-indigo-950 border-b border-gray-800">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-20 lg:py-28">
          <div className="grid lg:grid-cols-2 gap-10 items-center">
            <div>
              <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-blue-900/40 text-blue-300 text-xs font-medium mb-4">
                <BoltIcon className="w-4 h-4" />
                Demo OLTP → ETL → OLAP với real-time dashboard
              </div>
              <h1 className="text-4xl lg:text-5xl font-bold text-gray-50 tracking-tight">
                Mua sắm online — <br className="hidden lg:block" />
                <span className="bg-gradient-to-r from-blue-400 to-indigo-400 bg-clip-text text-transparent">
                  analytics realtime
                </span>
              </h1>
              <p className="mt-5 text-lg text-gray-300 max-w-xl">
                Storefront giả lập với 200+ sản phẩm. Mỗi đơn hàng bạn đặt sẽ
                được ghi vào OLTP, đồng bộ qua ETL pipeline, và hiển thị real-time
                trên Admin Dashboard nhờ SignalR.
              </p>
              <div className="mt-8 flex flex-wrap gap-3">
                <Link
                  to="/shop"
                  className="inline-flex items-center gap-2 px-6 py-3 rounded-md bg-blue-500 hover:bg-blue-600 text-white font-medium transition"
                >
                  Shop ngay
                  <ArrowRightIcon className="w-4 h-4" />
                </Link>
                <Link
                  to="/admin"
                  className="inline-flex items-center gap-2 px-6 py-3 rounded-md bg-gray-800 hover:bg-gray-700 text-gray-100 font-medium transition border border-gray-700"
                >
                  <ChartBarIcon className="w-4 h-4" />
                  Admin Dashboard
                </Link>
              </div>
            </div>
            <div className="hidden lg:block">
              <div className="aspect-square rounded-2xl bg-gradient-to-br from-blue-500/20 to-indigo-600/20 border border-blue-800/30 p-8 flex items-center justify-center">
                <ShoppingBagIcon className="w-48 h-48 text-blue-400/60" />
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* Feature grid */}
      <section className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-16">
        <div className="text-center mb-12">
          <h2 className="text-3xl font-bold text-gray-50">Project demo những gì?</h2>
          <p className="mt-3 text-gray-400">
            Đây không phải shop thật — đây là project mô phỏng kiến trúc một e-commerce.
          </p>
        </div>

        <div className="grid sm:grid-cols-2 lg:grid-cols-4 gap-6">
          <FeatureCard
            icon={ShoppingBagIcon}
            title="Storefront"
            description="Browse 200+ products, add to cart, checkout — luồng e-com truyền thống."
            href="/shop"
            cta="Shop ngay"
          />
          <FeatureCard
            icon={ChartBarIcon}
            title="Analytics Dashboard"
            description="Real-time KPI cards + 3 biểu đồ trên 300k+ orders từ OLAP Columnstore."
            href="/admin"
            cta="Mở Admin"
          />
          <FeatureCard
            icon={CubeTransparentIcon}
            title="Excel Import"
            description="Bulk import customers/products/orders từ .xlsx với template sẵn."
            href="/admin/import"
            cta="Try Import"
          />
          <FeatureCard
            icon={BoltIcon}
            title="Stress Test"
            description="Bắn 1000 orders song song để demo OLTP throughput + ETL pipeline."
            href="/admin/stress"
            cta="Stress Test"
          />
        </div>
      </section>

      {/* Architecture */}
      <section className="bg-gray-900 border-y border-gray-800">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-16">
          <div className="grid lg:grid-cols-2 gap-10 items-center">
            <div>
              <h2 className="text-3xl font-bold text-gray-50">CQRS thực tế — 2 database</h2>
              <p className="mt-3 text-gray-300">
                OLTP database (row-store) ghi đơn hàng cực nhanh. OLAP database (Columnstore)
                phục vụ báo cáo. ETL pipeline Hangfire đồng bộ định kỳ giữa 2 bên.
              </p>
              <ul className="mt-6 space-y-3 text-sm text-gray-300">
                <li className="flex items-start gap-2">
                  <span className="text-blue-400 font-bold mt-1">→</span>
                  <span><strong className="text-gray-50">OLTP (EF Core):</strong> INSERT đơn hàng &lt; 50ms, validate qua FluentValidation</span>
                </li>
                <li className="flex items-start gap-2">
                  <span className="text-blue-400 font-bold mt-1">→</span>
                  <span><strong className="text-gray-50">ETL (Hangfire):</strong> Watermark pattern + SqlBulkCopy 5000 row/batch, cron every 5 min</span>
                </li>
                <li className="flex items-start gap-2">
                  <span className="text-blue-400 font-bold mt-1">→</span>
                  <span><strong className="text-gray-50">OLAP (Dapper):</strong> Columnstore Index Batch Mode, query 300k rows trong ~90ms</span>
                </li>
                <li className="flex items-start gap-2">
                  <span className="text-blue-400 font-bold mt-1">→</span>
                  <span><strong className="text-gray-50">Real-time:</strong> SignalR push event khi ETL xong → Admin dashboard tự refresh</span>
                </li>
              </ul>
            </div>
            <div className="bg-gray-950 border border-gray-800 rounded-lg p-6 font-mono text-xs text-gray-300 overflow-x-auto">
              <pre>{`POST /shop → /api/orders
   ↓ (EF Core write)
[OLTP] Customers / Products / Orders
   ↓ (Hangfire job, every 5 min
   ↓  + Polly retry, HOLDLOCK MERGE)
[OLAP] Star schema + CCI
   ↓ (Dapper raw SQL)
GET /api/reports/*
   ↓ (SignalR push)
Admin Dashboard auto-refresh ✓`}</pre>
            </div>
          </div>
        </div>
      </section>

      {/* CTA */}
      <section className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-16 text-center">
        <h2 className="text-3xl font-bold text-gray-50">Sẵn sàng thử chưa?</h2>
        <p className="mt-3 text-gray-400">
          Tạo tài khoản (mock), browse shop, place đơn → xem nó hiện trong Admin Dashboard.
        </p>
        <div className="mt-8 flex flex-wrap justify-center gap-3">
          <Link
            to="/register"
            className="px-6 py-3 rounded-md bg-blue-500 hover:bg-blue-600 text-white font-medium"
          >
            Đăng ký tài khoản
          </Link>
          <Link
            to="/shop"
            className="px-6 py-3 rounded-md bg-gray-800 hover:bg-gray-700 text-gray-100 font-medium border border-gray-700"
          >
            Hoặc browse luôn
          </Link>
        </div>
      </section>
    </div>
  )
}

function FeatureCard({
  icon: Icon, title, description, href, cta,
}: {
  icon: React.ComponentType<{ className?: string }>
  title: string
  description: string
  href: string
  cta: string
}) {
  return (
    <Link
      to={href}
      className="group bg-gray-900 border border-gray-800 rounded-lg p-6 hover:border-blue-700 hover:bg-gray-800/60 transition"
    >
      <div className="w-10 h-10 rounded-md bg-blue-900/30 flex items-center justify-center mb-4 group-hover:bg-blue-900/50 transition">
        <Icon className="w-5 h-5 text-blue-400" />
      </div>
      <h3 className="font-semibold text-gray-50">{title}</h3>
      <p className="mt-2 text-sm text-gray-400">{description}</p>
      <div className="mt-4 inline-flex items-center gap-1 text-blue-400 text-sm font-medium group-hover:gap-2 transition-all">
        {cta} <ArrowRightIcon className="w-4 h-4" />
      </div>
    </Link>
  )
}
