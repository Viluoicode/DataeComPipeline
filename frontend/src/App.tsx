import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { AppLayout } from './components/AppLayout'
import { Dashboard } from './pages/Dashboard'
import { OrdersList } from './pages/OrdersList'
import { CreateOrder } from './pages/CreateOrder'
import { OrderDetail } from './pages/OrderDetail'
import { ImportPage } from './pages/ImportPage'
import { StressTest } from './pages/StressTest'

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<AppLayout />}>
          <Route path="/"             element={<Dashboard />} />
          <Route path="/orders"       element={<OrdersList />} />
          <Route path="/orders/new"   element={<CreateOrder />} />
          <Route path="/orders/:id"   element={<OrderDetail />} />
          <Route path="/import"       element={<ImportPage />} />
          <Route path="/stress"       element={<StressTest />} />
          <Route path="*"             element={<Navigate to="/" replace />} />
        </Route>
      </Routes>
    </BrowserRouter>
  )
}
