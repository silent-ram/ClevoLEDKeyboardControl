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
    h2 { margin: 0; font-size: 18px; }
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
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 18px; }
    .section-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-bottom: 16px; }
    table { width: 100%; border-collapse: collapse; }
    th, td { border-bottom: 1px solid #2a3548; padding: 10px 4px; text-align: left; font-size: 14px; }
    th:last-child, td:last-child { text-align: right; }
    .bars { display: flex; align-items: flex-end; gap: 5px; height: 180px; padding: 12px 0 24px; border-bottom: 1px solid #2a3548; }
    .bar-wrap { flex: 1; min-width: 5px; height: 100%; display: flex; flex-direction: column; justify-content: flex-end; align-items: center; gap: 4px; }
    .bar { width: 100%; min-height: 2px; border-radius: 4px 4px 0 0; background: linear-gradient(180deg, #64a0ff, #367cf4); }
    .bar-label { font-size: 10px; color: #8290a7; white-space: nowrap; transform: rotate(-45deg) translate(-7px, 7px); }
    .empty { color: #8d9ab0; padding: 20px 0; }
    @media (max-width: 760px) { .metrics { grid-template-columns: repeat(2, 1fr); } .grid { grid-template-columns: 1fr; } header { flex-direction: column; } }
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
      <div class="grid">
        <section class="panel"><div class="section-head"><h2>版本分布</h2></div><div id="versions"></div></section>
        <section class="panel"><div class="section-head"><h2>每日活跃趋势</h2><span id="updated" class="subtle"></span></div><div id="chart"></div></section>
      </div>
    </section>
  </main>
  <script>
    let adminToken = "";
    const $ = (id) => document.getElementById(id);
    const number = (value) => Number(value || 0).toLocaleString("zh-CN");
    function showError(message) { $("loginError").textContent = message || ""; }
    async function loadSummary() {
      const response = await fetch("/admin/api/summary", { headers: { Authorization: "Bearer " + adminToken }, cache: "no-store" });
      if (response.status === 401) throw new Error("管理员口令不正确");
      if (!response.ok) throw new Error("后台暂时不可用，请稍后重试");
      return response.json();
    }
    function render(data) {
      $("total").textContent = number(data.totalInstallations);
      $("active1d").textContent = number(data.active1d);
      $("active7d").textContent = number(data.active7d);
      $("active30d").textContent = number(data.active30d);
      $("updated").textContent = "更新于 " + new Date(data.generatedAt).toLocaleString("zh-CN");
      const versions = data.versions || [];
      $("versions").innerHTML = versions.length ? "<table><thead><tr><th>版本</th><th>设备数</th></tr></thead><tbody>" + versions.map((item) => "<tr><td>" + item.version + "</td><td>" + number(item.device_count) + "</td></tr>").join("") + "</tbody></table>" : "<div class='empty'>暂时没有数据</div>";
      const daily = data.daily || [];
      if (!daily.length) { $("chart").innerHTML = "<div class='empty'>暂时没有每日活跃数据</div>"; return; }
      const max = Math.max(...daily.map((item) => Number(item.active_devices || 0)), 1);
      $("chart").innerHTML = "<div class='bars'>" + daily.map((item) => { const value = Number(item.active_devices || 0); const height = Math.max(2, Math.round(value / max * 145)); return "<div class='bar-wrap' title='" + item.activity_date + "：" + number(value) + " 台'><div class='bar' style='height:" + height + "px'></div><div class='bar-label'>" + item.activity_date.slice(5) + "</div></div>"; }).join("") + "</div>";
    }
    async function enter() {
      const value = $("token").value.trim();
      if (!value) { showError("请输入管理员口令"); return; }
      adminToken = value;
      try { render(await loadSummary()); $("login").classList.add("hidden"); $("dashboard").classList.remove("hidden"); showError(""); }
      catch (error) { adminToken = ""; showError(error.message); }
    }
    $("loginButton").addEventListener("click", enter);
    $("token").addEventListener("keydown", (event) => { if (event.key === "Enter") enter(); });
    $("refreshButton").addEventListener("click", async () => { try { render(await loadSummary()); } catch (error) { alert(error.message); } });
  </script>
</body>
</html>`;

const ALLOWED_EVENTS = new Set(["install", "heartbeat", "version"]);
const INSTALL_ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const VERSION_PATTERN = /^[0-9A-Za-z][0-9A-Za-z.+-]{0,31}$/;
const MAX_BODY_BYTES = 1024;
const MAX_REQUESTS_PER_MINUTE = 10;
const recentRequests = new Map();

function json(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "no-store",
    },
  });
}

function noContent() {
  return new Response(null, {
    status: 204,
    headers: { "cache-control": "no-store" },
  });
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

function isAdminAuthorized(request, env) {
  const configuredToken = typeof env.ADMIN_TOKEN === "string" ? env.ADMIN_TOKEN.trim() : "";
  if (!configuredToken) return false;

  const authorization = request.headers.get("authorization") || "";
  const prefix = "Bearer ";
  if (!authorization.startsWith(prefix)) return false;
  const suppliedToken = authorization.slice(prefix.length).trim();
  return safeEqual(suppliedToken, configuredToken);
}

function safeEqual(left, right) {
  if (left.length !== right.length) return false;
  let difference = 0;
  for (let index = 0; index < left.length; index += 1) {
    difference |= left.charCodeAt(index) ^ right.charCodeAt(index);
  }
  return difference === 0;
}

async function getAdminSummary(env) {
  const summary = await env.DB.prepare(`
    SELECT
      COUNT(*) AS total_installations,
      COALESCE(SUM(CASE WHEN julianday(last_seen) >= julianday('now', '-1 day') THEN 1 ELSE 0 END), 0) AS active_1d,
      COALESCE(SUM(CASE WHEN julianday(last_seen) >= julianday('now', '-7 days') THEN 1 ELSE 0 END), 0) AS active_7d,
      COALESCE(SUM(CASE WHEN julianday(last_seen) >= julianday('now', '-30 days') THEN 1 ELSE 0 END), 0) AS active_30d
    FROM installations
  `).first();

  const versions = await env.DB.prepare(`
    SELECT current_version AS version, COUNT(*) AS device_count
    FROM installations
    GROUP BY current_version
    ORDER BY device_count DESC, version DESC
  `).all();

  const daily = await env.DB.prepare(`
    SELECT activity_date, COUNT(*) AS active_devices
    FROM daily_activity
    GROUP BY activity_date
    ORDER BY activity_date DESC
    LIMIT 30
  `).all();

  return {
    generatedAt: new Date().toISOString(),
    totalInstallations: Number(summary?.total_installations || 0),
    active1d: Number(summary?.active_1d || 0),
    active7d: Number(summary?.active_7d || 0),
    active30d: Number(summary?.active_30d || 0),
    versions: versions.results || [],
    daily: (daily.results || []).reverse(),
  };
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === "/admin" && request.method === "GET") {
      return new Response(ADMIN_HTML, {
        status: 200,
        headers: {
          "content-type": "text/html; charset=utf-8",
          "cache-control": "no-store",
          "content-security-policy": "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self'",
          "x-content-type-options": "nosniff",
          "referrer-policy": "no-referrer",
        },
      });
    }

    if (url.pathname === "/admin/api/summary") {
      if (request.method !== "GET") {
        return new Response(null, {
          status: 405,
          headers: { allow: "GET", "cache-control": "no-store" },
        });
      }
      if (!env.DB) return json({ error: "database_unavailable" }, 503);
      if (!isAdminAuthorized(request, env)) {
        return json({ error: "unauthorized" }, 401);
      }

      try {
        return json(await getAdminSummary(env), 200);
      } catch (error) {
        console.error("admin summary failed", error instanceof Error ? error.message : "unknown error");
        return json({ error: "temporary_failure" }, 503);
      }
    }

    if (url.pathname !== "/v1/telemetry") {
      return json({ error: "not_found" }, 404);
    }
    if (request.method !== "POST") {
      return new Response(null, {
        status: 405,
        headers: { allow: "POST", "cache-control": "no-store" },
      });
    }
    if (!env.DB) {
      return json({ error: "database_unavailable" }, 503);
    }

    const contentLength = Number(request.headers.get("content-length") || 0);
    if (Number.isFinite(contentLength) && contentLength > MAX_BODY_BYTES) {
      return json({ error: "payload_too_large" }, 413);
    }

    let payload;
    try {
      const body = await request.text();
      if (new TextEncoder().encode(body).byteLength > MAX_BODY_BYTES) {
        return json({ error: "payload_too_large" }, 413);
      }
      payload = JSON.parse(body);
    } catch {
      return json({ error: "invalid_json" }, 400);
    }

    if (!isValidPayload(payload)) {
      return json({ error: "invalid_payload" }, 400);
    }

    const installId = payload.installId.toLowerCase();
    if (isRateLimited(installId, Date.now())) {
      return new Response(null, {
        status: 429,
        headers: { "cache-control": "no-store", "retry-after": "60" },
      });
    }
    const seenAt = new Date().toISOString();
    const activityDate = seenAt.slice(0, 10);

    try {
      const installation = env.DB.prepare(`
        INSERT INTO installations (install_id, first_seen, last_seen, current_version)
        VALUES (?, ?, ?, ?)
        ON CONFLICT(install_id) DO UPDATE SET
          last_seen = excluded.last_seen,
          current_version = excluded.current_version
      `).bind(installId, seenAt, seenAt, payload.version);

      const activity = env.DB.prepare(`
        INSERT OR IGNORE INTO daily_activity (activity_date, install_id)
        VALUES (?, ?)
      `).bind(activityDate, installId);

      await env.DB.batch([installation, activity]);
      return noContent();
    } catch (error) {
      console.error("telemetry write failed", error instanceof Error ? error.message : "unknown error");
      return json({ error: "temporary_failure" }, 503);
    }
  },
};
