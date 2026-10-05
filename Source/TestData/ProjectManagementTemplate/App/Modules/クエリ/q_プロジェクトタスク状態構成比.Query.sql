SELECT
  CASE
    WHEN t.status = '完了' THEN '完了'
    WHEN date(t.end_date) < date('now', 'localtime') THEN '遅延'
    ELSE t.status
  END AS category_name,
  COUNT(*) AS task_count
FROM task t
WHERE @project_id IS NOT NULL
  AND t.project_id = @project_id
GROUP BY category_name
