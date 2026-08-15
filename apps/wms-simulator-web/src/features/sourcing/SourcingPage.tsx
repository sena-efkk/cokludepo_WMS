import { useState } from 'react';
import { fulfillmentApi, masterDataApi } from '../../api';
import type { OptimizedPlan, SourcingEvaluation, StrategyComparison } from '../../api/types';
import { Badge, ErrorBox, Section, useAsync } from '../../components/ui';
import { newRequestId } from '../../api/http';

export function SourcingPage() {
  const skus = useAsync(() => masterDataApi.listSkus(), []);
  const [lines, setLines] = useState<{ skuId: string; quantity: number }[]>([{ skuId: '', quantity: 2 }]);
  const [strategy, setStrategy] = useState('compare');
  const [destination, setDestination] = useState('Bursa / Nilüfer');
  const [lat, setLat] = useState<number>(40.19);
  const [lon, setLon] = useState<number>(29.07);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error | null>(null);
  const [evaluation, setEvaluation] = useState<SourcingEvaluation | null>(null);
  const [commitError, setCommitError] = useState<Error | null>(null);
  const [committedLinks, setCommittedLinks] = useState<{ warehouseId: string; outboundOrderId: string; orderNumber: string }[]>([]);

  const evaluate = async () => {
    setBusy(true);
    setError(null);
    setEvaluation(null);
    setCommittedLinks([]);
    try {
      const result = await fulfillmentApi.evaluate({
        requestId: newRequestId(),
        destination,
        destinationLatitude: lat,
        destinationLongitude: lon,
        strategy,
        lines: lines.filter(l => l.skuId),
      });
      setEvaluation(result);
    } catch (e) {
      setError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setBusy(false);
    }
  };

  const commit = async (plan: OptimizedPlan) => {
    setBusy(true);
    setCommitError(null);
    try {
      const result = await fulfillmentApi.commit(evaluation!.sourcingRequestId, {
        requestId: newRequestId(),
        plan: plan.warehouses
          .map(w => ({
            warehouseId: w.warehouseId,
            lines: w.lines.filter(l => l.fulfillable).map(l => ({ skuId: l.skuId, quantity: l.requestedQuantity })),
          }))
          .filter(w => w.lines.length > 0),
        optimization: {
          strategyUsed: plan.strategyUsed,
          status: plan.status,
          totalCost: plan.cost.totalCost,
          totalDistanceKm: plan.totalDistanceKm,
          routeSource: plan.routeSource,
          explanations: plan.explanations,
        },
      });
      setCommittedLinks(result.orderLinks);
      setCommitError(null);
    } catch (e) {
      setCommitError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div>
      <h1>Sourcing &amp; Optimization</h1>
      <p className="subtitle">Backend değerlendirir; frontend skor hesaplamaz. Explainable decisions gerçek response'tan gelir.</p>

      <Section title="Order Demand">
        <div className="row">
          <div>
            <label htmlFor="src-dest">Customer Destination</label>
            <input id="src-dest" value={destination} onChange={e => setDestination(e.target.value)} style={{ minWidth: 200 }} />
          </div>
          <div>
            <label htmlFor="src-lat">Latitude</label>
            <input id="src-lat" type="number" step="0.0001" value={lat} onChange={e => setLat(Number(e.target.value))} />
          </div>
          <div>
            <label htmlFor="src-lon">Longitude</label>
            <input id="src-lon" type="number" step="0.0001" value={lon} onChange={e => setLon(Number(e.target.value))} />
          </div>
          <div>
            <label htmlFor="src-strategy">Strategy</label>
            <select id="src-strategy" value={strategy} onChange={e => setStrategy(e.target.value)}>
              {['nearest', 'greedy', 'optimized', 'compare'].map(s => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </div>
        </div>

        <h2 style={{ marginTop: 12 }}>Order Lines</h2>
        {lines.map((line, index) => (
          <div className="row" key={index} style={{ marginBottom: 6 }}>
            <select
              value={line.skuId}
              onChange={e => {
                const next = [...lines];
                next[index] = { ...next[index], skuId: e.target.value };
                setLines(next);
              }}
            >
              <option value="">— SKU —</option>
              {skus.data?.slice(0, 300).map(s => (
                <option key={s.id} value={s.id}>
                  {s.code}
                </option>
              ))}
            </select>
            <input
              type="number"
              min={1}
              value={line.quantity}
              onChange={e => {
                const next = [...lines];
                next[index] = { ...next[index], quantity: Number(e.target.value) };
                setLines(next);
              }}
              style={{ width: 90 }}
            />
            <button
              className="secondary"
              onClick={() => setLines(lines.filter((_, i) => i !== index))}
              disabled={lines.length <= 1}
            >
              ✕
            </button>
          </div>
        ))}
        <div className="row">
          <button className="secondary" onClick={() => setLines([...lines, { skuId: '', quantity: 1 }])}>
            + Line
          </button>
          <button onClick={evaluate} disabled={busy || lines.every(l => !l.skuId)}>
            Evaluate
          </button>
        </div>
      </Section>

      {error && <ErrorBox error={error} />}

      {evaluation && (
        <>
          {!evaluation.fulfillable && (
            <div className="error-box">
              <strong>UNFULFILLABLE</strong>
              {evaluation.shortages.map(s => (
                <div key={s.skuId}>
                  {s.skuCode}: requested {s.requestedQuantity}, network ATP {s.networkAtp}, shortage {s.shortage}
                </div>
              ))}
            </div>
          )}

          {evaluation.comparison && <ComparisonPanel comparison={evaluation.comparison} onCommit={commit} busy={busy} />}
          {evaluation.optimization && <OptimizedPanel plan={evaluation.optimization} onCommit={commit} busy={busy} />}

          {!evaluation.optimization && !evaluation.comparison && (
            <Section title="Feasible Candidates (Phase 14)">
              <table>
                <thead>
                  <tr>
                    <th>#</th>
                    <th>Warehouse</th>
                    <th>Coverage</th>
                    <th>Score</th>
                    <th>Explanations</th>
                  </tr>
                </thead>
                <tbody>
                  {evaluation.candidates.map(c => (
                    <tr key={c.rank}>
                      <td>{c.rank}</td>
                      <td>
                        <strong>{c.warehouseCode}</strong>
                        {c.warehouses.length > 1 && <span className="badge purple">SPLIT ×{c.warehouses.length}</span>}
                      </td>
                      <td>
                        {c.fulfillableLineCount}/{c.totalLineCount} {c.canFulfillCompletely && <Badge value="FULL" />}
                      </td>
                      <td>{c.score}</td>
                      <td className="muted">
                        {c.explanations.map((e, i) => (
                          <div key={i}>{e}</div>
                        ))}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </Section>
          )}
        </>
      )}

      {commitError && (
        <div className="error-box">
          <strong>SOURCING_STALE</strong> — Stock changed since evaluation. Re-evaluate sourcing.
          <div className="muted">{commitError instanceof Error ? commitError.message : String(commitError)}</div>
        </div>
      )}

      {committedLinks.length > 0 && (
        <Section title="Committed — Outbound Orders">
          {committedLinks.map(link => (
            <div className="explanation" key={link.outboundOrderId}>
              Warehouse order <strong>{link.orderNumber}</strong> —{' '}
              <a href={`#/outbound`} style={{ color: 'var(--accent)' }}>
                Outbound sayfasında görüntüle
              </a>
            </div>
          ))}
        </Section>
      )}
    </div>
  );
}

function ComparisonPanel({
  comparison,
  onCommit,
  busy,
}: {
  comparison: StrategyComparison;
  onCommit: (plan: OptimizedPlan) => void;
  busy: boolean;
}) {
  const plans: { key: string; label: string; plan: OptimizedPlan }[] = [];
  if (comparison.nearest) plans.push({ key: 'nearest', label: 'Nearest', plan: comparison.nearest });
  if (comparison.greedy) plans.push({ key: 'greedy', label: 'Greedy', plan: comparison.greedy });
  if (comparison.optimized) plans.push({ key: 'optimized', label: 'Optimized', plan: comparison.optimized });

  const bestPlan = plans.find(p => p.label.toLowerCase() === comparison.recommendedStrategy.toLowerCase())?.plan ?? plans[0]?.plan;

  return (
    <>
      <Section title="Strategy Comparison">
        <div className="explanation">
          Recommended: <strong>{comparison.recommendedStrategy}</strong>
          {comparison.savingsVsNearest != null && (
            <span className="muted"> — savings vs nearest: {comparison.savingsVsNearest.toFixed(2)}</span>
          )}
        </div>
        <table>
          <thead>
            <tr>
              <th />
              {plans.map(p => (
                <th key={p.key}>{p.label}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            <tr>
              <td>Warehouses</td>
              {plans.map(p => (
                <td key={p.key}>{p.plan.warehouses.map(w => w.warehouseCode).join(' + ')}</td>
              ))}
            </tr>
            <tr>
              <td>Distance (km)</td>
              {plans.map(p => (
                <td key={p.key}>{p.plan.totalDistanceKm.toFixed(1)}</td>
              ))}
            </tr>
            <tr>
              <td>Shipments</td>
              {plans.map(p => (
                <td key={p.key}>{p.plan.shipmentCount}</td>
              ))}
            </tr>
            <tr>
              <td>Risk Penalty</td>
              {plans.map(p => (
                <td key={p.key}>{p.plan.cost.inventoryReliabilityPenalty.toFixed(2)}</td>
              ))}
            </tr>
            <tr>
              <td>Split Penalty</td>
              {plans.map(p => (
                <td key={p.key}>{p.plan.cost.splitPenalty.toFixed(2)}</td>
              ))}
            </tr>
            <tr>
              <td>
                <strong>Total Cost</strong>
              </td>
              {plans.map(p => (
                <td key={p.key}>
                  <strong style={{ color: p.plan === bestPlan ? 'var(--green)' : undefined }}>{p.plan.cost.totalCost.toFixed(2)}</strong>
                </td>
              ))}
            </tr>
            <tr>
              <td>Route Source</td>
              {plans.map(p => (
                <td key={p.key}>
                  {p.plan.routeSource.includes('HAVERSINE_FALLBACK') ? (
                    <span className="badge orange">HAVERSINE_FALLBACK</span>
                  ) : (
                    <Badge value={p.plan.routeSource} />
                  )}
                </td>
              ))}
            </tr>
            <tr>
              <td>Status</td>
              {plans.map(p => (
                <td key={p.key}>
                  <Badge value={p.plan.status} /> <span className="muted">{p.plan.strategyUsed}</span>
                </td>
              ))}
            </tr>
          </tbody>
        </table>

        <h2>Counterfactuals</h2>
        {comparison.counterfactuals.map((c, i) => (
          <div className="explanation counterfactual" key={i}>
            {c}
          </div>
        ))}
      </Section>

      {bestPlan && <PlanPanel plan={bestPlan} onCommit={onCommit} busy={busy} />}
    </>
  );
}

function OptimizedPanel({ plan, onCommit, busy }: { plan: OptimizedPlan; onCommit: (plan: OptimizedPlan) => void; busy: boolean }) {
  return (
    <>
      <div className="explanation">
        Strategy: <strong>{plan.strategyUsed}</strong> · Status: <Badge value={plan.status} /> · Route:{' '}
        {plan.routeSource.includes('HAVERSINE_FALLBACK') ? <span className="badge orange">HAVERSINE_FALLBACK</span> : <Badge value={plan.routeSource} />}
      </div>
      <PlanPanel plan={plan} onCommit={onCommit} busy={busy} />
    </>
  );
}

function PlanPanel({ plan, onCommit, busy }: { plan: OptimizedPlan; onCommit: (plan: OptimizedPlan) => void; busy: boolean }) {
  return (
    <Section title="Selected Plan">
      <div className="grid grid-4">
        <div className="stat">
          <div className="value">{plan.warehouses.map(w => w.warehouseCode).join(' + ')}</div>
          <div className="label">Warehouses</div>
        </div>
        <div className="stat">
          <div className="value">{plan.totalDistanceKm.toFixed(1)} km</div>
          <div className="label">Distance</div>
        </div>
        <div className="stat">
          <div className="value">{plan.shipmentCount}</div>
          <div className="label">Shipments</div>
        </div>
        <div className="stat">
          <div className="value" style={{ color: 'var(--green)' }}>
            {plan.cost.totalCost.toFixed(2)}
          </div>
          <div className="label">Total Cost</div>
        </div>
      </div>

      <h2>Cost Breakdown (backend)</h2>
      <table>
        <tbody>
          <tr><td>Transport</td><td>{plan.cost.transportCost.toFixed(2)}</td></tr>
          <tr><td>Dispatch</td><td>{plan.cost.dispatchCost.toFixed(2)}</td></tr>
          <tr><td>Packaging</td><td>{plan.cost.packagingCost.toFixed(2)}</td></tr>
          <tr><td>Handling</td><td>{plan.cost.handlingCost.toFixed(2)}</td></tr>
          <tr><td>Picking</td><td>{plan.cost.pickingCost.toFixed(2)}</td></tr>
          <tr><td>Split</td><td>{plan.cost.splitPenalty.toFixed(2)}</td></tr>
          <tr><td>Reliability</td><td>{plan.cost.inventoryReliabilityPenalty.toFixed(2)}</td></tr>
          <tr><td>Scarcity</td><td>{plan.cost.scarcityPenalty.toFixed(2)}</td></tr>
          <tr><td>SLA</td><td>{plan.cost.slaPenalty.toFixed(2)}</td></tr>
          <tr>
            <td><strong>Total</strong></td>
            <td><strong>{plan.cost.totalCost.toFixed(2)}</strong></td>
          </tr>
        </tbody>
      </table>

      <h2>Why selected?</h2>
      {plan.explanations.map((e, i) => (
        <div className="explanation" key={i}>
          {e}
        </div>
      ))}

      <div style={{ marginTop: 12 }}>
        <button onClick={() => onCommit(plan)} disabled={busy}>
          Commit Plan
        </button>
      </div>
    </Section>
  );
}
