import { NavLink, Route, Routes } from 'react-router-dom';
import { OverviewPage } from './features/overview/OverviewPage';
import { NetworkPage } from './features/network/NetworkPage';
import { WarehousePage } from './features/warehouse/WarehousePage';
import { InventoryPage } from './features/inventory/InventoryPage';
import { InboundPage } from './features/inbound/InboundPage';
import { OutboundPage } from './features/outbound/OutboundPage';
import { TransfersPage } from './features/transfers/TransfersPage';
import { AccuracyPage } from './features/accuracy/AccuracyPage';
import { SourcingPage } from './features/sourcing/SourcingPage';
import { ScenariosPage } from './features/scenarios/ScenariosPage';
import { OperationsPage } from './features/operations/OperationsPage';

const NAV = [
  { to: '/', label: 'Overview' },
  { to: '/network', label: 'Network' },
  { to: '/warehouses', label: 'Warehouses' },
  { to: '/inventory', label: 'Inventory' },
  { to: '/inbound', label: 'Inbound' },
  { to: '/outbound', label: 'Outbound' },
  { to: '/transfers', label: 'Transfers' },
  { to: '/accuracy', label: 'Accuracy' },
  { to: '/sourcing', label: 'Sourcing' },
  { to: '/scenarios', label: 'Scenarios' },
  { to: '/operations', label: 'Operations' },
];

export default function App() {
  return (
    <div className="app">
      <nav className="sidebar">
        <div className="brand">
          <span className="brand-mark">WMS</span>
          <div>
            <strong>Simulator</strong>
            <small>Multi-Warehouse Demo</small>
          </div>
        </div>
        <ul>
          {NAV.map(item => (
            <li key={item.to}>
              <NavLink to={item.to} end={item.to === '/'} className={({ isActive }) => (isActive ? 'active' : '')}>
                {item.label}
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>
      <main className="content">
        <Routes>
          <Route path="/" element={<OverviewPage />} />
          <Route path="/network" element={<NetworkPage />} />
          <Route path="/warehouses" element={<WarehousePage />} />
          <Route path="/warehouses/:id" element={<WarehousePage />} />
          <Route path="/inventory" element={<InventoryPage />} />
          <Route path="/inbound" element={<InboundPage />} />
          <Route path="/outbound" element={<OutboundPage />} />
          <Route path="/transfers" element={<TransfersPage />} />
          <Route path="/accuracy" element={<AccuracyPage />} />
          <Route path="/sourcing" element={<SourcingPage />} />
          <Route path="/scenarios" element={<ScenariosPage />} />
          <Route path="/operations" element={<OperationsPage />} />
        </Routes>
      </main>
    </div>
  );
}
