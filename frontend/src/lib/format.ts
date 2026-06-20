/** Format a number as Vietnamese Dong with thousand separators. */
export function formatVnd(n: number): string {
  return n.toLocaleString('vi-VN', { maximumFractionDigits: 0 }) + ' ₫'
}

/** Compact format for big numbers in KPI tiles (e.g. 22.3T VND, 1.4M). */
export function formatCompact(n: number, currency = false): string {
  if (Math.abs(n) >= 1_000_000_000_000) return (n / 1_000_000_000_000).toFixed(1) + 'T' + (currency ? ' ₫' : '')
  if (Math.abs(n) >= 1_000_000_000)     return (n / 1_000_000_000).toFixed(1)     + 'B' + (currency ? ' ₫' : '')
  if (Math.abs(n) >= 1_000_000)         return (n / 1_000_000).toFixed(1)         + 'M' + (currency ? ' ₫' : '')
  if (Math.abs(n) >= 1_000)             return (n / 1_000).toFixed(1)             + 'K' + (currency ? ' ₫' : '')
  return n.toLocaleString('vi-VN') + (currency ? ' ₫' : '')
}

/** Format an ISO datetime string as locale date-time. */
export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString('vi-VN', {
    day: '2-digit', month: '2-digit', year: 'numeric',
    hour: '2-digit', minute: '2-digit',
  })
}

/** Format an ISO datetime string as just the date. */
export function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('vi-VN', {
    day: '2-digit', month: '2-digit', year: 'numeric',
  })
}

/** Product image: the uploaded image when present, else a stable placeholder. */
export function productImage(
  seed: string | number, width = 400, height = 300, imageUrl?: string | null,
): string {
  if (imageUrl) return imageUrl
  return `https://picsum.photos/seed/ecom-${seed}/${width}/${height}`
}
