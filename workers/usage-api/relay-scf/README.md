# 参与改进计划 · 国内数据入口（腾讯云函数 SCF + COS）

## 背景

原方案把心跳写到 Cloudflare D1（`workers.dev`），但该域名在中国大陆被阻断
（家宽与云厂商公网出口均不可达），国内用户的 install/heartbeat 全部丢失，
统计严重偏低。

现方案改为**完全国内闭环**：腾讯云函数直接接收上报并写入同区域 COS。
国内外用户统一走这一个入口，单一数据源，无需合并。

```
客户端(每设备每天 1~2 次 POST) ──► SCF Web 函数 ──► COS 桶(扁平 key,无目录层级)
                                                  ├─ telemetry-{installId}.json           主档(总安装/活跃)
                                                  └─ daily-{yyyy-mm-dd}-{installId}.json  日活(趋势)
```

> key 采用扁平化命名（不含斜杠）：COS 列举接口的 prefix 参数值含斜杠时，
> 手写签名的 URL 编码与服务端规范化存在差异风险，扁平化从根上规避。

- 幂等：文件按 installId 覆盖写，重复上报不会虚增统计
- 校验：installId UUID 格式、event 白名单、version 格式、单设备 10 次/分钟限速，
  与原 Worker 逻辑一致
- 成功返回 204；客户端（`UsageTelemetryClient.cs`）支持多端点 fallback、204 才算成功

## 部署

### 1. COS（免费额度 6 个月，之后按量计费，本场景每月不到 1 元）

- 创建存储桶：地域**广州**（与函数同区域）、访问权限**私有读写**、
  数据冗余策略**单 AZ 存储**（多 AZ 更贵且无法关闭，统计场景不需要）
- 桶的高级功能全部保持默认（版本控制/加密/日志均不开）

### 2. 云函数 SCF（Web 函数，Node.js 18+）

- 上传 `relay-scf.zip`（或在在线编辑器里用 `index.js` 覆盖 `app.js`）
- 监听端口 9000；执行超时建议 10 秒；健康检查不启用；日志投递不启用
- 环境变量（函数配置 → 编辑）：

| 键 | 值 |
|---|---|
| `COS_BUCKET` | 桶全名，如 `clevo-usage-data-1234567890` |
| `COS_REGION` | `ap-guangzhou` |
| `COS_SECRET_ID` | API 密钥 SecretId（访问管理 → API 密钥管理） |
| `COS_SECRET_KEY` | API 密钥 SecretKey |
| `ADMIN_TOKEN` | 自定义管理口令（查看统计用，勿用弱口令） |

⚠️ 密钥只保存在函数环境变量里，**不要提交进 git**；泄露后在访问管理里禁用换新即可。

### 3. 开启公网

函数 URL → 编辑 → 公网访问启用、授权类型开放。URL 长期有效，无需备案域名。

## 查看统计

```bash
curl -H "Authorization: Bearer <ADMIN_TOKEN>" \
  "https://<函数URL>/admin/api/summary"
```

返回 JSON：总安装、1d/7d/30d 活跃、最近 30 天每日活跃。

## 运维

- **`daily-` 前缀建议配置生命周期规则**（桶 → 基础配置 → 生命周期）：
  90 天自动删除，控制趋势数据的存储成本；`telemetry-` 主档永不删除。
- 免费额度：SCF 每月 100 万次调用；本场景每设备每天 1~2 次。
- 云函数代码与 Cloudflare Worker（`src/index.js`）互为替代：Worker 保留在仓库中备查，
  已由本方案取代。
