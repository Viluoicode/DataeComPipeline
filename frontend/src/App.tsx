import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { Toaster } from 'react-hot-toast'
import { AuthProvider } from './contexts/AuthContext'
import { CartProvider } from './contexts/CartContext'

// Layouts + guards
import { PublicLayout } from './components/PublicLayout'
import { AppLayout } from './components/AppLayout'
import { ProtectedRoute } from './components/ProtectedRoute'

// Public pages (storefront)
import { Landing } from './pages/public/Landing'
import { Shop } from './pages/public/Shop'
import { ProductDetail } from './pages/public/ProductDetail'
import { Checkout } from './pages/public/Checkout'
import { PaymentReturn } from './pages/public/PaymentReturn'
import { MyOrders } from './pages/public/MyOrders'
import { OrderTracking } from './pages/public/OrderTracking'
import { Addresses } from './pages/public/Addresses'
import { Login } from './pages/public/Login'
import { Register } from './pages/public/Register'

// Admin pages
import { Dashboard } from './pages/Dashboard'
import { OrdersList } from './pages/OrdersList'
import { CreateOrder } from './pages/CreateOrder'
import { OrderDetail } from './pages/OrderDetail'
import { ProductsAdmin } from './pages/ProductsAdmin'
import { ImportPage } from './pages/ImportPage'
import { StressTest } from './pages/StressTest'
import { AskData } from './pages/AskData'

export default function App() {
  return (
    <AuthProvider>
      <CartProvider>
        <BrowserRouter>
          <Toaster
            position="bottom-right"
            toastOptions={{
              style: { background: '#1f2937', color: '#f3f4f6', border: '1px solid #374151' },
              success: { iconTheme: { primary: '#10b981', secondary: '#f3f4f6' } },
              error:   { iconTheme: { primary: '#f43f5e', secondary: '#f3f4f6' } },
            }}
          />
          <Routes>
            {/* ── Public storefront — open to everyone ── */}
            <Route element={<PublicLayout />}>
              <Route path="/"               element={<Landing />} />
              <Route path="/shop"           element={<Shop />} />
              <Route path="/shop/:id"       element={<ProductDetail />} />
              <Route path="/login"          element={<Login />} />
              <Route path="/register"       element={<Register />} />
              {/* Gateway redirect lands here (public — session is restored from storage) */}
              <Route path="/payment-result" element={<PaymentReturn />} />

              {/* Logged-in customer area */}
              <Route element={<ProtectedRoute />}>
                <Route path="/checkout"     element={<Checkout />} />
                <Route path="/my-orders"    element={<MyOrders />} />
                <Route path="/my-orders/:id" element={<OrderTracking />} />
                <Route path="/addresses"    element={<Addresses />} />
              </Route>
            </Route>

            {/* ── Admin console — Admin or Staff role required ── */}
            <Route element={<ProtectedRoute requireRole="Staff" />}>
              <Route path="/admin" element={<AppLayout />}>
                <Route index                element={<Dashboard />} />
                <Route path="orders"        element={<OrdersList />} />
                <Route path="orders/new"    element={<CreateOrder />} />
                <Route path="orders/:id"    element={<OrderDetail />} />
                <Route path="products"      element={<ProductsAdmin />} />
                <Route path="ask"           element={<AskData />} />
                <Route path="import"        element={<ImportPage />} />
                <Route path="stress"        element={<StressTest />} />
              </Route>
            </Route>

            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </BrowserRouter>
      </CartProvider>
    </AuthProvider>
  )
}
