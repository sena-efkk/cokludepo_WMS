import { useEffect, useRef, useState } from 'react';
import L from 'leaflet';
import 'leaflet/dist/leaflet.css';
import { facilityApi, networkApi } from '../../api';
import type { NetworkWarehouseSummary, WarehouseInfo } from '../../api/types';
import { ErrorBox, useAsync } from '../../components/ui';

function WarehouseMap({ markers }: { markers: { warehouse: WarehouseInfo; summary?: NetworkWarehouseSummary }[] }) {
  const mapRef = useRef<L.Map | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!containerRef.current || mapRef.current) return;
    const map = L.map(containerRef.current).setView([40.0, 31.0], 6);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '© OpenStreetMap',
    }).addTo(map);
    mapRef.current = map;
    return () => {
      map.remove();
      mapRef.current = null;
    };
  }, []);

  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    map.eachLayer(layer => {
      if ((layer as L.Marker).getLatLng) map.removeLayer(layer);
    });

    const visible: [number, number][] = [];
    for (const { warehouse, summary } of markers) {
      if (warehouse.latitude == null || warehouse.longitude == null) continue;
      const latlng: [number, number] = [warehouse.latitude, warehouse.longitude];
      visible.push(latlng);
      const popup = `
        <strong>${warehouse.code}</strong><br/>
        ${warehouse.name}<br/>
        Physical: ${summary?.physicalStock ?? '—'} · ATP: ${summary?.atp ?? '—'}<br/>
        ${warehouse.isActive ? 'Operational' : '<b style="color:#e4574f">INACTIVE</b>'}
      `;
      L.marker(latlng)
        .addTo(map)
        .bindPopup(popup);
    }

    if (visible.length > 0) {
      map.fitBounds(visible, { padding: [40, 40] });
    }
  }, [markers]);

  return <div ref={containerRef} className="map" />;
}

export function NetworkPage() {
  const warehouses = useAsync(() => facilityApi.listWarehouses(), []);
  const summary = useAsync(() => networkApi.summary(), []);
  const [selected, setSelected] = useState<string | null>(null);

  const markers = (warehouses.data ?? []).map(warehouse => ({
    warehouse,
    summary: summary.data?.warehouses.find(w => w.warehouseId === warehouse.id),
  }));

  const unavailable = markers.filter(m => m.warehouse.latitude == null || m.warehouse.longitude == null);

  return (
    <div>
      <h1>Network</h1>
      <p className="subtitle">Warehouse koordinatları Facility'den gelir; eksik koordinat açıkça işaretlenir.</p>

      {warehouses.error && <ErrorBox error={warehouses.error} onRetry={warehouses.refresh} />}

      <WarehouseMap markers={markers} />

      <div style={{ marginTop: 14 }}>
        <table>
          <thead>
            <tr>
              <th>Warehouse</th>
              <th>City</th>
              <th>Physical</th>
              <th>ATP</th>
              <th>Status</th>
              <th>Location</th>
            </tr>
          </thead>
          <tbody>
            {markers.map(({ warehouse, summary }) => (
              <tr key={warehouse.id} style={{ cursor: 'pointer' }} onClick={() => setSelected(warehouse.id)}>
                <td>
                  <strong>{warehouse.code}</strong>
                  <div className="muted">{warehouse.name}</div>
                </td>
                <td>{warehouse.city ?? '—'}</td>
                <td>{summary?.physicalStock ?? '—'}</td>
                <td style={{ color: 'var(--accent)' }}>{summary?.atp ?? '—'}</td>
                <td>{warehouse.isActive ? <span className="badge green">ACTIVE</span> : <span className="badge red">INACTIVE</span>}</td>
                <td className="mono">
                  {warehouse.latitude != null && warehouse.longitude != null
                    ? `${warehouse.latitude.toFixed(4)}, ${warehouse.longitude.toFixed(4)}`
                    : <span className="badge muted">LOCATION UNAVAILABLE</span>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {unavailable.length > 0 && (
        <div className="error-box" style={{ marginTop: 12 }}>
          <strong>{unavailable.length} warehouse haritada gösterilemedi (koordinat eksik).</strong>{' '}
          Koordinatlar Facility'de tanımlanmadığı sürece haritada <em>(0,0)</em> gösterilmez.
        </div>
      )}

      {selected && (
        <p className="muted">
          Seçili: <strong>{markers.find(m => m.warehouse.id === selected)?.warehouse.code}</strong> — detay için{' '}
          <a href={`#/warehouses/${selected}`} style={{ color: 'var(--accent)' }}>Warehouses</a> sayfası.
        </p>
      )}
    </div>
  );
}
