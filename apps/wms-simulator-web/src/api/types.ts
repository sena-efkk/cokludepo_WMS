export interface WarehouseInfo {
  id: string;
  code: string;
  name: string;
  addressLine?: string;
  city?: string;
  countryCode?: string;
  latitude?: number;
  longitude?: number;
  isActive: boolean;
}

export interface LocationInfo {
  id: string;
  warehouseId: string;
  code: string;
  name: string;
  type: string;
  parentLocationId?: string;
  allowsPicking: boolean;
  allowsPutaway: boolean;
  allowsReplenishment: boolean;
  holdsInventory: boolean;
  isActive: boolean;
}

export interface LocationTreeNode {
  id: string;
  code: string;
  name: string;
  type: string;
  isActive: boolean;
  children: LocationTreeNode[];
}

export interface SkuInfo {
  id: string;
  code: string;
  isActive: boolean;
}

export interface SkuWithBarcodes extends SkuInfo {
  name?: string;
  barcodes?: { value: string; type: string }[];
}

export interface StatusQuantity {
  status: string;
  quantity: number;
}

export interface WarehouseSkuSummary {
  skuId: string;
  warehouseId: string;
  onHand: number;
  allocated: number;
  available: number;
  byStatus: StatusQuantity[];
}

export interface BalanceView {
  skuId: string;
  warehouseId: string;
  locationId: string;
  status: string;
  quantity: number;
  allocated: number;
  available: number;
}

export interface LedgerEntry {
  id: string;
  requestId: string;
  skuId: string;
  warehouseId: string;
  locationId: string;
  status: string;
  entryType: string;
  quantityDelta: number;
  allocatedDelta: number;
  movementId?: string;
  referenceType?: string;
  referenceId?: string;
  occurredAt: string;
}

export interface NetworkWarehouseSummary {
  warehouseId: string;
  code: string;
  isOperational: boolean;
  physicalStock: number;
  atp: number;
  allocated: number;
  hold: number;
  quarantine: number;
  damaged: number;
  skuCount: number;
}

export interface NetworkSummary {
  totalWarehouses: number;
  activeWarehouses: number;
  physicalStock: number;
  atp: number;
  allocated: number;
  hold: number;
  quarantine: number;
  damaged: number;
  warehouses: NetworkWarehouseSummary[];
}

export interface SkuLocationBalance {
  locationId: string;
  status: string;
  quantity: number;
  allocated: number;
  available: number;
}

export interface SkuNetworkWarehouse {
  warehouseId: string;
  warehouseCode: string;
  isOperational: boolean;
  physicalStock: number;
  allocated: number;
  atp: number;
  hold: number;
  quarantine: number;
  damaged: number;
  riskLevel?: string;
  riskScore?: number;
  recentNotFoundCount?: number;
}

export interface SkuNetworkView {
  skuId: string;
  skuCode: string;
  networkPhysicalStock: number;
  networkAtp: number;
  networkAllocated: number;
  warehouses: SkuNetworkWarehouse[];
}

export interface ReceiptSummary {
  id: string;
  receiptNumber: string;
  warehouseId: string;
  externalReference?: string;
  status: string;
  createdAt: string;
  totalExpected: number;
  totalReceived: number;
}

export interface ReceiptLine {
  id: string;
  skuId: string;
  expectedQuantity: number;
  receivedQuantity: number;
  disposition?: string;
}

export interface ReceiveRecord {
  id: string;
  requestId: string;
  receiptLineId: string;
  quantity: number;
  disposition: string;
  receivingLocationId: string;
  inventoryStatus: string;
  inventoryOperationId: string;
  receivedAt: string;
}

export interface ReceiptDetail extends ReceiptSummary {
  requestId: string;
  sourceType?: string;
  receivingStartedAt?: string;
  completedAt?: string;
  cancelledAt?: string;
  lines: ReceiptLine[];
  receiveRecords: ReceiveRecord[];
}

export interface PutawayTask {
  id: string;
  receiptId: string;
  receiptLineId: string;
  receiveRecordId: string;
  skuId: string;
  warehouseId: string;
  sourceLocationId: string;
  inventoryStatus: string;
  quantity: number;
  status: string;
  movementId?: string;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
}

export interface OrderSummary {
  id: string;
  orderNumber: string;
  warehouseId: string;
  externalOrderReference?: string;
  status: string;
  createdAt: string;
  totalRequested: number;
}

export interface OrderLine {
  id: string;
  skuId: string;
  requestedQuantity: number;
  reservationId?: string;
}

export interface PickTaskInfo {
  id: string;
  orderId: string;
  orderLineId: string;
  reservationId: string;
  reservationLineId: string;
  warehouseId: string;
  locationId: string;
  skuId: string;
  requiredQuantity: number;
  pickedQuantity: number;
  status: string;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
}

export interface PackageInfo {
  id: string;
  orderId: string;
  packageNumber: string;
  status: string;
  createdAt: string;
  packedAt: string;
}

export interface ShipmentInfo {
  id: string;
  orderId: string;
  shipmentNumber: string;
  status: string;
  trackingNumber?: string;
  carrierCode?: string;
  createdAt: string;
  shippedAt?: string;
}

export interface OrderDetail extends OrderSummary {
  requestId: string;
  allocatedAt?: string;
  pickingStartedAt?: string;
  packedAt?: string;
  shippedAt?: string;
  cancelledAt?: string;
  lines: OrderLine[];
  pickTasks: PickTaskInfo[];
  package?: PackageInfo;
  shipment?: ShipmentInfo;
}

export interface TransferSummary {
  id: string;
  transferNumber: string;
  sourceWarehouseId: string;
  destinationWarehouseId: string;
  status: string;
  inTransitQuantity: number;
  createdAt: string;
}

export interface TransferLine {
  id: string;
  skuId: string;
  requestedQuantity: number;
  shippedQuantity: number;
  receivedQuantity: number;
  confirmedVarianceQuantity: number;
  inTransitQuantity: number;
  isClosed: boolean;
  outboundOrderLineId?: string;
  inboundReceiptLineId?: string;
}

export interface TransferDiscrepancy {
  id: string;
  requestId: string;
  transferLineId: string;
  quantity: number;
  reason: string;
  note?: string;
  createdAt: string;
}

export interface TransferDetail extends TransferSummary {
  requestId: string;
  externalReference?: string;
  outboundOrderId?: string;
  inboundReceiptId?: string;
  shippedAt?: string;
  completedAt?: string;
  cancelledAt?: string;
  lines: TransferLine[];
  discrepancies: TransferDiscrepancy[];
}

export interface RiskAssessment {
  skuId: string;
  warehouseId: string;
  locationId: string;
  count30d: number;
  count90d: number;
  count180d: number;
  lastActivityAt?: string;
  daysSinceLastActivity?: number;
  velocityClass: string;
  movementState: string;
  notFoundCount7d: number;
  notFoundCount30d: number;
  consecutiveNotFound: number;
  lastNotFoundAt?: string;
  riskScore: number;
  riskLevel: string;
  reasons: { code: string; points: number; description: string }[];
}

export interface CycleCountTaskInfo {
  id: string;
  warehouseId: string;
  locationId: string;
  skuId: string;
  reason: string;
  priority: string;
  status: string;
  riskScoreAtCreation: number;
  expectedQuantity: number;
  expectedAllocated: number;
  expectedStatus?: string;
  evidence: string;
  assignedTo?: string;
  createdAt: string;
  dueAt?: string;
  startedAt?: string;
  completedAt?: string;
}

export interface CycleCountResultInfo {
  id: string;
  cycleCountTaskId: string;
  countedQuantity: number;
  expectedQuantity: number;
  expectedAllocated: number;
  expectedStatus: string;
  variance: number;
  outcome: string;
  countedBy?: string;
  countedAt: string;
}

export interface ReconciliationInfo {
  id: string;
  cycleCountResultId: string;
  warehouseId: string;
  skuId: string;
  locationId: string;
  status: string;
  expectedQuantity: number;
  countedQuantity: number;
  variance: number;
  isLargeVariance: boolean;
  reason: string;
  reconciliationStatus: string;
  resolutionNote: string;
  resolvedBy?: string;
  createdAt: string;
  resolvedAt?: string;
}

export interface AccuracySignal {
  id: string;
  requestId: string;
  signalType: string;
  sourceType: string;
  skuId: string;
  warehouseId: string;
  locationId: string;
  sourceReferenceId?: string;
  systemQuantityAtSignal: number;
  allocatedAtSignal: number;
  availableAtSignal: number;
  statusAtSignal: string;
  occurredAt: string;
}

export interface SourcingCandidateLine {
  skuId: string;
  skuCode: string;
  requestedQuantity: number;
  atp: number;
  fulfillable: boolean;
}

export interface SourcingWarehouseAssignment {
  warehouseId: string;
  warehouseCode: string;
  lines: SourcingCandidateLine[];
}

export interface SourcingCandidate {
  rank: number;
  warehouseId: string;
  warehouseCode: string;
  canFulfillCompletely: boolean;
  fulfillableLineCount: number;
  totalLineCount: number;
  score: number;
  explanations: string[];
  warehouses: SourcingWarehouseAssignment[];
  worstRiskLevel?: string;
  recentNotFoundCount?: number;
}

export interface SourcingShortage {
  skuId: string;
  skuCode: string;
  requestedQuantity: number;
  networkAtp: number;
  shortage: number;
}

export interface SourcingIncomingStock {
  skuId: string;
  skuCode: string;
  inTransitQuantity: number;
}

export interface CostBreakdown {
  transportCost: number;
  dispatchCost: number;
  packagingCost: number;
  handlingCost: number;
  pickingCost: number;
  splitPenalty: number;
  inventoryReliabilityPenalty: number;
  scarcityPenalty: number;
  slaPenalty: number;
  totalCost: number;
}

export interface OptimizedPlan {
  strategy: string;
  status: string;
  strategyUsed: string;
  warehouses: SourcingWarehouseAssignment[];
  shipmentCount: number;
  totalDistanceKm: number;
  totalDurationMinutes: number;
  cost: CostBreakdown;
  routeSource: string;
  completeCoverage: boolean;
  explanations: string[];
  evaluationTimeMs: number;
}

export interface StrategyComparison {
  nearest?: OptimizedPlan;
  greedy?: OptimizedPlan;
  optimized?: OptimizedPlan;
  recommendedStrategy: string;
  savingsVsNearest?: number;
  counterfactuals: string[];
}

export interface SourcingEvaluation {
  sourcingRequestId: string;
  fulfillable: boolean;
  candidates: SourcingCandidate[];
  shortages: SourcingShortage[];
  incomingStock: SourcingIncomingStock[];
  optimization?: OptimizedPlan;
  comparison?: StrategyComparison;
}

export interface HealthResult {
  status: string;
  results: Record<string, { status: string; description: string }>;
}
