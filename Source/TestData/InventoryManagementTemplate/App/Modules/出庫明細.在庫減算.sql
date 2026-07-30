INSERT INTO inventory(product_id, warehouse_id, current_stock, version, created_at, updated_at)
SELECT
  @product_id,
  s.warehouse_id,
  -@quantity,
  0,
  strftime('%Y/%m/%d %H:%M:%S', 'now', '+9 hours'),
  strftime('%Y/%m/%d %H:%M:%S', 'now', '+9 hours')
FROM shipping s
JOIN shipping_detail sd ON sd.shipping_id = s.id
WHERE sd.id = last_insert_rowid()
ON CONFLICT(product_id, warehouse_id)
DO UPDATE SET
  current_stock = current_stock + excluded.current_stock,
  version = version + 1,
  updated_at = excluded.updated_at;