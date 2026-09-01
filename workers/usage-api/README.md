# clevo-usage-api 部署说明

Worker 名称：`clevo-usage-api`  
D1 数据库：`clevo-usage-db`  
D1 绑定变量：`DB`

## 在 Cloudflare 控制台部署

1. 打开 `Workers & Pages` → `clevo-usage-api` → `编辑代码`。
2. 用 `src/index.js` 的完整内容替换编辑器中的默认 `worker.js`。这是单文件版本，不需要新增文件。
3. 保存并部署。
4. 在 `设置` → `Bindings` 确认 D1 绑定仍为 `DB` → `clevo-usage-db`。

本目录的 `schema.sql` 是表结构备份；你已经执行过同样的 SQL，不需要重复执行。

## 查看统计后台

统计页面地址：`https://clevo-usage-api.yycc1936.workers.dev/admin`

第一次部署时，在 Worker 的 `Settings` → `Variables and Secrets` 中新增一个加密变量：

- 名称：`ADMIN_TOKEN`
- 值：你自己设置的一串管理员口令

保存并重新部署后，打开 `/admin` 输入这个口令即可查看统计。口令不会写入代码，也不会写入数据库；用户端的 `/v1/telemetry` 上报接口不需要口令。

如果没有设置 `ADMIN_TOKEN`，统计 API 会保持关闭状态。

## 验证接口

部署后，用 PowerShell 执行：

```powershell
$body = @{ installId = "11111111-1111-4111-8111-111111111111"; event = "install"; version = "3.2.0" } | ConvertTo-Json
Invoke-WebRequest `
  -Uri "https://clevo-usage-api.yycc1936.workers.dev/v1/telemetry" `
  -Method Post `
  -ContentType "application/json" `
  -Body $body `
  -SkipHttpErrorCheck
```

正常结果是 HTTP `204`。然后在 D1 控制台执行：

```sql
SELECT * FROM installations;
SELECT * FROM daily_activity;
```

只应看到随机安装 ID、版本号和时间字段，不会写入 IP、用户名、硬件信息、歌曲名或进程名。
