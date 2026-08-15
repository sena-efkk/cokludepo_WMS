import { useCallback, useEffect, useRef, useState } from 'react';

export function Badge({ value }: { value?: string | null }) {
  if (!value) return null;
  const v = value.toLowerCase();
  const cls =
    v === 'green' || v === 'available' || v === 'completed' || v === 'matched' || v === 'shipped' || v === 'optimal' || v === 'recorded'
      ? 'green'
      : v === 'yellow' || v === 'partial' || v === 'short'
        ? 'yellow'
        : v === 'orange'
          ? 'orange'
          : v === 'red' || v === 'rejected' || v === 'notfound' || v === 'cancelled' || v === 'exception' || v === 'stale'
            ? 'red'
            : v === 'inprogress' || v === 'intransit' || v === 'pending' || v === 'receiving' || v === 'picking' || v === 'created'
              ? 'blue'
              : v === 'over' || v === 'damaged' || v === 'quarantine' || v === 'hold'
                ? 'purple'
                : 'muted';

  return <span className={`badge ${cls}`}>{value}</span>;
}

export function StatusBadge({ status }: { status?: string | null }) {
  return <Badge value={status} />;
}

export function Stat({ label, value, accent }: { label: string; value: string | number; accent?: string }) {
  return (
    <div className="stat">
      <div className="value" style={accent ? { color: accent } : undefined}>
        {value}
      </div>
      <div className="label">{label}</div>
    </div>
  );
}

export function ErrorBox({ error, onRetry }: { error?: Error | null; onRetry?: () => void }) {
  if (!error) return null;
  const message = error.message;
  return (
    <div className="error-box" role="alert">
      <strong>{message}</strong>
      {onRetry && (
        <div className="retry">
          <button className="secondary" onClick={onRetry}>
            Retry
          </button>
        </div>
      )}
    </div>
  );
}

export function useAsync<T>(loader: () => Promise<T>, deps: unknown[]) {
  const [state, setState] = useState<{ data?: T; error?: Error | null; loading: boolean }>({ loading: true });
  const requestRef = useRef(0);

  const run = useCallback(() => {
    const requestId = ++requestRef.current;
    setState(s => ({ ...s, loading: true }));
    loader()
      .then(data => {
        if (requestRef.current === requestId) setState({ data, loading: false });
      })
      .catch(error => {
        if (requestRef.current === requestId) {
          setState({ error: error instanceof Error ? error : new Error(String(error)), loading: false });
        }
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);

  useEffect(() => {
    run();
  }, [run]);

  return { ...state, refresh: run };
}

export function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="panel" style={{ marginBottom: 14 }}>
      <h2 style={{ marginTop: 0 }}>{title}</h2>
      {children}
    </section>
  );
}
