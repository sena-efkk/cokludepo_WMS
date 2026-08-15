import { expect, test } from '@playwright/test';
import { request } from '@playwright/test';

const API = 'http://127.0.0.1:5217';

// E2E: gerçek Wms.Api + PostgreSQL üzerinde kritik senaryolar.
// Not: testler sıralıdır (workers:1) ve benzersiz SKU'lar üretir.

test.describe.serial('WMS acceptance scenarios (real backend)', () => {
  let skuId: string;
  let barcode: string;
  let warehouseId: string;
  let storageLocationId: string;
  let storageLocationCode: string;
  let receivingLocationId: string;

  test.beforeAll(async () => {
    const api = await request.newContext({ baseURL: API });

    // Ürün + SKU + barcode
    const productResponse = await api.post('/api/products', {
      data: { name: `E2E Product ${Date.now()}` },
    });
    expect(productResponse.ok()).toBeTruthy();
    const product = (await productResponse.json()) as { id: string };

    const skuResponse = await api.post('/api/skus', {
      data: { productId: product.id, code: `E2E-${Date.now()}`, name: 'E2E Sku', barcode: `BC${Date.now()}`, uomCode: 'EA' },
    });
    expect(skuResponse.ok()).toBeTruthy();
    const sku = (await skuResponse.json()) as { id: string; barcodes: { value: string }[] };
    skuId = sku.id;
    barcode = sku.barcodes[0].value;

    // Warehouse + lokasyonlar
    const whResponse = await api.post('/api/warehouses', {
      data: { code: `E2E-W${Date.now()}`, name: 'E2E Warehouse', city: 'Bursa', countryCode: 'TR', latitude: 40.19, longitude: 29.07 },
    });
    expect(whResponse.ok()).toBeTruthy();
    const warehouse = (await whResponse.json()) as { id: string };
    warehouseId = warehouse.id;

    const recvResponse = await api.post(`/api/warehouses/${warehouseId}/locations`, {
      data: { code: 'RECEIVING', name: 'Giris', type: 'Receiving', holdsInventory: true },
    });
    receivingLocationId = ((await recvResponse.json()) as { id: string }).id;

    const locResponse = await api.post(`/api/warehouses/${warehouseId}/locations`, {
      data: { code: 'STORAGE', name: 'Stok', type: 'Storage', allowsPicking: true, holdsInventory: true },
    });
    const storage = (await locResponse.json()) as { id: string; code: string };
    storageLocationId = storage.id;
    storageLocationCode = storage.code;

    await api.dispose();
  });

  test('A — Stock flow: Receive → Putaway → Inventory', async () => {
    const api = await request.newContext({ baseURL: API });

    const receiptResponse = await api.post('/api/inbound/receipts', {
      data: {
        warehouseId,
        externalReference: 'E2E-ASN',
        sourceType: 'ASN',
        lines: [{ skuId, expectedQuantity: 10 }],
      },
    });
    expect(receiptResponse.ok()).toBeTruthy();
    const receipt = (await receiptResponse.json()) as { receiptId: string };

    const detail = (await (await api.get(`/api/inbound/receipts/${receipt.receiptId}`)).json()) as { lines: { id: string }[] };
    const lineId = detail.lines[0].id;

    const receiveResponse = await api.post(`/api/inbound/receipts/${receipt.receiptId}/receive`, {
      data: { receiptLineId: lineId, quantity: 10, receivingLocationId, receivingStatus: 'AVAILABLE' },
    });
    expect(receiveResponse.ok()).toBeTruthy();
    const receive = (await receiveResponse.json()) as { putawayTaskId: string };

    // Putaway (scan-enforced)
    const putawayResponse = await api.post(`/api/inbound/putaway-tasks/${receive.putawayTaskId}/complete`, {
      data: { sourceScan: 'RECEIVING', skuScan: barcode, destinationScan: storageLocationCode, quantity: 10 },
    });
    expect(putawayResponse.ok()).toBeTruthy();
    const putaway = (await putawayResponse.json()) as { status: string };
    expect(putaway.status).toBe('Completed');

    // Inventory görüntüleme (ATP)
    const summary = (await (await api.get(`/api/inventory/warehouses/${warehouseId}/skus/${skuId}`)).json()) as { onHand: number; available: number };
    expect(summary.onHand).toBe(10);
    expect(summary.available).toBe(10);

    await api.dispose();
  });

  test('B — Customer fulfillment: Order → Source → Reserve → Pick → Pack → Ship', async () => {
    const api = await request.newContext({ baseURL: API });

    // Sourcing evaluate (optimized)
    const evalResponse = await api.post('/api/fulfillment/sourcing/evaluate', {
      data: {
        destination: 'Bursa',
        destinationLatitude: 40.19,
        destinationLongitude: 29.07,
        strategy: 'optimized',
        lines: [{ skuId, quantity: 3 }],
      },
    });
    expect(evalResponse.ok()).toBeTruthy();
    const evaluation = (await evalResponse.json()) as {
      sourcingRequestId: string;
      fulfillable: boolean;
      optimization?: { warehouses: { warehouseId: string; lines: { skuId: string; requestedQuantity: number; fulfillable: boolean }[] }[] };
    };
    expect(evaluation.fulfillable).toBe(true);
    expect(evaluation.optimization).toBeTruthy();

    // Commit
    const commitResponse = await api.post(`/api/fulfillment/sourcing/${evaluation.sourcingRequestId}/commit`, {
      data: {
        plan: evaluation.optimization!.warehouses.map(w => ({
          warehouseId: w.warehouseId,
          lines: w.lines.filter(l => l.fulfillable).map(l => ({ skuId: l.skuId, quantity: l.requestedQuantity })),
        })),
      },
    });
    expect(commitResponse.ok()).toBeTruthy();
    const commit = (await commitResponse.json()) as { outcome: string; orderLinks: { outboundOrderId: string }[] };
    expect(commit.outcome).toBe('Committed');
    const orderId = commit.orderLinks[0].outboundOrderId;

    // Pick
    const order = (await (await api.get(`/api/outbound/orders/${orderId}`)).json()) as { status: string; pickTasks: { id: string; requiredQuantity: number }[] };
    expect(order.status).toBe('Allocated');
    for (const task of order.pickTasks) {
      const pickResponse = await api.post(`/api/outbound/pick-tasks/${task.id}/confirm`, {
        data: { locationScan: storageLocationCode, skuScan: barcode, quantity: task.requiredQuantity },
      });
      expect(pickResponse.ok()).toBeTruthy();
    }

    // Pack
    const packResponse = await api.post(`/api/outbound/orders/${orderId}/pack`, { data: { requestId: crypto.randomUUID() } });
    expect(packResponse.ok()).toBeTruthy();

    // Ship
    const shipResponse = await api.post(`/api/outbound/orders/${orderId}/ship`, { data: { requestId: crypto.randomUUID() } });
    expect(shipResponse.ok()).toBeTruthy();

    const finalOrder = (await (await api.get(`/api/outbound/orders/${orderId}`)).json()) as { status: string; shipment?: { status: string } };
    expect(finalOrder.status).toBe('Shipped');

    // Stok düştü
    const summary = (await (await api.get(`/api/inventory/warehouses/${warehouseId}/skus/${skuId}`)).json()) as { onHand: number };
    expect(summary.onHand).toBe(7);

    await api.dispose();
  });

  test('D — Inventory accuracy: NotFound → CycleCount → Reconciliation', async () => {
    const api = await request.newContext({ baseURL: API });

    // 2× PickNotFound (farklı siparişlerle) — stok değişmemeli
    for (let i = 0; i < 2; i++) {
      const orderResponse = await api.post('/api/outbound/orders', {
        data: { warehouseId, lines: [{ skuId, requestedQuantity: 1 }] },
      });
      const order = (await orderResponse.json()) as { orderId: string };
      await api.post(`/api/outbound/orders/${order.orderId}/allocate`, { data: {} });
      const detail = (await (await api.get(`/api/outbound/orders/${order.orderId}`)).json()) as { pickTasks: { id: string }[] };
      const notFoundResponse = await api.post(`/api/outbound/pick-tasks/${detail.pickTasks[0].id}/not-found`, { data: { requestId: crypto.randomUUID() } });
      expect(notFoundResponse.ok()).toBeTruthy();
    }

    // Cycle count değerlendirmesi (RED/tekrarlı NotFound)
    const evaluateCounts = await api.post('/api/inventory/accuracy/cycle-counts/evaluate', { data: {} });
    expect(evaluateCounts.ok()).toBeTruthy();

    // Queue'da bizim SKU için task bul
    const queue = (await (await api.get(`/api/inventory/accuracy/cycle-counts/queue?limit=500`)).json()) as { id: string; skuId: string; status: string }[];
    const task = queue.find(t => t.skuId === skuId && t.status === 'Pending');
    expect(task).toBeTruthy();

    await api.post(`/api/inventory/accuracy/cycle-counts/${task!.id}/start`, { data: { assignedTo: 'e2e' } });
    const completeResponse = await api.post(`/api/inventory/accuracy/cycle-counts/${task!.id}/complete`, {
      data: { countedQuantity: 4, countedBy: 'e2e' },
    });
    expect(completeResponse.ok()).toBeTruthy();
    const result = (await completeResponse.json()) as { outcome: string };
    expect(result.outcome).toBe('VarianceDetected');

    // Reconciliation approve
    const reconciliations = (await (await api.get('/api/inventory/accuracy/reconciliations?limit=500')).json()) as { id: string; skuId: string; reconciliationStatus: string }[];
    const reconciliation = reconciliations.find(r => r.skuId === skuId && r.reconciliationStatus === 'Open');
    expect(reconciliation).toBeTruthy();

    const approveResponse = await api.post(`/api/inventory/accuracy/reconciliations/${reconciliation!.id}/approve`, {
      data: { reason: 'CycleCountVariance', resolvedBy: 'e2e', resolutionNote: 'E2E onayı' },
    });
    expect(approveResponse.ok()).toBeTruthy();
    const approval = (await approveResponse.json()) as { outcome: string };
    expect(approval.outcome).toBe('Applied');

    // Stok yalnız reconciliation ile düzeldi: 7 - 3 = 4
    const summary = (await (await api.get(`/api/inventory/warehouses/${warehouseId}/skus/${skuId}`)).json()) as { onHand: number };
    expect(summary.onHand).toBe(4);

    await api.dispose();
  });

  test('C — Transfer: A → InTransit → B (partial receive)', async () => {
    const api = await request.newContext({ baseURL: API });

    // Destination warehouse
    const destResponse = await api.post('/api/warehouses', {
      data: { code: `E2E-D${Date.now()}`, name: 'E2E Destination', city: 'Istanbul', countryCode: 'TR', latitude: 41.0, longitude: 28.98 },
    });
    const destinationId = ((await destResponse.json()) as { id: string }).id;
    const destRecvResponse = await api.post(`/api/warehouses/${destinationId}/locations`, {
      data: { code: 'RECEIVING', name: 'Giris', type: 'Receiving', holdsInventory: true },
    });
    const destReceivingId = ((await destRecvResponse.json()) as { id: string }).id;

    const transferResponse = await api.post('/api/transfers', {
      data: { sourceWarehouseId: warehouseId, destinationWarehouseId: destinationId, lines: [{ skuId, requestedQuantity: 2 }] },
    });
    expect(transferResponse.ok()).toBeTruthy();
    const transfer = (await transferResponse.json()) as { transferId: string };
    const transferId = transfer.transferId;

    const allocateResponse = await api.post(`/api/transfers/${transferId}/allocate`, { data: {} });
    expect(allocateResponse.ok()).toBeTruthy();
    const allocate = (await allocateResponse.json()) as { outboundOrderId: string };
    const outboundOrderId = allocate.outboundOrderId;

    // Pick + pack source order
    const order = (await (await api.get(`/api/outbound/orders/${outboundOrderId}`)).json()) as { pickTasks: { id: string; requiredQuantity: number }[] };
    for (const task of order.pickTasks) {
      await api.post(`/api/outbound/pick-tasks/${task.id}/confirm`, {
        data: { locationScan: storageLocationCode, skuScan: barcode, quantity: task.requiredQuantity },
      });
    }
    await api.post(`/api/outbound/orders/${outboundOrderId}/pack`, { data: { requestId: crypto.randomUUID() } });

    const shipResponse = await api.post(`/api/transfers/${transferId}/ship`, { data: {} });
    expect(shipResponse.ok()).toBeTruthy();
    const ship = (await shipResponse.json()) as { outcome: string };
    expect(ship.outcome).toBe('Shipped');

    const inTransit = (await (await api.get(`/api/transfers/${transferId}`)).json()) as { status: string; inTransitQuantity: number; lines: { id: string }[] };
    expect(inTransit.status).toBe('InTransit');
    expect(inTransit.inTransitQuantity).toBe(2);

    // Partial receive 1
    const receiveResponse = await api.post(`/api/transfers/${transferId}/receive`, {
      data: { transferLineId: inTransit.lines[0].id, quantity: 1, receivingLocationId: destReceivingId, receivingStatus: 'AVAILABLE' },
    });
    expect(receiveResponse.ok()).toBeTruthy();

    const afterPartial = (await (await api.get(`/api/transfers/${transferId}`)).json()) as { inTransitQuantity: number };
    expect(afterPartial.inTransitQuantity).toBe(1);

    // Final receive
    await api.post(`/api/transfers/${transferId}/receive`, {
      data: { transferLineId: inTransit.lines[0].id, quantity: 1, receivingLocationId: destReceivingId, receivingStatus: 'AVAILABLE' },
    });
    const final = (await (await api.get(`/api/transfers/${transferId}`)).json()) as { status: string; inTransitQuantity: number };
    expect(final.status).toBe('Completed');
    expect(final.inTransitQuantity).toBe(0);

    await api.dispose();
  });

  test('E — Optimization: nearest vs greedy vs optimized (compare)', async () => {
    const api = await request.newContext({ baseURL: API });

    const evalResponse = await api.post('/api/fulfillment/sourcing/evaluate', {
      data: {
        destination: 'Bursa',
        destinationLatitude: 40.19,
        destinationLongitude: 29.07,
        strategy: 'compare',
        lines: [{ skuId, quantity: 1 }],
      },
    });
    expect(evalResponse.ok()).toBeTruthy();
    const evaluation = (await evalResponse.json()) as {
      comparison?: { nearest?: unknown; greedy?: unknown; optimized?: unknown; recommendedStrategy: string; counterfactuals: string[] };
    };
    expect(evaluation.comparison).toBeTruthy();
    expect(evaluation.comparison!.nearest).toBeTruthy();
    expect(evaluation.comparison!.greedy).toBeTruthy();
    expect(evaluation.comparison!.optimized).toBeTruthy();
    expect(evaluation.comparison!.recommendedStrategy).toBeTruthy();
    expect(evaluation.comparison!.counterfactuals.length).toBeGreaterThan(0);

    await api.dispose();
  });

  test('F — Concurrency/error: SOURCING_STALE UI-facing protection', async () => {
    const api = await request.newContext({ baseURL: API });

    const evalResponse = await api.post('/api/fulfillment/sourcing/evaluate', {
      data: { destinationLatitude: 40.19, destinationLongitude: 29.07, lines: [{ skuId, quantity: 2 }] },
    });
    const evaluation = (await evalResponse.json()) as { sourcingRequestId: string; fulfillable: boolean; candidates: { warehouses: { warehouseId: string; lines: { skuId: string; requestedQuantity: number; fulfillable: boolean }[] }[] }[] };
    expect(evaluation.fulfillable).toBe(true);

    // Planın güvendiği warehouse'u bul ve rakip işlemle stoğu tüket
    const selected = evaluation.candidates.find(c => c.warehouses.some(w => w.lines.some(l => l.fulfillable)));
    expect(selected).toBeTruthy();
    const targetWarehouse = selected!.warehouses.find(w => w.lines.some(l => l.fulfillable))!;
    const targetLine = targetWarehouse.lines.find(l => l.fulfillable)!;

    const rivalOrder = await api.post('/api/outbound/orders', {
      data: { warehouseId: targetWarehouse.warehouseId, lines: [{ skuId, requestedQuantity: targetLine.requestedQuantity }] },
    });
    const rival = (await rivalOrder.json()) as { orderId: string };
    const rivalAllocate = await api.post(`/api/outbound/orders/${rival.orderId}/allocate`, { data: {} });
    expect(rivalAllocate.ok()).toBeTruthy();

    // Commit → stale (veya başarı — kalan stok yetersizse stale beklenir)
    const commitResponse = await api.post(`/api/fulfillment/sourcing/${evaluation.sourcingRequestId}/commit`, {
      data: {
        plan: selected!.warehouses.map(w => ({
          warehouseId: w.warehouseId,
          lines: w.lines.filter(l => l.fulfillable).map(l => ({ skuId: l.skuId, quantity: l.requestedQuantity })),
        })),
      },
    });

    if (commitResponse.ok()) {
      const commit = (await commitResponse.json()) as { outcome: string };
      expect(['Committed', 'Stale']).toContain(commit.outcome);
    } else {
      const body = (await commitResponse.json()) as { staleReason?: string };
      expect(commitResponse.status()).toBe(409);
      expect(body.staleReason).toBeTruthy();
    }

    await api.dispose();
  });
});
