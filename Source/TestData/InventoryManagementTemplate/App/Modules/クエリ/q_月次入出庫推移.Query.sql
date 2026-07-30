-- 直近6ヶ月の月次入庫量・出庫量
SELECT month, in_qty, out_qty
FROM (
    SELECT
        month,
        SUM(in_qty) AS in_qty,
        SUM(out_qty) AS out_qty
    FROM (
        SELECT substr(r.receiving_date, 1, 7) AS month, rd.quantity AS in_qty, 0 AS out_qty
        FROM receiving r JOIN receiving_detail rd ON rd.receiving_id = r.id
        UNION ALL
        SELECT substr(s.shipping_date, 1, 7) AS month, 0, sd.quantity
        FROM shipping s JOIN shipping_detail sd ON sd.shipping_id = s.id
    )
    GROUP BY month
    ORDER BY month DESC
    LIMIT 6
)
ORDER BY month ASC
