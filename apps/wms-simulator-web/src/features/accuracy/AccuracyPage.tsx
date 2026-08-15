import { useState } from 'react';
import { accuracyApi } from '../../api';
import type { CycleCountTaskInfo, ReconciliationInfo, RiskAssessment } from '../../api/types';
import { Badge, ErrorBox, Section, Stat, useAsync } from '../../components/ui';
import { newRequestId } from '../../api/http';

export function AccuracyPage() {
  const summary = useAsync(() => accuracyApi.summary(), []);
  const risk = useAsync(() => accuracyApi.highRisk(), []);
  const cycleCounts = useAsync(() => accuracyApi.cycleCounts(), []);
  const reconciliations = useAsync(() => accuracyApi.reconciliations(), []);
  const notFound = useAsync(() => accuracyApi.recentNotFound(), []);

  return (
    <div>
      <h1>Inventory Accuracy</h1>
      <p className="subtitle">Risk → Cycle Count → Reconciliation (stok yalnız reconciliation ile düzelir).</p>

      <div className="grid grid-4">
        <Stat label="High-Risk Locations (RED)" value={summary.data?.highRiskLocations ?? '—'} accent="#e4574f" />
        <Stat label="Open Cycle Counts" value={summary.data?.openCycleCounts ?? '—'} />
        <Stat label="Open Reconciliations" value={summary.data?.openReconciliations ?? '—'} />
        <Stat label="PickNotFound (24h)" value={summary.data?.recentPickNotFound ?? '—'} accent="#ef8f3b" />
      </div>

      <Section title="Risk Distribution (RED)">
        {risk.error && <ErrorBox error={risk.error} onRetry={risk.refresh} />}
        <table>
          <thead>
            <tr>
              <th>Warehouse</th>
              <th>SKU</th>
              <th>Location</th>
              <th>Score</th>
              <th>Level</th>
              <th>Reasons</th>
            </tr>
          </thead>
          <tbody>
            {risk.data?.slice(0, 50).map(r => (
              <RiskRow key={`${r.warehouseId}-${r.skuId}-${r.locationId}`} risk={r} />
            ))}
            {risk.data?.length === 0 && (
              <tr>
                <td colSpan={6} className="muted">
                  RED risk yok.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </Section>

      <Section title="Cycle Count Queue">
        {cycleCounts.error && <ErrorBox error={cycleCounts.error} onRetry={cycleCounts.refresh} />}
        <table>
          <thead>
            <tr>
              <th>Task</th>
              <th>Reason</th>
              <th>Priority</th>
              <th>Status</th>
              <th>SKU / Location</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {cycleCounts.data?.slice(0, 50).map(t => (
              <CycleCountRow key={t.id} task={t} onDone={cycleCounts.refresh} />
            ))}
          </tbody>
        </table>
      </Section>

      <Section title="Reconciliations">
        {reconciliations.error && <ErrorBox error={reconciliations.error} onRetry={reconciliations.refresh} />}
        <table>
          <thead>
            <tr>
              <th>Case</th>
              <th>SKU</th>
              <th>Expected</th>
              <th>Counted</th>
              <th>Variance</th>
              <th>Status</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {reconciliations.data?.slice(0, 50).map(r => (
              <ReconciliationRow key={r.id} reconciliation={r} onDone={reconciliations.refresh} />
            ))}
          </tbody>
        </table>
      </Section>

      <Section title="Recent PickNotFound Signals">
        <table>
          <thead>
            <tr>
              <th>Time</th>
              <th>SKU</th>
              <th>Location</th>
              <th>System Qty At Signal</th>
              <th>Source</th>
            </tr>
          </thead>
          <tbody>
            {notFound.data?.slice(0, 30).map(s => (
              <tr key={s.id}>
                <td className="muted mono">{new Date(s.occurredAt).toLocaleString()}</td>
                <td className="mono">{s.skuId.slice(0, 8)}…</td>
                <td className="mono">{s.locationId.slice(0, 8)}…</td>
                <td>{s.systemQuantityAtSignal}</td>
                <td className="mono">{s.sourceReferenceId?.slice(0, 8) ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Section>
    </div>
  );
}

function RiskRow({ risk }: { risk: RiskAssessment }) {
  return (
    <tr>
      <td className="mono">{risk.warehouseId.slice(0, 8)}…</td>
      <td className="mono">{risk.skuId.slice(0, 8)}…</td>
      <td className="mono">{risk.locationId.slice(0, 8)}…</td>
      <td>{risk.riskScore}</td>
      <td>
        <Badge value={risk.riskLevel} />
      </td>
      <td className="muted">
        {risk.reasons.map(r => (
          <div key={r.code}>
            {r.description} <span className="mono">(+{r.points})</span>
          </div>
        ))}
      </td>
    </tr>
  );
}

function CycleCountRow({ task, onDone }: { task: CycleCountTaskInfo; onDone: () => void }) {
  const [counted, setCounted] = useState<number | ''>('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error | null>(null);

  const start = async () => {
    setBusy(true);
    setError(null);
    try {
      await accuracyApi.startCycleCount(task.id);
      onDone();
    } catch (e) {
      setError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setBusy(false);
    }
  };

  const complete = async () => {
    setBusy(true);
    setError(null);
    try {
      await accuracyApi.completeCycleCount(task.id, Number(counted));
      onDone();
    } catch (e) {
      setError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setBusy(false);
    }
  };

  return (
    <tr>
      <td className="mono">{task.id.slice(0, 8)}…</td>
      <td>
        <Badge value={task.reason} />
      </td>
      <td>
        <Badge value={task.priority} />
      </td>
      <td>
        <Badge value={task.status} />
      </td>
      <td className="mono">
        SKU {task.skuId.slice(0, 8)}… @ {task.locationId.slice(0, 8)}…
      </td>
      <td>
        {task.status === 'Pending' && (
          <button className="secondary" onClick={start} disabled={busy}>
            Start
          </button>
        )}
        {task.status === 'InProgress' && (
          <div className="row">
            <input
              type="number"
              placeholder="Counted Qty (blind)"
              value={counted}
              onChange={e => setCounted(e.target.value === '' ? '' : Number(e.target.value))}
              style={{ width: 150 }}
            />
            <button onClick={complete} disabled={busy || counted === ''}>
              Complete Blind Count
            </button>
          </div>
        )}
        {error && <ErrorBox error={error} />}
      </td>
    </tr>
  );
}

function ReconciliationRow({ reconciliation, onDone }: { reconciliation: ReconciliationInfo; onDone: () => void }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error | null>(null);

  const approve = async () => {
    setBusy(true);
    setError(null);
    try {
      await accuracyApi.approveReconciliation(reconciliation.id, {
        requestId: newRequestId(),
        reason: 'CYCLE_COUNT_VARIANCE',
        resolvedBy: 'demo-operator',
        resolutionNote: 'UI onayı',
      });
      onDone();
    } catch (e) {
      setError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setBusy(false);
    }
  };

  const reject = async () => {
    setBusy(true);
    setError(null);
    try {
      await accuracyApi.rejectReconciliation(reconciliation.id, { resolvedBy: 'demo-operator', resolutionNote: 'UI reddi' });
      onDone();
    } catch (e) {
      setError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setBusy(false);
    }
  };

  return (
    <tr>
      <td className="mono">{reconciliation.id.slice(0, 8)}…</td>
      <td className="mono">{reconciliation.skuId.slice(0, 8)}…</td>
      <td>{reconciliation.expectedQuantity}</td>
      <td>{reconciliation.countedQuantity}</td>
      <td style={{ color: reconciliation.variance !== 0 ? 'var(--red)' : undefined }}>{reconciliation.variance}</td>
      <td>
        <Badge value={reconciliation.reconciliationStatus} />
        {reconciliation.isLargeVariance && <span className="badge orange">LARGE</span>}
      </td>
      <td>
        {reconciliation.reconciliationStatus === 'Open' && (
          <div className="row">
            <button onClick={approve} disabled={busy}>
              Approve
            </button>
            <button className="danger" onClick={reject} disabled={busy}>
              Reject
            </button>
          </div>
        )}
        {error && <ErrorBox error={error} />}
      </td>
    </tr>
  );
}
