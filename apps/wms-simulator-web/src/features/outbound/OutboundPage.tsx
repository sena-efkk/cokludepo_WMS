import { useState } from 'react';
import { facilityApi, masterDataApi, outboundApi } from '../../api';
import type { OrderDetail, OrderSummary } from '../../api/types';
import { Badge, ErrorBox, Section, useAsync } from '../../components/ui';
import { newRequestId } from '../../api/http';

export function OutboundPage() {
  const orders = useAsync(() => outboundApi.listOrders(), []);
  const [orderId, setOrderId] = useState<string | null>(null);

  return (
    <div>
      <h1>Outbound</h1>
      <p className="subtitle">Order → Allocation → Pick → Pack → Ship (gerçek workflow).</p>

      <CreateOrderForm onCreated={orders.refresh} />
      {orders.error && <ErrorBox error={orders.error} onRetry={orders.refresh} />}

      <Section title="Fulfillment Orders">
        <table>
          <thead>
            <tr>
              <th>Order</th>
              <th>Status</th>
              <th>Requested</th>
              <th>Created</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {orders.data?.map(o => (
              <tr key={o.id}>
                <td>
                  <strong>{o.orderNumber}</strong>
                  {o.externalOrderReference && <div className="muted">ref: {o.externalOrderReference}</div>}
                </td>
                <td>
                  <Badge value={o.status} />
                </td>
                <td>{o.totalRequested}</td>
                <td className="muted mono">{new Date(o.createdAt).toLocaleString()}</td>
                <td>
                  <button className="secondary" onClick={() => setOrderId(o.id)}>
                    Detail
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Section>

      {orderId && <OrderDetailPanel orderId={orderId} onChanged={orders.refresh} />}
    </div>
  );
}

function OrderDetailPanel({ orderId, onChanged }: { orderId: string; onChanged: () => void }) {
  const order = useAsync(() => outboundApi.getOrder(orderId), [orderId]);
  const [error, setError] = useState<Error | null>(null);
  const [busy, setBusy] = useState(false);

  const action = async (fn: () => Promise<unknown>) => {
    setBusy(true);
    setError(null);
    try {
      await fn();
      onChanged();
      order.refresh();
    } catch (e) {
      setError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setBusy(false);
    }
  };

  if (order.loading) return <div className="loading">Loading…</div>;
  if (order.error) return <ErrorBox error={order.error} onRetry={order.refresh} />;
  const o = order.data;
  if (!o) return null;

  return (
    <Section title={`Order ${o.orderNumber}`}>
      <div className="row" style={{ marginBottom: 10 }}>
        <Badge value={o.status} />
        {o.status === 'Created' && (
          <button onClick={() => action(() => outboundApi.allocate(orderId))} disabled={busy}>
            Allocate
          </button>
        )}
        {o.status === 'Picked' && (
          <button onClick={() => action(() => outboundApi.pack(orderId))} disabled={busy}>
            Pack
          </button>
        )}
        {o.status === 'Packed' && (
          <button onClick={() => action(() => outboundApi.ship(orderId))} disabled={busy}>
            Ship
          </button>
        )}
      </div>

      <table>
        <thead>
          <tr>
            <th>SKU</th>
            <th>Requested</th>
            <th>Reservation</th>
          </tr>
        </thead>
        <tbody>
          {o.lines.map(line => (
            <tr key={line.id}>
              <td className="mono">{line.skuId.slice(0, 8)}…</td>
              <td>{line.requestedQuantity}</td>
              <td className="mono">{line.reservationId ? `${line.reservationId.slice(0, 8)}…` : '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <h2 style={{ marginTop: 16 }}>Pick Tasks</h2>
      <table>
        <thead>
          <tr>
            <th>Task</th>
            <th>Location</th>
            <th>Required</th>
            <th>Picked</th>
            <th>Status</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {o.pickTasks.map(task => (
            <PickRow key={task.id} orderId={orderId} task={task} onChanged={() => action(() => Promise.resolve())} />
          ))}
        </tbody>
      </table>

      <div className="grid grid-2" style={{ marginTop: 16 }}>
        <div className="stat">
          <div className="value">{o.package?.packageNumber ?? '—'}</div>
          <div className="label">Package</div>
        </div>
        <div className="stat">
          <div className="value">
            {o.shipment ? `${o.shipment.shipmentNumber} (${o.shipment.status})` : '—'}
          </div>
          <div className="label">Shipment</div>
        </div>
      </div>
      {error && <ErrorBox error={error} />}
    </Section>
  );
}

function PickRow({ task, onChanged }: { orderId: string; task: OrderDetail['pickTasks'][number]; onChanged: () => void }) {
  const [locationScan, setLocationScan] = useState('');
  const [skuScan, setSkuScan] = useState('');
  const [quantity, setQuantity] = useState<number>(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error | null>(null);

  const pick = async () => {
    setBusy(true);
    setError(null);
    try {
      await outboundApi.confirmPick(task.id, { locationScan, skuScan, quantity });
      onChanged();
    } catch (e) {
      setError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setBusy(false);
    }
  };

  const notFound = async () => {
    setBusy(true);
    setError(null);
    try {
      await outboundApi.notFoundPick(task.id, { requestId: newRequestId() });
      onChanged();
    } catch (e) {
      setError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setBusy(false);
    }
  };

  return (
    <tr>
      <td className="mono">{task.id.slice(0, 8)}…</td>
      <td className="mono">{task.locationId.slice(0, 8)}…</td>
      <td>{task.requiredQuantity}</td>
      <td>{task.pickedQuantity}</td>
      <td>
        <Badge value={task.status} />
      </td>
      <td>
        {task.status === 'Pending' || task.status === 'InProgress' ? (
          <>
            <input placeholder="location scan" value={locationScan} onChange={e => setLocationScan(e.target.value)} style={{ width: 120, marginRight: 4 }} />
            <input placeholder="barcode" value={skuScan} onChange={e => setSkuScan(e.target.value)} style={{ width: 110, marginRight: 4 }} />
            <input
              type="number"
              placeholder="qty"
              value={quantity || ''}
              onChange={e => setQuantity(Number(e.target.value))}
              style={{ width: 60, marginRight: 4 }}
            />
            <button onClick={pick} disabled={busy} style={{ marginRight: 4 }}>
              Pick
            </button>
            <button className="danger" onClick={notFound} disabled={busy}>
              Not Found
            </button>
            {error && <ErrorBox error={error} />}
          </>
        ) : null}
      </td>
    </tr>
  );
}

function CreateOrderForm({ onCreated }: { onCreated: () => void }) {
  const warehouses = useAsync(() => facilityApi.listWarehouses(), []);
  const skus = useAsync(() => masterDataApi.listSkus(), []);
  const [warehouseId, setWarehouseId] = useState('');
  const [skuId, setSkuId] = useState('');
  const [quantity, setQuantity] = useState(3);
  const [externalRef, setExternalRef] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error | null>(null);
  const [created, setCreated] = useState<string | null>(null);

  const create = async () => {
    setBusy(true);
    setError(null);
    try {
      const result = await outboundApi.createOrder({
        requestId: newRequestId(),
        warehouseId,
        externalOrderReference: externalRef || undefined,
        lines: [{ skuId, requestedQuantity: quantity }],
      });
      setCreated(result.orderNumber);
      onCreated();
    } catch (e) {
      setError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Section title="Create Order">
      <div className="row">
        <div>
          <label htmlFor="co-wh">Warehouse</label>
          <select id="co-wh" value={warehouseId} onChange={e => setWarehouseId(e.target.value)}>
            <option value="">— seç —</option>
            {warehouses.data?.map(w => (
              <option key={w.id} value={w.id}>
                {w.code}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="co-sku">SKU</label>
          <select id="co-sku" value={skuId} onChange={e => setSkuId(e.target.value)}>
            <option value="">— seç —</option>
            {skus.data?.slice(0, 200).map(s => (
              <option key={s.id} value={s.id}>
                {s.code}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="co-qty">Quantity</label>
          <input id="co-qty" type="number" min={1} value={quantity} onChange={e => setQuantity(Number(e.target.value))} />
        </div>
        <div>
          <label htmlFor="co-ref">External Reference</label>
          <input id="co-ref" value={externalRef} onChange={e => setExternalRef(e.target.value)} placeholder="OMS-001" />
        </div>
        <button onClick={create} disabled={busy || !warehouseId || !skuId || quantity <= 0}>
          Create Order
        </button>
      </div>
      {created && <div className="explanation">Order oluşturuldu: {created}</div>}
      {error && <ErrorBox error={error} />}
    </Section>
  );
}
