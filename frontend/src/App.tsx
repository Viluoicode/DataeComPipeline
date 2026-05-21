import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { Toaster } from 'react-hot-toast'
import { AuthProvider } from './contexts/AuthContext'
import { CartProvider } from './contexts/CartContext'

// Layouts
import { PublicLayout } from './components/PublicLayout'
import { AppLayout } from './components/AppLayout'

// Public pages (storefront)
import { Landing } from './pages/public/Landing'
import { Shop } from './pages/public/Shop'
import { ProductDetail } from './pages/public/ProductDetail'
import { Checkout } from './pages/public/Checkout'
import { MyOrders } from './pages/public/MyOrders'
import { Login } from './pages/public/Login'
import { Register } from './pages/public/Register'

// Admin pages
import { Dashboard } from './pages/Dashboard'
import { OrdersList } from './pages/OrdersList'
import { CreateOrder } from './pages/CreateOrder'
import { OrderDetail } from './pages/OrderDetail'
import { ImportPage } from './pages/ImportPage'
import { StressTest } from './pages/StressTest'

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
            {/* ── Public storefront ── */}
            <Route element={<PublicLayout />}>
              <Route path="/"               element={<Landing />} />
              <Route path="/shop"           element={<Shop />} />
              <Route path="/shop/:id"       element={<ProductDetail />} />
              <Route path="/checkout"       element={<Checkout />} />
              <Route path="/my-orders"      element={<MyOrders />} />
              <Route path="/login"          element={<Login />} />
              <Route path="/register"       element={<Register />} />
            </Route>

            {/* ── Admin console (existing pages, now prefixed /admin) ── */}
            <Route path="/admin" element={<AppLayout />}>
              <Route index                  element={<Dashboard />} />
              <Route path="orders"          element={<OrdersList />} />
              <Route path="orders/new"      element={<CreateOrder />} />
              <Route path="orders/:id"      element={<OrderDetail />} />
              <Route path="import"          element={<ImportPage />} />
              <Route path="stress"          element={<StressTest />} />
            </Route>

            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </BrowserRouter>
      </CartProvider>
    </AuthProvider>
  )
}
