import { useEffect, useState } from 'react';
import { facilityApi, masterDataApi, networkApi, transfersApi } from '../../api';
import type { TransferDetail, TransferSummary } from '../../api/types';
import { Badge, ErrorBox, Section, Stat, useAsync } from '../../components/ui';
import { newRequestId } from '../../api/http';

const LIFECYCLE = ['CREATED', 'ALLOCATED', 'IN_TRANSIT', 'RECEIVING', 'COMPLETED'];

export function TransfersPage() {
  const transfers = useAsync(() => transfersApi.list(), []);
  const [transferId, setTransferId] = useState<string | null>(null);

  return (
    <div>
      <h1>Transfers</h1>
      <p className="subtitle">A → InTransit → B (network physical invariant canlı izlenir).</p>

      <CreateTransferForm onCreated={transfers.refresh} />
      {transfers.error && <ErrorBox error={transfers.error} onRetry={transfers.refresh} />}

      <Section title="Transfers">
        <table>
          <thead>
            <tr>
              <th>Number</th>
              <th>Status</th>
              <th>InTransit</th>
              <th>Created</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {transfers.data?.map(t => (
              <tr key={t.id}>
                <td>
                  <strong>{t.transferNumber}</strong>
                </td>
                <td>
                  <Badge value={t.status} />
                </td>
                <td style={{ color: 'var(--purple)' }}>{t.inTransitQuantity}</td>
                <td className="muted mono">{new Date(t.createdAt).toLocaleString()}</td>
                <td>
                  <button className="secondary" onClick={() => setTransferId(t.id)}>
                    Detail
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Section>

      {transferId && <TransferDetailPanel transferId={transferId} onChanged={transfers.refresh} />}
    </div>
  );
}

function TransferDetailPanel({ transferId, onChanged }: { transferId: string; onChanged: () => void }) {
  const transfer = useAsync(() => transfersApi.get(transferId), [transferId]);
  const [error, setError] = useState<Error | null>(null);
  const [busy, setBusy] = useState(false);

  // Kısa polling: transfer lifecycle + outbox state backend'den yeniden okunur.
  useEffect(() => {
    const timer = setInterval(() => {
      transfer.refresh();
    }, 4000);
    return () => clearInterval(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [transferId]);

  const action = async (fn: () => Promise<unknown>) => {
    setBusy(true);
    setError(null);
    try {
      await fn();
      onChanged();
      transfer.refresh();
    } catch (e) {
      setError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setBusy(false);
    }
  };

  if (transfer.loading) return <div className="loading">Loading…</div>;
  if (transfer.error) return <ErrorBox error={transfer.error} onRetry={transfer.refresh} />;
  const t = transfer.data;
  if (!t) return null;

  const stageIndex = LIFECYCLE.indexOf(t.status);

  return (
    <Section title={`Transfer ${t.transferNumber}`}>
      <div className="timeline">
        {LIFECYCLE.map((stage, i) => (
          <span key={stage}>
            {i > 0 && <span className="arrow">→</span>}
            <span className={`step ${i < stageIndex || t.status === 'COMPLETED' && i <= stageIndex ? 'done' : ''} ${i === stageIndex ? 'current' : ''}`}>
              {stage}
            </span>
          </span>
        ))}
        {t.status === 'CANCELLED' && <span className="step" style={{ color: 'var(--red)' }}>CANCELLED</span>}
      </div>

      <div className="row">
        {t.status === 'Created' && (
          <button onClick={() => action(() => transfersApi.allocate(transferId))} disabled={busy}>
            Allocate
          </button>
        )}
        {t.status === 'Allocated' && (
          <button onClick={() => action(() => transfersApi.ship(transferId))} disabled={busy}>
            Ship (source shipment)
          </button>
        )}
      </div>

      <TransferStockViz transfer={t} />

      <h2 style={{ marginTop: 14 }}>Lines</h2>
      <table>
        <thead>
          <tr>
            <th>SKU</th>
            <th>Requested</th>
            <th>Shipped</th>
            <th>Received</th>
            <th>Variance</th>
            <th>InTransit</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {t.lines.map(line => (
            <TransferLineRow key={line.id} transfer={t} line={line} busy={busy} onAction={action} />
          ))}
        </tbody>
      </table>

      <h2>Discrepancies</h2>
      {t.discrepancies.length === 0 && <p className="muted">Yok.</p>}
      <table>
        <tbody>
          {t.discrepancies.map(d => (
            <tr key={d.id}>
              <td>
                <Badge value={d.reason} />
              </td>
              <td>{d.quantity}</td>
              <td className="muted mono">{new Date(d.createdAt).toLocaleString()}</td>
            </tr>
          ))}
        </tbody>
      </table>

      {error && <ErrorBox error={error} />}
    </Section>
  );
}

function TransferStockViz({ transfer }: { transfer: TransferDetail }) {
  const summary = useAsync(() => networkApi.summary(), []);
  const warehousesById = new Map((summary.data?.warehouses ?? []).map(w => [w.warehouseId, w]));

  const source = warehousesById.get(transfer.sourceWarehouseId);
  const destination = warehousesById.get(transfer.destinationWarehouseId);

  return (
    <div className="grid grid-4" style={{ marginTop: 12 }}>
      <Stat label={`SOURCE (${source?.code ?? '—'})`} value={source?.physicalStock ?? '—'} />
      <Stat label="IN TRANSIT" value={transfer.inTransitQuantity} accent="#a07ef0" />
      <Stat label={`DESTINATION (${destination?.code ?? '—'})`} value={destination?.physicalStock ?? '—'} />
      <Stat
        label="NETWORK PHYSICAL"
        value={(source?.physicalStock ?? 0) + (destination?.physicalStock ?? 0) + transfer.inTransitQuantity}
        accent="#35c46e"
      />
    </div>
  );
}

function TransferLineRow({
  transfer,
  line,
  busy,
  onAction,
}: {
  transfer: TransferDetail;
  line: TransferDetail['lines'][number];
  busy: boolean;
  onAction: (fn: () => Promise<unknown>) => void;
}) {
  const [quantity, setQuantity] = useState<number>(0);
  const [varianceQty, setVarianceQty] = useState<number>(0);
  const [reason, setReason] = useState('SHORT');
  const [receivingLocationId, setReceivingLocationId] = useState('');
  const locations = useAsync(() => facilityApi.listLocations(transfer.destinationWarehouseId), [transfer.destinationWarehouseId]);
  const receivingLocations = (locations.data ?? []).filter(l => l.holdsInventory && (l.type === 'Receiving' || l.type === 'RECEIVING' || l.type === 'Storage' || l.type === 'STORAGE'));

  return (
    <tr>
      <td className="mono">{line.skuId.slice(0, 8)}…</td>
      <td>{line.requestedQuantity}</td>
      <td>{line.shippedQuantity}</td>
      <td>{line.receivedQuantity}</td>
      <td>{line.confirmedVarianceQuantity}</td>
      <td style={{ color: 'var(--purple)' }}>{line.inTransitQuantity}</td>
      <td>
        {!line.isClosed && (transfer.status === 'InTransit' || transfer.status === 'Receiving') && (
          <div className="row">
            <input type="number" placeholder="qty" value={quantity || ''} onChange={e => setQuantity(Number(e.target.value))} style={{ width: 60 }} />
            <select value={receivingLocationId} onChange={e => setReceivingLocationId(e.target.value)}>
              <option value="">loc</option>
              {receivingLocations.map(l => (
                <option key={l.id} value={l.id}>
                  {l.code}
                </option>
              ))}
            </select>
            <button
              onClick={() =>
                onAction(() =>
                  transfersApi.receive(transfer.id, {
                    requestId: newRequestId(),
                    transferLineId: line.id,
                    quantity,
                    receivingLocationId,
                    receivingStatus: 'AVAILABLE',
                  }),
                )
              }
              disabled={busy || quantity <= 0 || !receivingLocationId}
            >
              Receive
            </button>
            <span className="muted">|</span>
            <input type="number" placeholder="var" value={varianceQty || ''} onChange={e => setVarianceQty(Number(e.target.value))} style={{ width: 60 }} />
            <select value={reason} onChange={e => setReason(e.target.value)}>
              {['SHORT', 'DAMAGED_IN_TRANSIT', 'LOST', 'OTHER'].map(r => (
                <option key={r} value={r}>
                  {r}
                </option>
              ))}
            </select>
            <button
              className="secondary"
              onClick={() =>
                onAction(() =>
                  transfersApi.confirmVariance(transfer.id, {
                    requestId: newRequestId(),
                    transferLineId: line.id,
                    quantity: varianceQty,
                    reason,
                  }),
                )
              }
              disabled={busy || varianceQty <= 0}
            >
              Confirm Variance
            </button>
          </div>
        )}
      </td>
    </tr>
  );
}

function CreateTransferForm({ onCreated }: { onCreated: () => void }) {
  const warehouses = useAsync(() => facilityApi.listWarehouses(), []);
  const skus = useAsync(() => masterDataApi.listSkus(), []);
  const [sourceId, setSourceId] = useState('');
  const [destinationId, setDestinationId] = useState('');
  const [skuId, setSkuId] = useState('');
  const [quantity, setQuantity] = useState(10);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error | null>(null);
  const [created, setCreated] = useState<string | null>(null);

  const create = async () => {
    setBusy(true);
    setError(null);
    try {
      const result = await transfersApi.create({
        requestId: newRequestId(),
        sourceWarehouseId: sourceId,
        destinationWarehouseId: destinationId,
        externalReference: 'DEMO-REPL',
        lines: [{ skuId, requestedQuantity: quantity }],
      });
      setCreated(result.transferNumber);
      onCreated();
    } catch (e) {
      setError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Section title="Create Transfer">
      <div className="row">
        <div>
          <label htmlFor="ct-src">Source Warehouse</label>
          <select id="ct-src" value={sourceId} onChange={e => setSourceId(e.target.value)}>
            <option value="">— seç —</option>
            {warehouses.data?.map(w => (
              <option key={w.id} value={w.id}>
                {w.code}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="ct-dst">Destination Warehouse</label>
          <select id="ct-dst" value={destinationId} onChange={e => setDestinationId(e.target.value)}>
            <option value="">— seç —</option>
            {warehouses.data?.map(w => (
              <option key={w.id} value={w.id}>
                {w.code}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="ct-sku">SKU</label>
          <select id="ct-sku" value={skuId} onChange={e => setSkuId(e.target.value)}>
            <option value="">— seç —</option>
            {skus.data?.slice(0, 200).map(s => (
              <option key={s.id} value={s.id}>
                {s.code}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="ct-qty">Quantity</label>
          <input id="ct-qty" type="number" min={1} value={quantity} onChange={e => setQuantity(Number(e.target.value))} />
        </div>
        <button onClick={create} disabled={busy || !sourceId || !destinationId || !skuId || quantity <= 0}>
          Create Transfer
        </button>
      </div>
      {created && <div className="explanation">Transfer oluşturuldu: {created}</div>}
      {error && <ErrorBox error={error} />}
    </Section>
  );
}
