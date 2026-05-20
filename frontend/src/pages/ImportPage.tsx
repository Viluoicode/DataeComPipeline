import { useState } from 'react'
import {
  Card,
  Title,
  Text,
  Flex,
  Button,
  TabGroup,
  TabList,
  Tab,
  TabPanels,
  TabPanel,
  Metric,
  Grid,
  Table,
  TableHead,
  TableHeaderCell,
  TableBody,
  TableRow,
  TableCell,
} from '@tremor/react'
import {
  ArrowDownTrayIcon,
  ArrowUpTrayIcon,
  CheckCircleIcon,
  ExclamationTriangleIcon,
} from '@heroicons/react/24/outline'
import { importApi, type ImportKind } from '../api/imports'
import type { ImportResult } from '../types/api'

const tabs: { key: ImportKind; label: string; columns: string[]; note: string }[] = [
  {
    key: 'customers',
    label: 'Customers',
    columns: ['FullName', 'Email', 'Phone', 'City'],
    note: 'Email phải duy nhất. Phone & City có thể bỏ trống.',
  },
  {
    key: 'products',
    label: 'Products',
    columns: ['Sku', 'Name', 'Category', 'Brand', 'Price', 'StockQuantity'],
    note: 'Sku phải duy nhất. Price > 0. Brand có thể bỏ trống.',
  },
  {
    key: 'orders',
    label: 'Orders',
    columns: ['OrderRef', 'CustomerEmail', 'Sku', 'Quantity'],
    note: 'Mỗi dòng = 1 line. Cùng OrderRef → gom thành 1 đơn. Customer/Product lookup bằng Email/Sku.',
  },
]

export function ImportPage() {
  const [activeKind, setActiveKind] = useState<ImportKind>('orders')
  const [file, setFile] = useState<File | null>(null)
  const [uploading, setUploading] = useState(false)
  const [result, setResult] = useState<ImportResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function handleUpload() {
    if (!file) return
    setUploading(true)
    setError(null)
    setResult(null)
    try {
      const r = await importApi.upload(activeKind, file)
      setResult(r)
    } catch (e: unknown) {
      const err = e as { response?: { data?: string | { detail?: string } } }
      const msg = typeof err?.response?.data === 'string'
        ? err.response.data
        : err?.response?.data?.detail ?? 'Upload failed'
      setError(msg)
    } finally {
      setUploading(false)
    }
  }

  function reset() {
    setFile(null)
    setResult(null)
    setError(null)
  }

  return (
    <div className="p-6 space-y-6">
      <div>
        <Title className="!text-2xl">Excel Import</Title>
        <Text>
          Bulk import customers, products, or orders từ file .xlsx. Tải template
          mẫu trước khi điền dữ liệu để khớp với header backend đang expect.
        </Text>
      </div>

      <Card>
        <TabGroup
          index={tabs.findIndex(t => t.key === activeKind)}
          onIndexChange={i => { setActiveKind(tabs[i].key); reset() }}
        >
          <TabList>
            {tabs.map(t => <Tab key={t.key}>{t.label}</Tab>)}
          </TabList>

          <TabPanels>
            {tabs.map(t => (
              <TabPanel key={t.key}>
                <Text className="mt-4">{t.note}</Text>

                <div className="mt-4">
                  <Text className="font-medium mb-2">Required columns:</Text>
                  <div className="flex flex-wrap gap-2">
                    {t.columns.map(c => (
                      <span
                        key={c}
                        className="px-2 py-1 text-xs font-mono bg-gray-100 dark:bg-gray-800 rounded-md"
                      >
                        {c}
                      </span>
                    ))}
                  </div>
                </div>

                <Flex justifyContent="start" className="gap-3 mt-6 flex-wrap">
                  <Button
                    icon={ArrowDownTrayIcon}
                    variant="secondary"
                    onClick={() => importApi.downloadTemplate(t.key)}
                  >
                    Download {t.label} Template
                  </Button>

                  <label className="cursor-pointer">
                    <input
                      type="file"
                      accept=".xlsx"
                      className="hidden"
                      onChange={e => { setFile(e.target.files?.[0] ?? null); setResult(null); setError(null) }}
                    />
                    <span className="inline-flex items-center gap-2 px-4 py-2 rounded-md bg-blue-500 text-white hover:bg-blue-600 text-sm">
                      📎 Choose .xlsx file
                    </span>
                  </label>

                  {file && (
                    <Text className="self-center">
                      📄 <span className="font-mono">{file.name}</span> ({(file.size / 1024).toFixed(1)} KB)
                    </Text>
                  )}

                  <Button
                    icon={ArrowUpTrayIcon}
                    onClick={handleUpload}
                    disabled={!file}
                    loading={uploading}
                  >
                    Upload & Import
                  </Button>

                  {(file || result || error) && (
                    <Button variant="light" onClick={reset}>Reset</Button>
                  )}
                </Flex>
              </TabPanel>
            ))}
          </TabPanels>
        </TabGroup>
      </Card>

      {error && (
        <Card decoration="left" decorationColor="rose">
          <Flex justifyContent="start" className="gap-2">
            <ExclamationTriangleIcon className="w-5 h-5 text-rose-500" />
            <Text color="rose">{error}</Text>
          </Flex>
        </Card>
      )}

      {result && (
        <>
          <Grid numItemsMd={3} className="gap-6">
            <Card decoration="top" decorationColor="gray">
              <Text>Total Rows</Text>
              <Metric>{result.totalRows.toLocaleString()}</Metric>
            </Card>
            <Card decoration="top" decorationColor="emerald">
              <Flex justifyContent="start" className="gap-2">
                <CheckCircleIcon className="w-5 h-5 text-emerald-500" />
                <Text>Imported</Text>
              </Flex>
              <Metric>{result.successCount.toLocaleString()}</Metric>
            </Card>
            <Card decoration="top" decorationColor={result.errorCount === 0 ? 'gray' : 'rose'}>
              <Flex justifyContent="start" className="gap-2">
                <ExclamationTriangleIcon className={`w-5 h-5 ${result.errorCount === 0 ? 'text-gray-400' : 'text-rose-500'}`} />
                <Text>Errors</Text>
              </Flex>
              <Metric>{result.errorCount.toLocaleString()}</Metric>
            </Card>
          </Grid>

          {result.errors.length > 0 && (
            <Card>
              <Title>Errors ({result.errors.length})</Title>
              <Table className="mt-4">
                <TableHead>
                  <TableRow>
                    <TableHeaderCell className="w-24">Row</TableHeaderCell>
                    <TableHeaderCell>Message</TableHeaderCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {result.errors.slice(0, 100).map((e, idx) => (
                    <TableRow key={idx}>
                      <TableCell>{e.row}</TableCell>
                      <TableCell className="text-rose-600 dark:text-rose-400">{e.message}</TableCell>
                    </TableRow>
                  ))}
                  {result.errors.length > 100 && (
                    <TableRow>
                      <TableCell colSpan={2} className="text-center text-gray-500">
                        ... and {result.errors.length - 100} more
                      </TableCell>
                    </TableRow>
                  )}
                </TableBody>
              </Table>
            </Card>
          )}
        </>
      )}
    </div>
  )
}
