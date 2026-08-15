import { networkApi, inboundApi, outboundApi, transfersApi, accuracyApi } from '../../api';
import { ErrorBox, Section, Stat, useAsync } from '../../components/ui';

export function OverviewPage() {
  const network = useAsync(() => networkApi.summary(), []);
  const inbound = useAsync(() => inboundApi.summary(), []);
  const outbound = useAsync(() => outboundApi.summary(), []);
  const transfers = useAsync(() => transfersApi.summary(), []);
  const accuracy = useAsync(() => accuracyApi.summary(), []);

  return (
    <div>
      <h1>Overview — Control Center</h1>
      <p className="subtitle">Canlı backend agregasyonları (UI kendi toplamını hesaplamaz).</p>

      {network.error && <ErrorBox error={network.error} onRetry={network.refresh} />}

      <div className="grid grid-4">
        <Stat label="Active Warehouses" value={network.data ? `${network.data.activeWarehouses} / ${network.data.totalWarehouses}` : '—'} />
        <Stat label="Total Physical Stock" value={network.data?.physicalStock ?? '—'} accent="#35c46e" />
        <Stat label="Network ATP" value={network.data?.atp ?? '—'} accent="#4d9fff" />
        <Stat label="InTransit" value={transfers.data?.inTransitTotal ?? '—'} accent="#a07ef0" />
      </div>

      <div className="grid grid-4" style={{ marginTop: 12 }}>
        <Stat label="Open Inbound (OPEN/PARTIAL)" value={inbound.data ? inbound.data.openReceipts + inbound.data.partiallyReceivedReceipts : '—'} />
        <Stat label="Pending Putaway Tasks" value={inbound.data?.pendingPutawayTasks ?? '—'} />
        <Stat label="Open Outbound Orders" value={outbound.data ? outbound.data.openOrders + outbound.data.allocatedOrders + outbound.data.pickingOrders : '—'} />
        <Stat label="Pending Pick Tasks" value={outbound.data?.pendingPickTasks ?? '—'} />
      </div>

      <div className="grid grid-4" style={{ marginTop: 12 }}>
        <Stat label="Open Transfers" value={transfers.data?.openTransfers ?? '—'} />
        <Stat label="Open Cycle Counts" value={accuracy.data?.openCycleCounts ?? '—'} />
        <Stat label="Open Reconciliations" value={accuracy.data?.openReconciliations ?? '—'} />
        <Stat label="High-Risk Locations (RED)" value={accuracy.data?.highRiskLocations ?? '—'} accent="#e4574f" />
      </div>

      <Section title="Warehouse Rollup">
        {network.loading && <div className="loading">Loading…</div>}
        {network.data && (
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
                <th>SKUs</th>
              </tr>
            </thead>
            <tbody>
              {network.data.warehouses.map(w => (
                <tr key={w.warehouseId}>
                  <td>
                    <strong>{w.code}</strong> {!w.isOperational && <span className="badge red">INACTIVE</span>}
                  </td>
                  <td>{w.physicalStock}</td>
                  <td style={{ color: 'var(--accent)' }}>{w.atp}</td>
                  <td>{w.allocated}</td>
                  <td>{w.hold}</td>
                  <td>{w.quarantine}</td>
                  <td>{w.damaged}</td>
                  <td>{w.skuCount}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Section>

      <Section title="Recent PickNotFound (24h)">
        <div className="stat" style={{ display: 'inline-block' }}>
          <div className="value" style={{ color: 'var(--orange)' }}>
            {accuracy.data?.recentPickNotFound ?? '—'}
          </div>
          <div className="label">signals</div>
        </div>
      </Section>
    </div>
  );
}
