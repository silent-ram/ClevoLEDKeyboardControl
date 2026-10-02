// 腾讯云函数（Web 函数，Node.js 18+）：参与改进计划的国内数据入口 + COS 落库。
//
// 背景：原方案数据落在 Cloudflare D1，但 workers.dev 在中国大陆被阻断（家宽与云厂商
// 公网出口均不可达），国内用户的 install/heartbeat 全部丢失。本函数在腾讯云上直接
// 接收上报并写入同区域的 COS，形成完全国内闭环；海外用户经公网访问本 URL 亦可上报。
//
// 路由：
//   POST /v1/telemetry   上报 {installId, event, version}，幂等覆盖，成功返回 204
//   GET  /admin/api/summary  携带 Authorization: Bearer <ADMIN_TOKEN>，返回汇总 JSON
//
// COS 对象布局：
//   telemetry/{installId}.json      主档：{installId, firstSeen, lastSeen, version}
//                                   一个设备一个对象，总安装数 = 对象数，
//                                   活跃统计 = 对象 LastModified
//   daily/{yyyy-mm-dd}/{installId}.json   日活：同日同设备幂等覆盖
//
// 环境变量：COS_BUCKET / COS_REGION / COS_SECRET_ID / COS_SECRET_KEY / ADMIN_TOKEN

const http = require("node:http");
const crypto = require("node:crypto");

const PORT = Number(process.env.PORT || 9000);
const COS_BUCKET = process.env.COS_BUCKET || "";
const COS_REGION = process.env.COS_REGION || "";
const COS_SECRET_ID = process.env.COS_SECRET_ID || "";
const COS_SECRET_KEY = process.env.COS_SECRET_KEY || "";
const ADMIN_TOKEN = process.env.ADMIN_TOKEN || "";

const ALLOWED_EVENTS = new Set(["install", "heartbeat", "version"]);
const INSTALL_ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const VERSION_PATTERN = /^[0-9A-Za-z][0-9A-Za-z.+-]{0,31}$/;
const MAX_BODY_BYTES = 1024;
const MAX_REQUESTS_PER_MINUTE = 10;
const DAILY_TREND_DAYS = 30;
const recentRequests = new Map();

// 全局双层限速(防恶意刷量产生费用):函数 URL 公开,匿名接口无法验证调用方身份,
// 用"费用硬顶"代替身份验证——
//   全局分钟限速 300:防瞬时高频打爆单实例(真实峰值约为开机潮的每分钟几十次);
//   全局日累计 2000:费用硬顶,即使全天被攻击,月账单也在几元以内(真实日量约 600,余量 3 倍)。
// 超限一律 429 秒拒。两个计数器在函数实例重启时清零,部署/发布新版即重置。
const GLOBAL_MAX_PER_MINUTE = 300;
const GLOBAL_MAX_PER_DAY = 2000;
let globalMinuteStart = Date.now();
let globalMinuteCount = 0;
let globalDayKey = new Date().toISOString().slice(0, 10);
let globalDayCount = 0;

function isGloballyRateLimited(nowMs) {
  if (nowMs - globalMinuteStart >= 60_000) {
    globalMinuteStart = nowMs;
    globalMinuteCount = 0;
  }
  const dayKey = new Date(nowMs).toISOString().slice(0, 10);
  if (dayKey !== globalDayKey) {
    globalDayKey = dayKey;
    globalDayCount = 0;
  }
  globalMinuteCount += 1;
  globalDayCount += 1;
  return globalMinuteCount > GLOBAL_MAX_PER_MINUTE || globalDayCount > GLOBAL_MAX_PER_DAY;
}

// 统计后台页面：浏览器访问 /admin，输入 ADMIN_TOKEN 查看。口令仅存于当前页面内存。
const ADMIN_HTML = String.raw`<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>ClevoLEDKeyboardControl · 用户改进计划</title>
  <style>
    :root { color-scheme: dark; font-family: system-ui, -apple-system, "Segoe UI", sans-serif; background: #10131a; color: #edf2f7; }
    * { box-sizing: border-box; }
    body { margin: 0; min-height: 100vh; background: radial-gradient(circle at 10% 0%, #1d2c48 0, #10131a 42rem); }
    main { width: min(1100px, calc(100% - 32px)); margin: 0 auto; padding: 40px 0 56px; }
    h1 { margin: 0; font-size: clamp(26px, 4vw, 38px); }
    p { color: #aebbd0; }
    .subtle { color: #8d9ab0; font-size: 13px; }
    .card, .panel { border: 1px solid #2a3548; background: rgba(23, 30, 43, .9); border-radius: 16px; box-shadow: 0 16px 50px rgba(0,0,0,.18); }
    .card { padding: 20px; }
    .panel { margin-top: 18px; padding: 22px; }
    header { display: flex; justify-content: space-between; gap: 20px; align-items: flex-start; margin-bottom: 24px; }
    button { border: 0; border-radius: 9px; padding: 10px 16px; color: white; background: #367cf4; font-weight: 600; cursor: pointer; }
    button:hover { background: #4b8cff; }
    input { width: min(440px, 100%); border: 1px solid #3a465b; border-radius: 9px; padding: 11px 12px; color: white; background: #0e131c; }
    .login { max-width: 560px; margin: 54px auto; padding: 28px; }
    .login-row { display: flex; flex-wrap: wrap; gap: 10px; margin-top: 18px; }
    .error { color: #ff8e8e; margin-top: 12px; min-height: 20px; }
    .hidden { display: none !important; }
    .metrics { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 14px; }
    .metric-label { color: #aebbd0; font-size: 13px; }
    .metric-value { margin-top: 8px; font-size: 32px; font-weight: 700; }
    .section-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-bottom: 16px; }
    .bars { display: flex; align-items: flex-end; gap: 5px; height: 180px; padding: 12px 0 24px; border-bottom: 1px solid #2a3548; }
    .bar-wrap { flex: 1; min-width: 5px; height: 100%; display: flex; flex-direction: column; justify-content: flex-end; align-items: center; gap: 4px; }
    .bar { width: 100%; min-height: 2px; border-radius: 4px 4px 0 0; background: linear-gradient(180deg, #64a0ff, #367cf4); }
    .bar-label { font-size: 10px; color: #8290a7; white-space: nowrap; transform: rotate(-45deg) translate(-7px, 7px); }
    .empty { color: #8d9ab0; padding: 20px 0; }
    @media (max-width: 760px) { .metrics { grid-template-columns: repeat(2, minmax(0, 1fr)); } header { flex-direction: column; } }
  </style>
</head>
<body>
  <main>
    <section id="login" class="card login">
      <h1>用户改进计划</h1>
      <p>请输入管理员口令查看汇总数据。口令只在当前页面内存中使用，不会保存。</p>
      <div class="login-row"><input id="token" type="password" autocomplete="off" placeholder="管理员口令"><button id="loginButton">进入后台</button></div>
      <div id="loginError" class="error"></div>
    </section>
    <section id="dashboard" class="hidden">
      <header><div><h1>用户改进计划统计</h1><p class="subtle">只显示设备汇总，不显示安装 ID 或其他个人信息。</p></div><button id="refreshButton">刷新数据</button></header>
      <div class="metrics">
        <div class="card"><div class="metric-label">总安装设备</div><div id="total" class="metric-value">—</div></div>
        <div class="card"><div class="metric-label">最近 1 天活跃</div><div id="active1d" class="metric-value">—</div></div>
        <div class="card"><div class="metric-label">最近 7 天活跃</div><div id="active7d" class="metric-value">—</div></div>
        <div class="card"><div class="metric-label">最近 30 天活跃</div><div id="active30d" class="metric-value">—</div></div>
      </div>
      <section class="panel"><div class="section-head"><h2>每日活跃趋势</h2><span id="updated" class="subtle"></span></div><div id="chart"></div></section>
    </section>
  </main>
  <script>
    let adminToken = "";
    var byId = function (id) { return document.getElementById(id); };
    var number = function (value) { return Number(value || 0).toLocaleString("zh-CN"); };
    function showError(message) { byId("loginError").textContent = message || ""; }
    async function loadSummary() {
      var response = await fetch("/admin/api/summary", { headers: { Authorization: "Bearer " + adminToken }, cache: "no-store" });
      if (response.status === 401) throw new Error("管理员口令不正确");
      if (!response.ok) throw new Error("后台暂时不可用，请稍后重试");
      return response.json();
    }
    function render(data) {
      byId("total").textContent = number(data.totalInstallations);
      byId("active1d").textContent = number(data.active1d);
      byId("active7d").textContent = number(data.active7d);
      byId("active30d").textContent = number(data.active30d);
      byId("updated").textContent = "更新于 " + new Date(data.generatedAt).toLocaleString("zh-CN");
      var daily = data.daily || [];
      if (!daily.length) { byId("chart").innerHTML = "<div class='empty'>暂时没有每日活跃数据</div>"; return; }
      var max = Math.max.apply(null, daily.map(function (item) { return Number(item.active_devices || 0); }).concat([1]));
      var bars = daily.map(function (item) {
        var value = Number(item.active_devices || 0);
        var height = Math.max(2, Math.round(value / max * 145));
        return "<div class='bar-wrap' title='" + item.activity_date + "：" + number(value) + " 台'><div class='bar' style='height:" + height + "px'></div><div class='bar-label'>" + item.activity_date.slice(5) + "</div></div>";
      }).join("");
      byId("chart").innerHTML = "<div class='bars'>" + bars + "</div>";
    }
    async function enter() {
      var value = byId("token").value.trim();
      if (!value) { showError("请输入管理员口令"); return; }
      adminToken = value;
      try { render(await loadSummary()); byId("login").classList.add("hidden"); byId("dashboard").classList.remove("hidden"); showError(""); }
      catch (error) { adminToken = ""; showError(error.message); }
    }
    byId("loginButton").addEventListener("click", enter);
    byId("token").addEventListener("keydown", function (event) { if (event.key === "Enter") enter(); });
    byId("refreshButton").addEventListener("click", async function () { try { render(await loadSummary()); } catch (error) { alert(error.message); } });
  </script>
</body>
</html>`;

const cosConfigured = () => Boolean(COS_BUCKET && COS_REGION && COS_SECRET_ID && COS_SECRET_KEY);
const cosHost = () => `${COS_BUCKET}.cos.${COS_REGION}.myqcloud.com`;

function hmacSha1Hex(key, value) {
  return crypto.createHmac("sha1", key).update(value).digest("hex");
}

function sha1Hex(value) {
  return crypto.createHash("sha1").update(value).digest("hex");
}

// COS 签名规范要求的 URL 编码：除 [A-Za-z0-9-_.~] 外全部转义。
function cosEncode(value) {
  return encodeURIComponent(value)
    .replace(/!/g, "%21").replace(/'/g, "%27")
    .replace(/\(/g, "%28").replace(/\)/g, "%29").replace(/\*/g, "%2A");
}

// COS XML API 签名（q-sign-algorithm=sha1），不依赖任何第三方 SDK。
function cosAuthorization(method, pathname, params = {}) {
  const now = Math.floor(Date.now() / 1000);
  const keyTime = `${now - 60};${now + 600}`;
  const signKey = hmacSha1Hex(COS_SECRET_KEY, keyTime);

  const paramKeys = Object.keys(params).sort();
  const paramStr = paramKeys.map((key) => `${key}=${cosEncode(params[key])}`).join("&");
  const paramList = paramKeys.join(";");

  // 不参与签名的业务头（content-type / content-length）不入 headerList，保持签名最简。
  const httpString = `${method.toLowerCase()}\n${pathname.toLowerCase()}\n${paramStr}\n\n`;
  const stringToSign = `sha1\n${keyTime}\n${sha1Hex(httpString)}\n`;
  const signature = hmacSha1Hex(signKey, stringToSign);

  return `q-sign-algorithm=sha1&q-ak=${COS_SECRET_ID}&q-sign-time=${keyTime}` +
    `&q-key-time=${keyTime}&q-header-list=&q-url-param-list=${paramList}&q-signature=${signature}`;
}

async function cosRequest(method, pathname, params, body) {
  // 空值参数不参与 URL 与签名：COS 服务端对空值参数的规范化与本地计算易不一致，曾导致 LIST 403。
  const usedParams = {};
  for (const [key, value] of Object.entries(params)) {
    if (value !== "" && value !== undefined && value !== null) usedParams[key] = value;
  }
  const query = Object.keys(usedParams).length
    ? "?" + Object.keys(usedParams).sort().map((key) => `${key}=${cosEncode(usedParams[key])}`).join("&")
    : "";
  const response = await fetch(`https://${cosHost()}${pathname}${query}`, {
    method,
    headers: { authorization: cosAuthorization(method, pathname, usedParams) },
    body,
  });
  if (!response.ok) {
    const detail = await response.text().catch(() => "");
    throw new Error(`COS ${method} ${pathname} -> ${response.status} ${detail.slice(0, 300)}`);
  }
  return response;
}

async function cosPutObject(key, body) {
  await cosRequest("PUT", `/${key}`, {}, body);
}

async function cosGetObject(key) {
  const response = await cosRequest("GET", `/${key}`, {});
  if (response.status === 404) return null;
  return response.text();
}

// 列举对象，返回 [{key, lastModifiedMs}]；COS 返回 XML，这里按固定结构提取。
async function cosList(prefix, marker = "") {
  const params = { "max-keys": "1000", prefix };
  if (marker) params.marker = marker;
  const response = await cosRequest("GET", "/", params, undefined);
  const xml = await response.text();
  const contents = [...xml.matchAll(/<Contents>[\s\S]*?<\/Contents>/g)].map((match) => {
    const key = /<Key>([^<]+)<\/Key>/.exec(match[0])?.[1] ?? "";
    const lastModified = /<LastModified>([^<]+)<\/LastModified>/.exec(match[0])?.[1] ?? "";
    return { key, lastModifiedMs: Date.parse(lastModified) || 0 };
  });
  const truncated = /<IsTruncated>true<\/IsTruncated>/.test(xml);
  const nextMarker = /<NextMarker>([^<]+)<\/NextMarker>/.exec(xml)?.[1] ?? "";
  return { contents, truncated, nextMarker };
}

async function listAll(prefix) {
  const all = [];
  let marker = "";
  for (let page = 0; page < 200; page += 1) {
    const { contents, truncated, nextMarker } = await cosList(prefix, marker);
    all.push(...contents);
    if (!truncated || !nextMarker) break;
    marker = nextMarker;
  }
  return all;
}

function isRateLimited(installId, nowMs) {
  const previous = recentRequests.get(installId);
  if (!previous || nowMs - previous.windowStart >= 60_000) {
    recentRequests.set(installId, { windowStart: nowMs, count: 1 });
    if (recentRequests.size > 2048) {
      for (const [key, value] of recentRequests) {
        if (nowMs - value.windowStart >= 60_000) recentRequests.delete(key);
      }
    }
    return false;
  }
  previous.count += 1;
  return previous.count > MAX_REQUESTS_PER_MINUTE;
}

function isValidPayload(payload) {
  return payload &&
    typeof payload === "object" &&
    typeof payload.installId === "string" &&
    INSTALL_ID_PATTERN.test(payload.installId) &&
    typeof payload.event === "string" &&
    ALLOWED_EVENTS.has(payload.event) &&
    typeof payload.version === "string" &&
    VERSION_PATTERN.test(payload.version);
}

async function handleTelemetry(req, res) {
  if (!cosConfigured()) {
    res.statusCode = 503;
    return res.end();
  }

  const chunks = [];
  let size = 0;
  for await (const chunk of req) {
    size += chunk.length;
    if (size > MAX_BODY_BYTES) {
      res.statusCode = 413;
      return res.end();
    }
    chunks.push(chunk);
  }

  let payload;
  try {
    payload = JSON.parse(Buffer.concat(chunks).toString("utf8"));
  } catch {
    res.statusCode = 400;
    return res.end();
  }
  if (!isValidPayload(payload)) {
    res.statusCode = 400;
    return res.end();
  }

  const installId = payload.installId.toLowerCase();
  if (isRateLimited(installId, Date.now())) {
    res.statusCode = 429;
    res.setHeader("retry-after", "60");
    return res.end();
  }

  const now = new Date();
  // 日活按东八区切日：与国内用户的实际"一天"对齐，避免凌晨上报被记到前一天的趋势里。
  const day = new Date(now.getTime() + 8 * 3_600_000).toISOString().slice(0, 10);
  let firstSeen = now.toISOString();
  try {
    const previous = await cosGetObject(`telemetry-${payload.version}-${installId}.json`);
    if (previous) {
      const parsed = JSON.parse(previous);
      if (typeof parsed.firstSeen === "string") firstSeen = parsed.firstSeen;
    }
  } catch {
    // 读取旧档失败不阻塞上报：firstSeen 退化为本次时间。
  }

  const record = JSON.stringify({
    installId,
    firstSeen,
    lastSeen: now.toISOString(),
    version: payload.version,
  });

  try {
    // key 含版本号：纯解析对象名即可得到版本分布，无需逐个读取内容。
    // 同一设备升级后会留下多个版本的 key，统计端按 installId 去重、取最新上报。
    await cosPutObject(`telemetry-${payload.version}-${installId}.json`, record);
    await cosPutObject(`daily-${day}-${installId}.json`, JSON.stringify({ installId, version: payload.version }));
    res.statusCode = 204;
    res.end();
  } catch (error) {
    console.error("[cos] write failed:", error instanceof Error ? error.message : error);
    res.statusCode = 503;
    res.end();
  }
}

function isAdminAuthorized(req) {
  if (!ADMIN_TOKEN) return false;
  const authorization = req.headers.authorization || "";
  const expected = "Bearer " + ADMIN_TOKEN;
  if (authorization.length !== expected.length) return false;
  let difference = 0;
  for (let index = 0; index < expected.length; index += 1) {
    difference |= authorization.charCodeAt(index) ^ expected.charCodeAt(index);
  }
  return difference === 0;
}

async function handleSummary(res) {
  if (!ADMIN_TOKEN || !cosConfigured()) {
    res.statusCode = 503;
    return res.end();
  }

  try {
    const now = Date.now();
    // 并行发出:主档列举 + 30 天每日列举,总耗时 ≈ 最慢的一个请求,而不是 31 个相加。
    // 单天列举失败把该日记 0,不影响其余天数(allSettled 语义)。
    const trendPromises = [];
    for (let offset = DAILY_TREND_DAYS - 1; offset >= 0; offset -= 1) {
      const date = new Date(now - offset * 86_400_000).toISOString().slice(0, 10);
      trendPromises.push(
        listAll(`daily-${date}-`)
          .then((objects) => ({ activity_date: date, active_devices: objects.length }))
          .catch((error) => {
            console.error(`[summary] list ${date} failed:`, error instanceof Error ? error.message : error);
            return { activity_date: date, active_devices: 0 };
          })
      );
    }
    const [devices, ...trend] = await Promise.all([listAll("telemetry-"), ...trendPromises]);

    // 解析 key:telemetry-{version}-{installId}.json;installId 是固定 5 段的 UUID,
    // 从尾部取 5 段还原 installId,余下部分为版本号(旧格式无版本段记为 unknown)。
    const byInstallId = new Map();
    for (const item of devices) {
      const rest = item.key.slice("telemetry-".length, item.key.length - ".json".length);
      const parts = rest.split("-");
      if (parts.length < 5) continue;
      const installId = parts.slice(-5).join("-");
      const version = parts.length > 5 ? parts.slice(0, -5).join("-") : "unknown";
      const current = byInstallId.get(installId);
      if (!current || item.lastModifiedMs > current.lastModifiedMs) {
        byInstallId.set(installId, { lastModifiedMs: item.lastModifiedMs, version });
      }
    }

    const within = (days) => {
      let count = 0;
      for (const device of byInstallId.values()) {
        if (now - device.lastModifiedMs <= days * 86_400_000) count += 1;
      }
      return count;
    };

    const versionCounts = new Map();
    for (const device of byInstallId.values()) {
      versionCounts.set(device.version, (versionCounts.get(device.version) || 0) + 1);
    }
    const versions = [...versionCounts.entries()]
      .map(([version, device_count]) => ({ version, device_count }))
      .sort((left, right) => right.device_count - left.device_count);

    const summary = {
      generatedAt: new Date().toISOString(),
      totalInstallations: byInstallId.size,
      active1d: within(1),
      active7d: within(7),
      active30d: within(30),
      versions,
      daily: trend,
    };

    res.statusCode = 200;
    res.setHeader("content-type", "application/json; charset=utf-8");
    res.end(JSON.stringify(summary));
  } catch (error) {
    // 把失败原因带回响应体，便于远程定位（签名/网络/权限）。
    const detail = error instanceof Error ? error.message : String(error);
    console.error("[summary] failed:", detail);
    res.statusCode = 503;
    res.setHeader("content-type", "application/json; charset=utf-8");
    res.end(JSON.stringify({ error: "summary_failed", detail }));
  }
}

const server = http.createServer(async (req, res) => {
  const pathname = (req.url || "/").split("?")[0];
  try {
    // 全局限速在最前:超限 429 秒拒,恶意流量不进入 COS 读写路径。
    if (isGloballyRateLimited(Date.now())) {
      res.statusCode = 429;
      res.setHeader("retry-after", "60");
      return res.end();
    }
    if (pathname === "/admin" && (req.method === "GET" || req.method === "HEAD")) {
      // SCF 网关会给响应强加 Content-Disposition: attachment，导致浏览器把页面当文件下载；
      // 显式声明 inline 予以覆盖。
      res.statusCode = 200;
      res.setHeader("content-type", "text/html; charset=utf-8");
      res.setHeader("cache-control", "no-store");
      res.setHeader("content-disposition", "inline");
      res.setHeader("content-security-policy", "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self'");
      res.setHeader("x-content-type-options", "nosniff");
      return res.end(req.method === "HEAD" ? undefined : ADMIN_HTML);
    }
    if (pathname === "/v1/telemetry" && req.method === "POST") return await handleTelemetry(req, res);
    if (pathname === "/admin/api/summary" && req.method === "GET") {
      if (!isAdminAuthorized(req)) {
        res.statusCode = 401;
        return res.end();
      }
      return await handleSummary(res);
    }
    res.statusCode = 404;
    res.end();
  } catch (error) {
    console.error("[http] unhandled:", error instanceof Error ? error.message : error);
    if (!res.headersSent) {
      res.statusCode = 503;
      res.end();
    }
  }
});

server.listen(PORT, () => {
  console.log(`[relay-cos] listening on ${PORT}, bucket=${COS_BUCKET || "(unset)"}.${COS_REGION || "(unset)"}, admin=${ADMIN_TOKEN ? "on" : "off"}`);
});
