import clsx from 'clsx'

interface Props {
  value: string
  onChange: (v: string) => void
  placeholder?: string
  className?: string
}

/** Native <input type="date"> styled to roughly match Tremor's TextInput. */
export function DateInput({ value, onChange, placeholder, className }: Props) {
  return (
    <input
      type="date"
      value={value}
      placeholder={placeholder}
      onChange={e => onChange(e.target.value)}
      className={clsx(
        'w-full px-3 py-2 rounded-tremor-default text-tremor-default',
        'bg-tremor-background dark:bg-dark-tremor-background',
        'border border-tremor-border dark:border-dark-tremor-border',
        'text-tremor-content-emphasis dark:text-dark-tremor-content-emphasis',
        'focus:outline-none focus:ring-2 focus:ring-tremor-brand-muted',
        'placeholder:text-tremor-content-subtle',
        className
      )}
    />
  )
}
