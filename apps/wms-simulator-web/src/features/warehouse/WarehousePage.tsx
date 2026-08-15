import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { facilityApi, networkApi, inboundApi, outboundApi, transfersApi } from '../../api';
import type { LocationTreeNode, WarehouseInfo } from '../../api/types';
import { ErrorBox, Section, Stat, useAsync } from '../../components/ui';

function LocationTree({ nodes, depth = 0 }: { nodes: LocationTreeNode[]; depth?: number }) {
  return (
    <ul style={{ listStyle: 'none', paddingLeft: depth === 0 ? 0 : 16 }}>
      {nodes.map(node => (
        <li key={node.id} style={{ margin: '3px 0' }}>
          <span className="mono">
            {node.code} <span className="muted">({node.type})</span>{' '}
            {!node.isActive && <span className="badge red">INACTIVE</span>}
          </span>
          {node.children.length > 0 && <LocationTree nodes={node.children} depth={depth + 1} />}
        </li>
      ))}
    </ul>
  );
}

export function WarehousePage() {
  const { id } = useParams();
  const warehouses = useAsync(() => facilityApi.listWarehouses(), []);
  const [selectedId, setSelectedId] = useState<string | undefined>(id);

  const selected = (warehouses.data ?? []).find(w => w.id === selectedId) ?? warehouses.data?.[0];

  return (
    <div>
      <h1>Warehouses</h1>
      <p className="subtitle">Seçilen warehouse'ın lokasyon ağacı ParentLocationId ilişkisiyle üretilir.</p>

      {warehouses.error && <ErrorBox error={warehouses.error} onRetry={warehouses.refresh} />}

      <div className="row" style={{ marginBottom: 14 }}>
        <select value={selected?.id ?? ''} onChange={e => setSelectedId(e.target.value)}>
          {warehouses.data?.map(w => (
            <option key={w.id} value={w.id}>
              {w.code} — {w.name}
            </option>
          ))}
        </select>
      </div>

      {selected && <WarehouseDetail warehouse={selected} />}
    </div>
  );
}

function WarehouseDetail({ warehouse }: { warehouse: WarehouseInfo }) {
  const tree = useAsync(() => facilityApi.locationTree(warehouse.id), [warehouse.id]);
  const summary = useAsync(() => networkApi.summary(), []);
  const inbound = useAsync(() => inboundApi.listReceipts(warehouse.id), [warehouse.id]);
  const outbound = useAsync(() => outboundApi.listOrders(warehouse.id), [warehouse.id]);
  const transfers = useAsync(() => transfersApi.list(warehouse.id), [warehouse.id]);

  const rollup = summary.data?.warehouses.find(w => w.warehouseId === warehouse.id);

  return (
    <div>
      <div className="grid grid-4">
        <Stat label="Physical Stock" value={rollup?.physicalStock ?? '—'} />
        <Stat label="ATP" value={rollup?.atp ?? '—'} />
        <Stat label="Hold / Quarantine / Damaged" value={`${rollup?.hold ?? 0} / ${rollup?.quarantine ?? 0} / ${rollup?.damaged ?? 0}`} />
        <Stat label="SKU Count" value={rollup?.skuCount ?? '—'} />
      </div>

      <div className="grid grid-2" style={{ marginTop: 12 }}>
        <Section title="Location Tree (generic hierarchy)">
          {tree.loading && <div className="loading">Loading…</div>}
          {tree.error && <ErrorBox error={tree.error} onRetry={tree.refresh} />}
          {tree.data && <LocationTree nodes={tree.data} />}
        </Section>

        <Section title="Activity">
          <table>
            <tbody>
              <tr>
                <td>Inbound receipts</td>
                <td>{inbound.data?.length ?? '—'}</td>
              </tr>
              <tr>
                <td>Outbound orders</td>
                <td>{outbound.data?.length ?? '—'}</td>
              </tr>
              <tr>
                <td>Transfers (source/destination)</td>
                <td>{transfers.data?.length ?? '—'}</td>
              </tr>
              <tr>
                <td>Operational</td>
                <td>{warehouse.isActive ? <span className="badge green">ACTIVE</span> : <span className="badge red">INACTIVE</span>}</td>
              </tr>
            </tbody>
          </table>
        </Section>
      </div>
    </div>
  );
}
