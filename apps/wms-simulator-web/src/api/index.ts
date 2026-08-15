import { get, post } from './http';
import type {
  WarehouseInfo,
  LocationInfo,
  LocationTreeNode,
  SkuInfo,
  SkuWithBarcodes,
  WarehouseSkuSummary,
  BalanceView,
  LedgerEntry,
  NetworkSummary,
  SkuLocationBalance,
  SkuNetworkView,
  ReceiptSummary,
  ReceiptDetail,
  PutawayTask,
  OrderSummary,
  OrderDetail,
  TransferSummary,
  TransferDetail,
  RiskAssessment,
  CycleCountTaskInfo,
  CycleCountResultInfo,
  ReconciliationInfo,
  AccuracySignal,
  SourcingEvaluation,
  HealthResult,
} from './types';

export const facilityApi = {
  listWarehouses: () => get<WarehouseInfo[]>('/api/warehouses'),
  listLocations: (warehouseId: string) => get<LocationInfo[]>(`/api/warehouses/${warehouseId}/locations`),
  locationTree: (warehouseId: string) => get<LocationTreeNode[]>(`/api/warehouses/${warehouseId}/location-tree`),
};

export const masterDataApi = {
  listSkus: () => get<SkuWithBarcodes[]>('/api/skus'),
  searchSkus: (query: string) => get<SkuInfo[]>(`/api/skus?search=${encodeURIComponent(query)}`),
  getSku: (id: string) => get<SkuWithBarcodes>(`/api/skus/${id}`),
};

export const inventoryApi = {
  balances: (warehouseId: string, skuId?: string) =>
    get<BalanceView[]>(`/api/inventory/warehouses/${warehouseId}/balances${skuId ? `?skuId=${skuId}` : ''}`),
  warehouseSkuSummary: (warehouseId: string, skuId: string) =>
    get<WarehouseSkuSummary>(`/api/inventory/warehouses/${warehouseId}/skus/${skuId}`),
  ledger: (params: { warehouseId?: string; skuId?: string; locationId?: string; limit?: number }) => {
    const query = new URLSearchParams();
    if (params.warehouseId) query.set('warehouseId', params.warehouseId);
    if (params.skuId) query.set('skuId', params.skuId);
    if (params.locationId) query.set('locationId', params.locationId);
    query.set('limit', String(params.limit ?? 100));
    return get<LedgerEntry[]>(`/api/inventory/ledger?${query.toString()}`);
  },
  openingBalance: (body: {
    requestId?: string;
    skuId: string;
    warehouseId: string;
    locationId: string;
    status: string;
    quantity: number;
  }) => post<{ outcome: string; requestId: string }>('/api/inventory/opening-balances', body),
};

export const networkApi = {
  summary: () => get<NetworkSummary>('/api/network/inventory/summary'),
  sku: (skuId: string) => get<SkuNetworkView>(`/api/network/inventory/skus/${skuId}`),
  skuByBarcode: async (barcode: string) => {
    const skus = await get<SkuWithBarcodes[]>('/api/skus');
    const match = skus.find(s => s.barcodes?.some(b => b.value === barcode));
    return match ?? null;
  },
};

export const inboundApi = {
  listReceipts: (warehouseId?: string) =>
    get<ReceiptSummary[]>(`/api/inbound/receipts${warehouseId ? `?warehouseId=${warehouseId}` : '?limit=100'}`),
  getReceipt: (id: string) => get<ReceiptDetail>(`/api/inbound/receipts/${id}`),
  createReceipt: (body: {
    requestId?: string;
    receiptNumber?: string;
    warehouseId: string;
    externalReference?: string;
    sourceType?: string;
    lines: { skuId: string; expectedQuantity: number }[];
  }) => post<{ outcome: string; receiptId: string; receiptNumber: string }>('/api/inbound/receipts', body),
  receive: (receiptId: string, body: {
    requestId?: string;
    receiptLineId: string;
    quantity: number;
    receivingLocationId: string;
    receivingStatus: string;
  }) =>
    post<{ outcome: string; receiveRecordId: string; disposition: string; lineReceivedQuantity: number; putawayTaskId: string }>(
      `/api/inbound/receipts/${receiptId}/receive`,
      body,
    ),
  listPutawayTasks: (warehouseId?: string) =>
    get<PutawayTask[]>(`/api/inbound/putaway-tasks${warehouseId ? `?warehouseId=${warehouseId}` : '?limit=100'}`),
  completePutaway: (taskId: string, body: {
    requestId?: string;
    sourceScan: string;
    skuScan: string;
    destinationScan: string;
    quantity: number;
    deviceId?: string;
    operatorId?: string;
  }) =>
    post<{ status: string; taskId: string; movementId?: string; rejectionCode?: string; rejectionReason?: string }>(
      `/api/inbound/putaway-tasks/${taskId}/complete`,
      body,
    ),
  summary: () => get<{ openReceipts: number; partiallyReceivedReceipts: number; pendingPutawayTasks: number; inProgressPutawayTasks: number }>('/api/inbound/summary'),
};

export const outboundApi = {
  listOrders: (warehouseId?: string) =>
    get<OrderSummary[]>(`/api/outbound/orders${warehouseId ? `?warehouseId=${warehouseId}` : '?limit=100'}`),
  getOrder: (id: string) => get<OrderDetail>(`/api/outbound/orders/${id}`),
  createOrder: (body: {
    requestId?: string;
    orderNumber?: string;
    warehouseId: string;
    externalOrderReference?: string;
    lines: { skuId: string; requestedQuantity: number }[];
  }) => post<{ outcome: string; orderId: string; orderNumber: string }>('/api/outbound/orders', body),
  allocate: (orderId: string) => post<{ outcome: string; orderId: string }>(`/api/outbound/orders/${orderId}/allocate`, {}),
  confirmPick: (taskId: string, body: { locationScan: string; skuScan: string; quantity: number }) =>
    post<{ taskId: string; taskCompleted: boolean; pickedQuantity: number; remainingQuantity: number }>(
      `/api/outbound/pick-tasks/${taskId}/confirm`,
      body,
    ),
  notFoundPick: (taskId: string, body: { requestId?: string }) =>
    post<{ taskId: string; orderPickException: boolean; signalRequestId?: string }>(
      `/api/outbound/pick-tasks/${taskId}/not-found`,
      body,
    ),
  pack: (orderId: string) =>
    post<{ outcome: string; orderId: string; packageId: string; packageNumber: string }>(
      `/api/outbound/orders/${orderId}/pack`,
      { requestId: crypto.randomUUID() },
    ),
  ship: (orderId: string) =>
    post<{ outcome: string; orderId: string; shipmentId: string; shipmentNumber: string }>(
      `/api/outbound/orders/${orderId}/ship`,
      { requestId: crypto.randomUUID(), carrierCode: 'DEMO' },
    ),
  summary: () => get<{ openOrders: number; allocatedOrders: number; pickingOrders: number; pendingPickTasks: number; pendingShipments: number }>('/api/outbound/summary'),
};

export const transfersApi = {
  list: (warehouseId?: string) => get<TransferSummary[]>(`/api/transfers?limit=100`),
  get: (id: string) => get<TransferDetail>(`/api/transfers/${id}`),
  create: (body: {
    requestId?: string;
    transferNumber?: string;
    sourceWarehouseId: string;
    destinationWarehouseId: string;
    externalReference?: string;
    lines: { skuId: string; requestedQuantity: number }[];
  }) => post<{ outcome: string; transferId: string; transferNumber: string }>('/api/transfers', body),
  allocate: (id: string) => post<{ outcome: string; transferId: string; outboundOrderId?: string }>(`/api/transfers/${id}/allocate`, {}),
  ship: (id: string) =>
    post<{ outcome: string; transferId: string; shipmentId?: string; shipmentNumber?: string; inboundReceiptId?: string }>(
      `/api/transfers/${id}/ship`,
      {},
    ),
  receive: (id: string, body: {
    requestId?: string;
    transferLineId: string;
    quantity: number;
    receivingLocationId: string;
    receivingStatus: string;
  }) =>
    post<{ outcome: string; transferId: string; transferLineId: string; lineReceivedQuantity: number; lineInTransitQuantity: number }>(
      `/api/transfers/${id}/receive`,
      body,
    ),
  confirmVariance: (id: string, body: { requestId?: string; transferLineId: string; quantity: number; reason: string; note?: string }) =>
    post<{ outcome: string; transferId: string; transferLineId: string; discrepancyId?: string; lineInTransitQuantity: number; transferCompleted: boolean }>(
      `/api/transfers/${id}/confirm-variance`,
      body,
    ),
  summary: () => get<{ openTransfers: number; inTransitTotal: number; receivingTransfers: number }>('/api/transfers/summary'),
};

export const accuracyApi = {
  risk: (params: { warehouseId?: string; skuId?: string; riskLevel?: string; limit?: number }) => {
    const query = new URLSearchParams();
    if (params.warehouseId) query.set('warehouseId', params.warehouseId);
    if (params.skuId) query.set('skuId', params.skuId);
    if (params.riskLevel) query.set('riskLevel', params.riskLevel);
    query.set('limit', String(params.limit ?? 100));
    return get<RiskAssessment[]>(`/api/inventory/accuracy/risk?${query.toString()}`);
  },
  highRisk: () => get<RiskAssessment[]>('/api/inventory/accuracy/high-risk?limit=100'),
  cycleCountQueue: (warehouseId?: string) =>
    get<CycleCountTaskInfo[]>(`/api/inventory/accuracy/cycle-counts/queue?limit=100${warehouseId ? `&warehouseId=${warehouseId}` : ''}`),
  cycleCounts: () => get<CycleCountTaskInfo[]>('/api/inventory/accuracy/cycle-counts?limit=100'),
  getCycleCount: (id: string) => get<CycleCountTaskInfo & { result?: CycleCountResultInfo }>(`/api/inventory/accuracy/cycle-counts/${id}`),
  startCycleCount: (id: string) => post<CycleCountTaskInfo>(`/api/inventory/accuracy/cycle-counts/${id}/start`, { assignedTo: 'demo-operator' }),
  completeCycleCount: (id: string, countedQuantity: number) =>
    post<CycleCountResultInfo>(`/api/inventory/accuracy/cycle-counts/${id}/complete`, { countedQuantity, countedBy: 'demo-operator' }),
  reconciliations: (warehouseId?: string) =>
    get<ReconciliationInfo[]>(`/api/inventory/accuracy/reconciliations?limit=100${warehouseId ? `&warehouseId=${warehouseId}` : ''}`),
  getReconciliation: (id: string) => get<ReconciliationInfo>(`/api/inventory/accuracy/reconciliations/${id}`),
  approveReconciliation: (id: string, body: { requestId?: string; reason: string; resolvedBy?: string; resolutionNote?: string; force?: boolean }) =>
    post<{ outcome: string; adjustmentId?: string }>(`/api/inventory/accuracy/reconciliations/${id}/approve`, body),
  rejectReconciliation: (id: string, body: { resolvedBy?: string; resolutionNote?: string }) =>
    post<unknown>(`/api/inventory/accuracy/reconciliations/${id}/reject`, body),
  recentNotFound: () =>
    get<AccuracySignal[]>('/api/inventory/accuracy/signals/recent-not-found?days=7&limit=50'),
  summary: () => get<{ highRiskLocations: number; openCycleCounts: number; openReconciliations: number; recentPickNotFound: number }>('/api/inventory/accuracy/summary'),
};

export const fulfillmentApi = {
  evaluate: (body: {
    requestId?: string;
    destination?: string;
    destinationLatitude?: number;
    destinationLongitude?: number;
    strategy?: string;
    lines: { skuId: string; quantity: number }[];
  }) => post<SourcingEvaluation>('/api/fulfillment/sourcing/evaluate', body),
  commit: (sourcingRequestId: string, body: {
    requestId?: string;
    plan: { warehouseId: string; lines: { skuId: string; quantity: number }[] }[];
    optimization?: unknown;
  }) =>
    post<{ outcome: string; decisionId?: string; orderLinks: { warehouseId: string; outboundOrderId: string; orderNumber: string }[]; staleReason?: string }>(
      `/api/fulfillment/sourcing/${sourcingRequestId}/commit`,
      body,
    ),
  getSourcing: (id: string) =>
    get<{ id: string; requestId: string; destination: string; status: string; createdAt: string; orderLinks: { warehouseId: string; outboundOrderId: string; orderNumber: string }[] }>(
      `/api/fulfillment/sourcing/${id}`,
    ),
};

export const opsApi = {
  health: () => get<HealthResult>('/health'),
  scenarioInit: (scenario: string) =>
    post<{ warehousesCreated: number; skusCreated: number; stockLocations: number; receiptsCreated: number }>(
      `/api/dev/scenarios/${scenario}/initialize`,
      {},
    ),
};
