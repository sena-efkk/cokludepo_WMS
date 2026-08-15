import { useState } from 'react';
import { opsApi } from '../../api';
import { ErrorBox, Section } from '../../components/ui';

const SCENARIOS = [
  {
    id: 'demo_small',
    title: 'Scenario 0 — Synthetic Dataset',
    description:
      '3 demo warehouse (Bursa/İstanbul/İnegöl), 12 SKU, location hiyerarşileri, stok dağılımı ve bir açık receipt. Gerçek use case\'lerle kurulur — doğrudan SQL YOK.',
    steps: ['Initialize'],
  },
  {
    id: 'normal_fulfillment',
    title: 'Scenario 1 — Normal Fulfillment',
    description: 'Inbound → Stock → Order → Sourcing → Pick → Ship zincirini gerçek backend üzerinden yürütün.',
    steps: ['Initialize (stock kur)', 'Inbound: create receipt + receive + putaway', 'Sourcing: evaluate + commit', 'Outbound: pick → pack → ship', 'Inventory: ledger kontrol'],
  },
  {
    id: 'phantom_inventory',
    title: 'Scenario 2 — Phantom Inventory',
    description: 'Sistem 5 stok görüyor ama fizikselde yok: PickNotFound ×2 → RED risk → CycleCount → Reconciliation → Adjustment.',
    steps: [
      'Initialize (stok kur)',
      'Outbound: order + allocate → Pick [Not Found] ×2 (farklı siparişlerle)',
      'Accuracy: risk RED görünür',
      'Accuracy: Cycle Count → Start → blind count 0',
      'Accuracy: Reconciliation → Approve (variance -5)',
      'Inventory: ledger + ATP düzeltildi',
    ],
  },
  {
    id: 'warehouse_transfer',
    title: 'Scenario 3 — Warehouse Transfer',
    description: 'A → InTransit → B; partial receive; network physical invariant her adımda görünür.',
    steps: ['Initialize', 'Transfers: create + allocate + ship', 'Transfers: partial receive', 'Transfers: final receive + variance gerekirse'],
  },
  {
    id: 'fragmented_inventory',
    title: 'Scenario 4 — Fragmented Inventory',
    description: 'Split gerektiren siparişte Nearest vs Greedy vs Optimized karşılaştırması.',
    steps: ['Initialize (bölük stok kur)', 'Sourcing: strategy=compare ile evaluate', 'Comparison tablosu + counterfactuals', 'Commit selected plan'],
  },
  {
    id: 'sourcing_race',
    title: 'Scenario 5 — Sourcing Race',
    description: 'Evaluate sonrası stok başka işlem tarafından alınır → Commit SOURCING_STALE ile döner.',
    steps: ['Initialize', 'Sourcing: evaluate (ATP=1 plan görünür)', 'Başka bir işlem stoğu reserve etsin (Outbound/Inventory)', 'Sourcing: Commit → SOURCING_STALE hatası görünür'],
  },
];

export function ScenariosPage() {
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<Error | null>(null);
  const [result, setResult] = useState<{ scenario: string; warehousesCreated: number; skusCreated: number; stockLocations: number; receiptsCreated: number } | null>(null);

  const initialize = async (scenario: string) => {
    setBusy(scenario);
    setError(null);
    try {
      const r = await opsApi.scenarioInit(scenario);
      setResult({ scenario, ...r });
    } catch (e) {
      setError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setBusy(null);
    }
  };

  return (
    <div>
      <h1>Simulation Scenarios</h1>
      <p className="subtitle">
        Initialize gerçek backend state'ini controlled şekilde kurar (application use case'ler üzerinden). Sonraki adımları kullanıcı kendi yürütür.
      </p>

      {error && <ErrorBox error={error} />}
      {result && (
        <div className="explanation">
          <strong>{result.scenario}</strong> kuruldu: {result.warehousesCreated} warehouse, {result.skusCreated} SKU, {result.stockLocations} stok
          lokasyonu, {result.receiptsCreated} açık receipt.
        </div>
      )}

      <div className="grid grid-2">
        {SCENARIOS.map(s => (
          <Section title={s.title} key={s.id}>
            <p className="muted">{s.description}</p>
            <ol>
              {s.steps.map((step, i) => (
                <li key={i}>{step}</li>
              ))}
            </ol>
            <button onClick={() => initialize(s.id)} disabled={busy === s.id}>
              {busy === s.id ? 'Initializing…' : 'Initialize'}
            </button>
          </Section>
        ))}
      </div>
    </div>
  );
}
