import { useState } from 'react';
import { facilityApi, inventoryApi, networkApi, masterDataApi } from '../../api';
import type { LedgerEntry, SkuWithBarcodes } from '../../api/types';
import { Badge, ErrorBox, Section, Stat, useAsync } from '../../components/ui';

export function InventoryPage() {
  const [query, setQuery] = useState('');
  const [sku, setSku] = useState<SkuWithBarcodes | null>(null);
  const [error, setError] = useState<Error | null>(null);
  const [loading, setLoading] = useState(false);

  const search = async () => {
    setLoading(true);
    setError(null);
    try {
      const term = query.trim();
      if (!term) return;
      const byBarcode = await networkApi.skuByBarcode(term);
      if (byBarcode) {
        setSku(byBarcode);
        return;
      }
      const skus = await masterDataApi.listSkus();
      const match = skus.find(s => s.code.toLowerCase() === term.toLowerCase() || s.id === term);
      setSku(match ?? null);
      if (!match) setError(new Error(`SKU bulunamadı: ${term}`));
    } catch (e) {
      setError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div>
      <h1>Inventory Explorer</h1>
      <p className="subtitle">SKU kodu veya barcode ile arayın — location-level balance'lar backend'den gelir.</p>

      <div className="row">
        <div>
          <label htmlFor="sku-search">SKU Code / Barcode</label>
          <input
            id="sku-search"
            value={query}
            onChange={e => setQuery(e.target.value)}
            onKeyDown={e => e.key === 'Enter' && search()}
            placeholder="SKU-000124 veya barcode"
            style={{ minWidth: 280 }}
          />
        </div>
        <button onClick={search} disabled={loading}>
          Search
        </button>
      </div>

      {error && <ErrorBox error={error} />}
      {sku && <SkuExplorer sku={sku} />}
    </div>
  );
}

function SkuExplorer({ sku }: { sku: SkuWithBarcodes }) {
  const network = useAsync(() => networkApi.sku(sku.id), [sku.id]);

  return (
    <div style={{ marginTop: 16 }}>
      <h2>
        {sku.code} <span className="muted mono">({sku.id.slice(0, 8)}…)</span>{' '}
        {!sku.isActive && <span className="badge red">INACTIVE</span>}
      </h2>

      {network.error && <ErrorBox error={network.error} onRetry={network.refresh} />}

      <div className="grid grid-4">
        <Stat label="Network Physical" value={network.data?.networkPhysicalStock ?? '—'} accent="#35c46e" />
        <Stat label="Network ATP" value={network.data?.networkAtp ?? '—'} accent="#4d9fff" />
        <Stat label="Allocated" value={network.data?.networkAllocated ?? '—'} />
        <Stat label="InTransit" value={0} />
      </div>

      <Section title="Location-level balances (status bölmeli)">
        <table>
          <thead>
            <tr>
              <th>Warehouse</th>
              <th>Physical</th>
              <th>ATP</th>
              <th>Allocated</th>
              <th>Hold</th>
              <th>Quarantine</th>
              <th>Damaged</th>
              <th>Risk</th>
            </tr>
          </thead>
          <tbody>
            {network.data?.warehouses.map(w => (
              <tr key={w.warehouseId}>
                <td>
                  <strong>{w.warehouseCode}</strong>
                  {!w.isOperational && <span className="badge red">INACTIVE</span>}
                </td>
                <td>{w.physicalStock}</td>
                <td style={{ color: 'var(--accent)' }}>{w.atp}</td>
                <td>{w.allocated}</td>
                <td>{w.hold}</td>
                <td>{w.quarantine}</td>
                <td>{w.damaged}</td>
                <td>
                  <Badge value={w.riskLevel} />
                  {w.recentNotFoundCount ? <span className="muted"> ({w.recentNotFoundCount} notfound)</span> : null}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        <p className="muted" style={{ marginTop: 8 }}>
          ATP yalnız AVAILABLE − allocated üzerinden hesaplanır; HOLD/QUARANTINE/DAMAGED ATP'ye girmez.
        </p>
      </Section>

      <LocationBalances skuId={sku.id} />
      <LedgerSection skuId={sku.id} />
    </div>
  );
}

function LocationBalances({ skuId }: { skuId: string }) {
  const warehouses = useAsync(() => facilityApi.listWarehouses(), []);
  const [selectedWh, setSelectedWh] = useState<string>('');

  const whId = selectedWh || warehouses.data?.[0]?.id;
  const balances = useAsync(() => (whId ? inventoryApi.balances(whId, skuId) : Promise.resolve([])), [whId, skuId]);

  return (
    <Section title="Location detail (warehouse drill-down)">
      <div className="row" style={{ marginBottom: 8 }}>
        <select value={whId} onChange={e => setSelectedWh(e.target.value)}>
          {warehouses.data?.map(w => (
            <option key={w.id} value={w.id}>
              {w.code}
            </option>
          ))}
        </select>
      </div>
      {balances.error && <ErrorBox error={balances.error} />}
      <table>
        <thead>
          <tr>
            <th>Location</th>
            <th>Status</th>
            <th>Qty</th>
            <th>Allocated</th>
            <th>ATP</th>
          </tr>
        </thead>
        <tbody>
          {balances.data?.filter(b => b.quantity > 0 || b.allocated > 0).map(b => (
            <tr key={`${b.locationId}-${b.status}`}>
              <td className="mono">{b.locationId.slice(0, 8)}…</td>
              <td>
                <Badge value={b.status} />
              </td>
              <td>{b.quantity}</td>
              <td>{b.allocated}</td>
              <td style={{ color: 'var(--accent)' }}>{b.status === 'AVAILABLE' ? b.available : '—'}</td>
            </tr>
          ))}
          {balances.data?.filter(b => b.quantity > 0 || b.allocated > 0).length === 0 && (
            <tr>
              <td colSpan={5} className="muted">
                Bu warehouse'da bu SKU için stok yok.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </Section>
  );
}

function LedgerSection({ skuId }: { skuId: string }) {
  const ledger = useAsync(() => inventoryApi.ledger({ skuId, limit: 200 }), [skuId]);

  return (
    <Section title="Ledger (read-only history)">
      {ledger.error && <ErrorBox error={ledger.error} />}
      <table>
        <thead>
          <tr>
            <th>Time</th>
            <th>Warehouse</th>
            <th>Entry</th>
            <th>Qty Δ</th>
            <th>Alloc Δ</th>
            <th>Status</th>
          </tr>
        </thead>
        <tbody>
          {ledger.data?.map(entry => (
            <LedgerRow key={entry.id} entry={entry} />
          ))}
        </tbody>
      </table>
      <p className="muted">Ledger read-only'dur — UI'dan düzenlenemez.</p>
    </Section>
  );
}

function LedgerRow({ entry }: { entry: LedgerEntry }) {
  const color = entry.quantityDelta > 0 ? 'var(--green)' : entry.quantityDelta < 0 ? 'var(--red)' : 'var(--muted)';
  return (
    <tr>
      <td className="mono muted">{new Date(entry.occurredAt).toLocaleString()}</td>
      <td className="mono">{entry.warehouseId.slice(0, 8)}…</td>
      <td>
        <Badge value={entry.entryType} />
      </td>
      <td style={{ color }}>{entry.quantityDelta > 0 ? '+' : ''}{entry.quantityDelta}</td>
      <td>{entry.allocatedDelta}</td>
      <td>
        <Badge value={entry.status} />
      </td>
    </tr>
  );
}
