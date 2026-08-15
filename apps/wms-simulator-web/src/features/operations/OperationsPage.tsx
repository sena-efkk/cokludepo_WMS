import { opsApi } from '../../api';
import { Badge, ErrorBox, Section, useAsync } from '../../components/ui';

export function OperationsPage() {
  const health = useAsync(() => opsApi.health(), []);

  return (
    <div>
      <h1>Operations</h1>
      <p className="subtitle">Backend health + altyapı bağlantıları (Grafana frontend içinde yeniden yazılmaz).</p>

      {health.error && <ErrorBox error={health.error} onRetry={health.refresh} />}

      <Section title="Health">
        {health.data && (
          <table>
            <thead>
              <tr>
                <th>Check</th>
                <th>Status</th>
                <th>Detail</th>
              </tr>
            </thead>
            <tbody>
              {Object.entries(health.data.results).map(([name, check]) => (
                <tr key={name}>
                  <td>
                    <strong>{name}</strong>
                  </td>
                  <td>
                    <Badge value={check.status} />
                  </td>
                  <td className="muted">{check.description}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Section>

      <Section title="External Tools">
        <table>
          <tbody>
            <tr>
              <td>Prometheus</td>
              <td className="mono">http://localhost:9090</td>
              <td>
                <a href="http://localhost:9090" target="_blank" rel="noreferrer" style={{ color: 'var(--accent)' }}>
                  Open ↗
                </a>
              </td>
            </tr>
            <tr>
              <td>Grafana (WMS Operations dashboard)</td>
              <td className="mono">http://localhost:3000/d/wms-operations</td>
              <td>
                <a href="http://localhost:3000/d/wms-operations" target="_blank" rel="noreferrer" style={{ color: 'var(--accent)' }}>
                  Open ↗
                </a>
              </td>
            </tr>
            <tr>
              <td>RabbitMQ Management</td>
              <td className="mono">http://localhost:15672</td>
              <td>
                <a href="http://localhost:15672" target="_blank" rel="noreferrer" style={{ color: 'var(--accent)' }}>
                  Open ↗
                </a>
              </td>
            </tr>
            <tr>
              <td>API Metrics</td>
              <td className="mono">/metrics</td>
              <td>
                <a href="/metrics" target="_blank" rel="noreferrer" style={{ color: 'var(--accent)' }}>
                  Open ↗
                </a>
              </td>
            </tr>
          </tbody>
        </table>
      </Section>

      <Section title="Backend Overview">
        <div className="grid grid-2">
          <div className="stat">
            <div className="value">PostgreSQL</div>
            <div className="label">schema-per-module · cross-module FK yok · transactional outbox</div>
          </div>
          <div className="stat">
            <div className="value">RabbitMQ</div>
            <div className="label">at-least-once + inbox idempotency + DLQ</div>
          </div>
        </div>
      </Section>
    </div>
  );
}
