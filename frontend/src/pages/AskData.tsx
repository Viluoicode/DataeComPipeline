import { useState, useRef, useEffect } from 'react'
import {
  Card, Title, Text, Badge, Table, TableHead, TableHeaderCell,
  TableBody, TableRow, TableCell, type Color,
} from '@tremor/react'
import { PaperAirplaneIcon, SparklesIcon, UserCircleIcon, CommandLineIcon } from '@heroicons/react/24/outline'
import { analystApi, type AnalystResult } from '../api/analyst'
import { isAbortError } from '../api/client'

interface ChatMessage {
  role: 'user' | 'assistant'
  text?: string                 // user question, or assistant error/intro
  result?: AnalystResult        // assistant structured answer
}

// Match the fewShot questions in config/schema.ecommerce.json so the offline
// provider returns a canned answer (real LLM providers handle free-form too).
const SUGGESTIONS = [
  'Doanh thu theo category, sắp xếp giảm dần',
  'Top 10 sản phẩm bán chạy nhất tháng 5 năm 2026',
  'Which customers have the highest lifetime value?',
  'Có bao nhiêu khách hàng đã không mua hàng trong hơn 60 ngày?',
]

export function AskData() {
  const [messages, setMessages] = useState<ChatMessage[]>([{
    role: 'assistant',
    text: 'Xin chào! Hỏi tôi bất cứ điều gì về dữ liệu bán hàng (tiếng Việt hoặc English). Tôi chuyển câu hỏi thành SQL an toàn (chỉ-đọc) trên Gold layer và trả lời.',
  }])
  const [input, setInput] = useState('')
  const [loading, setLoading] = useState(false)
  const endRef = useRef<HTMLDivElement>(null)

  useEffect(() => { endRef.current?.scrollIntoView({ behavior: 'smooth' }) }, [messages, loading])

  async function send(question: string) {
    const q = question.trim()
    if (!q || loading) return
    setMessages(m => [...m, { role: 'user', text: q }])
    setInput('')
    setLoading(true)
    try {
      const result = await analystApi.ask(q, true)
      setMessages(m => [...m, { role: 'assistant', result }])
    } catch (e: unknown) {
      if (isAbortError(e)) return
      const err = e as { response?: { status?: number; data?: { detail?: string } } }
      const msg = err?.response?.status === 503
        ? 'Dịch vụ AI Analyst chưa sẵn sàng. Đảm bảo container analyst-api đang chạy.'
        : err?.response?.data?.detail ?? 'Có lỗi khi hỏi AI.'
      setMessages(m => [...m, { role: 'assistant', text: '⚠️ ' + msg }])
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="p-4 sm:p-6 max-w-4xl mx-auto flex flex-col h-[calc(100dvh-3.5rem)] md:h-screen">
      <div className="mb-4">
        <Title className="!text-2xl flex items-center gap-2">
          <SparklesIcon className="w-6 h-6 text-blue-400" /> Ask Data
        </Title>
        <Text>NL→SQL trên Gold layer · mọi truy vấn được validate (chỉ-đọc, whitelist) trước khi chạy</Text>
      </div>

      {/* Messages */}
      <div className="flex-1 overflow-y-auto space-y-4 pr-1">
        {messages.map((m, i) =>
          m.role === 'user'
            ? <UserBubble key={i} text={m.text!} />
            : <AssistantBubble key={i} text={m.text} result={m.result} />
        )}
        {loading && (
          <div className="flex items-center gap-2 text-gray-400 text-sm">
            <SparklesIcon className="w-5 h-5 animate-pulse text-blue-400" />
            Đang sinh SQL và truy vấn...
          </div>
        )}
        <div ref={endRef} />
      </div>

      {/* Suggestions (only before first question) */}
      {messages.length === 1 && (
        <div className="flex flex-wrap gap-2 my-3">
          {SUGGESTIONS.map(s => (
            <button key={s} onClick={() => send(s)}
              className="px-3 py-1.5 text-xs rounded-full bg-gray-800 hover:bg-gray-700 text-gray-300 border border-gray-700">
              {s}
            </button>
          ))}
        </div>
      )}

      {/* Input */}
      <form onSubmit={e => { e.preventDefault(); send(input) }} className="mt-3 flex gap-2">
        <input
          value={input}
          onChange={e => setInput(e.target.value)}
          placeholder="Hỏi về doanh thu, sản phẩm, khách hàng..."
          disabled={loading}
          className="flex-1 px-4 py-3 rounded-lg bg-gray-900 border border-gray-700 text-gray-100 placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
        />
        <button type="submit" disabled={loading || !input.trim()}
          className="px-4 py-3 rounded-lg bg-blue-500 hover:bg-blue-600 disabled:opacity-50 text-white">
          <PaperAirplaneIcon className="w-5 h-5" />
        </button>
      </form>
    </div>
  )
}

function UserBubble({ text }: { text: string }) {
  return (
    <div className="flex justify-end gap-2">
      <div className="bg-blue-600 text-white rounded-2xl rounded-tr-sm px-4 py-2 max-w-[80%]">{text}</div>
      <UserCircleIcon className="w-7 h-7 text-gray-500 flex-shrink-0" />
    </div>
  )
}

function AssistantBubble({ text, result }: { text?: string; result?: AnalystResult }) {
  return (
    <div className="flex gap-2">
      <SparklesIcon className="w-7 h-7 text-blue-400 flex-shrink-0" />
      <div className="flex-1 max-w-[88%] space-y-2">
        {text && <div className="bg-gray-800 text-gray-100 rounded-2xl rounded-tl-sm px-4 py-2 inline-block">{text}</div>}
        {result && <AnswerCard result={result} />}
      </div>
    </div>
  )
}

function AnswerCard({ result }: { result: AnalystResult }) {
  const [showSql, setShowSql] = useState(false)
  const refused = result.status === 'Refused'
  const statusColor: Color = refused ? 'rose' : 'emerald'

  return (
    <Card className="!p-4">
      <div className="flex items-center justify-between mb-2">
        <Badge color={statusColor}>{result.status}</Badge>
        {result.rowCount != null && !refused && (
          <Text className="text-xs">{result.rowCount} rows</Text>
        )}
      </div>

      {/* Refusal */}
      {refused && result.refusalReasons?.length ? (
        <ul className="text-sm text-rose-300 list-disc pl-5">
          {result.refusalReasons.map((r, i) => <li key={i}>{r}</li>)}
        </ul>
      ) : null}

      {/* NL summary */}
      {result.summary && <Text className="!text-gray-100 mb-2">{result.summary}</Text>}

      {/* Result table */}
      {result.columns?.length && result.rows?.length ? (
        <div className="max-h-72 overflow-auto border border-gray-800 rounded-md mt-2">
          <Table>
            <TableHead>
              <TableRow>
                {result.columns.map(c => <TableHeaderCell key={c}>{c}</TableHeaderCell>)}
              </TableRow>
            </TableHead>
            <TableBody>
              {result.rows.slice(0, 100).map((row, ri) => (
                <TableRow key={ri}>
                  {row.map((cell, ci) => (
                    <TableCell key={ci}>{cell == null ? '—' : String(cell)}</TableCell>
                  ))}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      ) : null}

      {/* Generated SQL (collapsible) */}
      {result.generatedSql && (
        <div className="mt-2">
          <button onClick={() => setShowSql(s => !s)}
            className="text-xs text-blue-400 hover:text-blue-300 flex items-center gap-1">
            <CommandLineIcon className="w-4 h-4" /> {showSql ? 'Ẩn' : 'Xem'} SQL đã sinh
          </button>
          {showSql && (
            <pre className="mt-1 text-xs font-mono bg-gray-950 border border-gray-800 rounded-md p-3 overflow-x-auto text-gray-300">
{result.executedSql || result.generatedSql}
            </pre>
          )}
        </div>
      )}
    </Card>
  )
}
