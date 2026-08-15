import { useState } from 'react';
import { facilityApi, inboundApi, masterDataApi } from '../../api';
import type { PutawayTask, ReceiptSummary, SkuWithBarcodes, WarehouseInfo } from '../../api/types';
import { Badge, ErrorBox, Section, useAsync } from '../../components/ui';
import { newRequestId } from '../../api/http';

export function InboundPage() {
  const receipts = useAsync(() => inboundApi.listReceipts(), []);
  const tasks = useAsync(() => inboundApi.listPutawayTasks(), []);
  const [receiptId, setReceiptId] = useState<string | null>(null);
  const [tab, setTab] = useState<'receipts' | 'putaway'>('receipts');

  return (
    <div>
      <h1>Inbound</h1>
      <p className="subtitle">Receipt → Receive → Putaway (gerçek Inbound workflow).</p>

      <div className="row" style={{ marginBottom: 10 }}>
        <button className={tab === 'receipts' ? '' : 'secondary'} onClick={() => setTab('receipts')}>
          Receipts
        </button>
        <button className={tab === 'putaway' ? '' : 'secondary'} onClick={() => setTab('putaway')}>
          Putaway Tasks
        </button>
      </div>

      {tab === 'receipts' ? (
        <>
          <CreateReceiptForm onCreated={receipts.refresh} />
          {receipts.error && <ErrorBox error={receipts.error} onRetry={receipts.refresh} />}
          <Section title="Receipts">
            <table>
              <thead>
                <tr>
                  <th>Number</th>
                  <th>Status</th>
                  <th>Received</th>
                  <th>Created</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {receipts.data?.map(r => (
                  <tr key={r.id}>
                    <td>
                      <strong>{r.receiptNumber}</strong>
                      {r.externalReference && <div className="muted">ref: {r.externalReference}</div>}
                    </td>
                    <td>
                      <Badge value={r.status} />
                    </td>
                    <td>
                      {r.totalReceived} / {r.totalExpected}
                    </td>
                    <td className="muted mono">{new Date(r.createdAt).toLocaleString()}</td>
                    <td>
                      <button className="secondary" onClick={() => setReceiptId(r.id)}>
                        Detail
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Section>
          {receiptId && <ReceiptDetail receiptId={receiptId} onChanged={receipts.refresh} />}
        </>
      ) : (
        <Section title="Putaway Tasks">
          {tasks.error && <ErrorBox error={tasks.error} onRetry={tasks.refresh} />}
          <table>
            <thead>
              <tr>
                <th>Task</th>
                <th>SKU</th>
                <th>Qty</th>
                <th>Inventory Status</th>
                <th>Status</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {tasks.data?.map(t => (
                <PutawayRow key={t.id} task={t} onDone={tasks.refresh} />
              ))}
            </tbody>
          </table>
        </Section>
      )}
    </div>
  );
}

function PutawayRow({ task, onDone }: { task: PutawayTask; onDone: () => void }) {
  const [sourceScan, setSourceScan] = useState('');
  const [skuScan, setSkuScan] = useState('');
  const [destinationScan, setDestinationScan] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error | null>(null);
  const [open, setOpen] = useState(false);

  const execute = async () => {
    setBusy(true);
    setError(null);
    try {
      await inboundApi.completePutaway(task.id, {
        requestId: newRequestId(),
        sourceScan,
        skuScan,
        destinationScan,
        quantity: task.quantity,
        deviceId: 'browser-scan-sim',
        operatorId: 'demo',
      });
      onDone();
    } catch (e) {
      setError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setBusy(false);
    }
  };

  if (!open) {
    return (
      <tr>
        <td className="mono">{task.id.slice(0, 8)}…</td>
        <td className="mono">{task.skuId.slice(0, 8)}…</td>
        <td>{task.quantity}</td>
        <td>
          <Badge value={task.inventoryStatus} />
        </td>
        <td>
          <Badge value={task.status} />
        </td>
        <td>
          <button className="secondary" onClick={() => setOpen(true)} disabled={task.status !== 'Pending'}>
            {task.status === 'Pending' ? 'Scan Putaway' : '—'}
          </button>
        </td>
      </tr>
    );
  }

  return (
    <tr>
      <td colSpan={6}>
        <div className="scan-form">
          <strong>Scan Simulation — RF cihazı yerine gerçek scan request üretir</strong>
          <label htmlFor={`src-${task.id}`}>SOURCE LOCATION</label>
          <input id={`src-${task.id}`} value={sourceScan} onChange={e => setSourceScan(e.target.value)} placeholder="RECEIVING-01" />
          <label htmlFor={`sku-${task.id}`}>SKU / BARCODE</label>
          <input id={`sku-${task.id}`} value={skuScan} onChange={e => setSkuScan(e.target.value)} placeholder="869…" />
          <label htmlFor={`dst-${task.id}`}>DESTINATION</label>
          <input id={`dst-${task.id}`} value={destinationScan} onChange={e => setDestinationScan(e.target.value)} placeholder="A01-B03" />
          <div className="muted">QUANTITY: {task.quantity}</div>
          <div className="row" style={{ marginTop: 8 }}>
            <button onClick={execute} disabled={busy}>
              Execute Scan Movement
            </button>
            <button className="secondary" onClick={() => setOpen(false)}>
              Cancel
            </button>
          </div>
          {error && <ErrorBox error={error} />}
        </div>
      </td>
    </tr>
  );
}

function ReceiptDetail({ receiptId, onChanged }: { receiptId: string; onChanged: () => void }) {
  const receipt = useAsync(() => inboundApi.getReceipt(receiptId), [receiptId]);
  const warehouses = useAsync(() => facilityApi.listWarehouses(), []);
  const [selectedLine, setSelectedLine] = useState<string>('');
  const [quantity, setQuantity] = useState<number>(0);
  const [status, setStatus] = useState('AVAILABLE');
  const [receivingLocationId, setReceivingLocationId] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error | null>(null);

  const warehousesById = new Map((warehouses.data ?? []).map(w => [w.id, w]));
  const locations = useAsync(
    () => (receipt.data ? facilityApi.listLocations(receipt.data.warehouseId) : Promise.resolve([])),
    [receipt.data?.warehouseId],
  );
  const receivingLocations = (locations.data ?? []).filter(l => l.type === 'RECEIVING' || l.type === 'Receiving' || l.type === 'STAGING');

  const receive = async () => {
    setBusy(true);
    setError(null);
    try {
      await inboundApi.receive(receiptId, {
        requestId: newRequestId(),
        receiptLineId: selectedLine,
        quantity,
        receivingLocationId,
        receivingStatus: status,
      });
      onChanged();
      receipt.refresh();
    } catch (e) {
      setError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setBusy(false);
    }
  };

  if (receipt.loading) return <div className="loading">Loading…</div>;
  if (receipt.error) return <ErrorBox error={receipt.error} onRetry={receipt.refresh} />;
  if (!receipt.data) return null;

  const warehouse = warehousesById.get(receipt.data.warehouseId);
  const selectedLineObj = receipt.data.lines.find(l => l.id === selectedLine);

  return (
    <Section title={`Receipt ${receipt.data.receiptNumber}`}>
      <div className="grid grid-4">
        <div className="stat">
          <div className="value">{warehouse?.code ?? '—'}</div>
          <div className="label">Warehouse</div>
        </div>
        <div className="stat">
          <div className="value">
            <Badge value={receipt.data.status} />
          </div>
          <div className="label">Status</div>
        </div>
        <div className="stat">
          <div className="value">
            {receipt.data.lines.reduce((a, l) => a + l.receivedQuantity, 0)} / {receipt.data.lines.reduce((a, l) => a + l.expectedQuantity, 0)}
          </div>
          <div className="label">Received / Expected</div>
        </div>
      </div>

      <table>
        <thead>
          <tr>
            <th>SKU</th>
            <th>Expected</th>
            <th>Received</th>
            <th>Disposition</th>
          </tr>
        </thead>
        <tbody>
          {receipt.data.lines.map(line => (
            <tr key={line.id} style={{ cursor: 'pointer', background: selectedLine === line.id ? 'rgba(77,159,255,0.08)' : undefined }} onClick={() => setSelectedLine(line.id)}>
              <td className="mono">{line.skuId.slice(0, 8)}…</td>
              <td>{line.expectedQuantity}</td>
              <td>{line.receivedQuantity}</td>
              <td>
                <Badge value={line.disposition} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      <div className="row" style={{ marginTop: 12 }}>
        <div>
          <label htmlFor="recv-loc">Receiving Location</label>
          <select id="recv-loc" value={receivingLocationId} onChange={e => setReceivingLocationId(e.target.value)}>
            <option value="">— seç —</option>
            {receivingLocations.map(l => (
              <option key={l.id} value={l.id}>
                {l.code}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="recv-qty">Quantity</label>
          <input id="recv-qty" type="number" min={1} value={quantity} onChange={e => setQuantity(Number(e.target.value))} />
        </div>
        <div>
          <label htmlFor="recv-status">Receiving Status</label>
          <select id="recv-status" value={status} onChange={e => setStatus(e.target.value)}>
            {['AVAILABLE', 'HOLD', 'QUARANTINE', 'DAMAGED'].map(s => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
        </div>
        <button onClick={receive} disabled={busy || !selectedLine || quantity <= 0 || !receivingLocationId}>
          Receive
        </button>
      </div>

      <div className="muted" style={{ marginTop: 8 }}>
        {selectedLineObj
          ? `Seçili line: expected ${selectedLineObj.expectedQuantity}, received ${selectedLineObj.receivedQuantity} — partial/short/over backend tarafından belirlenir.`
          : 'Receive için bir line seçin.'}
      </div>
      {error && <ErrorBox error={error} />}
    </Section>
  );
}

function CreateReceiptForm({ onCreated }: { onCreated: () => void }) {
  const warehouses = useAsync(() => facilityApi.listWarehouses(), []);
  const skus = useAsync(() => masterDataApi.listSkus(), []);
  const [warehouseId, setWarehouseId] = useState('');
  const [skuId, setSkuId] = useState('');
  const [expected, setExpected] = useState(10);
  const [externalRef, setExternalRef] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error | null>(null);
  const [created, setCreated] = useState<string | null>(null);

  const create = async () => {
    setBusy(true);
    setError(null);
    try {
      const result = await inboundApi.createReceipt({
        requestId: newRequestId(),
        warehouseId,
        externalReference: externalRef || undefined,
        sourceType: 'ASN',
        lines: [{ skuId, expectedQuantity: expected }],
      });
      setCreated(result.receiptNumber);
      onCreated();
    } catch (e) {
      setError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Section title="Create Receipt">
      <div className="row">
        <div>
          <label htmlFor="cr-wh">Warehouse</label>
          <select id="cr-wh" value={warehouseId} onChange={e => setWarehouseId(e.target.value)}>
            <option value="">— seç —</option>
            {warehouses.data?.map(w => (
              <option key={w.id} value={w.id}>
                {w.code}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="cr-sku">SKU</label>
          <select id="cr-sku" value={skuId} onChange={e => setSkuId(e.target.value)}>
            <option value="">— seç —</option>
            {skus.data?.slice(0, 200).map(s => (
              <option key={s.id} value={s.id}>
                {s.code}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="cr-exp">Expected Quantity</label>
          <input id="cr-exp" type="number" min={1} value={expected} onChange={e => setExpected(Number(e.target.value))} />
        </div>
        <div>
          <label htmlFor="cr-ref">External Reference</label>
          <input id="cr-ref" value={externalRef} onChange={e => setExternalRef(e.target.value)} placeholder="ASN-123" />
        </div>
        <button onClick={create} disabled={busy || !warehouseId || !skuId || expected <= 0}>
          Create Receipt
        </button>
      </div>
      {created && <div className="explanation">Receipt oluşturuldu: {created}</div>}
      {error && <ErrorBox error={error} />}
    </Section>
  );
}
