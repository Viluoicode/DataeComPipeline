import { Button } from '@tremor/react'
import { ExclamationTriangleIcon } from '@heroicons/react/24/outline'

interface Props {
  open: boolean
  title: string
  message: string
  confirmLabel?: string
  cancelLabel?: string
  destructive?: boolean
  onConfirm: () => void
  onCancel: () => void
}

export function ConfirmDialog({
  open, title, message,
  confirmLabel = 'Confirm', cancelLabel = 'Cancel',
  destructive = false, onConfirm, onCancel,
}: Props) {
  if (!open) return null
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4">
      <div className="bg-gray-900 border border-gray-800 rounded-lg shadow-xl max-w-md w-full p-6">
        <div className="flex items-start gap-3">
          <div className={`p-2 rounded-full ${destructive ? 'bg-rose-900/40' : 'bg-blue-900/40'}`}>
            <ExclamationTriangleIcon className={`w-6 h-6 ${destructive ? 'text-rose-400' : 'text-blue-400'}`} />
          </div>
          <div className="flex-1">
            <h3 className="text-lg font-semibold text-gray-50">{title}</h3>
            <p className="mt-2 text-sm text-gray-300">{message}</p>
          </div>
        </div>
        <div className="mt-6 flex justify-end gap-2">
          <Button variant="secondary" onClick={onCancel}>{cancelLabel}</Button>
          <Button color={destructive ? 'rose' : 'blue'} onClick={onConfirm}>{confirmLabel}</Button>
        </div>
      </div>
    </div>
  )
}
