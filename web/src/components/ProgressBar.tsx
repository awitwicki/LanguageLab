interface Props {
  sorted: number
  total: number
}

export function ProgressBar({ sorted, total }: Props) {
  const percent = total === 0 ? 0 : Math.round((sorted / total) * 100)

  return (
    <div className="progress" title={`${sorted} з ${total}`}>
      <div className="progress-fill" style={{ width: `${percent}%` }} />
      <span className="progress-label">
        {percent}% — {sorted} з {total}
      </span>
    </div>
  )
}
