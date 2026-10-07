/**
 * Sonverter 出口节点转换工具 - 现代化前端交互逻辑
 */

// ============ 现代化通知 Toast ============

function showToast(message, type = 'success') {
  const container = document.getElementById('toastContainer');
  if (!container) return;

  const toast = document.createElement('div');
  toast.className = 'toast-msg';
  const icon = type === 'success' ? '✅' : (type === 'error' ? '❌' : 'ℹ️');
  toast.innerHTML = `<span>${icon}</span><span>${message}</span>`;
  container.appendChild(toast);

  requestAnimationFrame(() => {
    toast.classList.add('show');
  });

  setTimeout(() => {
    toast.classList.remove('show');
    setTimeout(() => toast.remove(), 300);
  }, 2500);
}

// ============ 主题切换 ============

const themeBtn = document.getElementById('themeBtn');

function applyTheme(theme) {
  if (theme === 'dark') {
    document.documentElement.setAttribute('data-theme', 'dark');
    if (themeBtn) themeBtn.textContent = '☀️';
  } else {
    document.documentElement.removeAttribute('data-theme');
    if (themeBtn) themeBtn.textContent = '🌙';
  }
}

function initTheme() {
  const saved = localStorage.getItem('sonverter-theme');
  const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
  applyTheme(saved || (prefersDark ? 'dark' : 'light'));
}

if (themeBtn) {
  themeBtn.addEventListener('click', () => {
    const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
    const next = isDark ? 'light' : 'dark';
    applyTheme(next);
    localStorage.setItem('sonverter-theme', next);
  });
}

// ============ 状态管理 ============

const state = {};
const sidebar = document.getElementById('sidebar');

for (const view of document.querySelectorAll('.content-area > .card')) {
  const name = view.id.replace(/^view-/, '');
  const input = document.getElementById(`file-input-${name}`);
  state[name] = {
    file: null,
    data: null,
    lastResult: null,
    extensions: (input?.accept || '').split(',').map(s => s.trim().toLowerCase()),
  };
}

// ============ 辅助函数 ============

function formatFileSize(bytes) {
  if (bytes < 1024) return bytes + ' B';
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(2) + ' KB';
  return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
}

function getTimestamp() {
  return new Date().toISOString()
    .replace(/[:.]/g, '-')
    .replace(/T/, '_')
    .slice(0, -5);
}

function showError(name, message) {
  const el = document.getElementById(`error-${name}`);
  if (!el) return;
  el.textContent = message;
  el.classList.add('show');
  showToast(message, 'error');
  setTimeout(() => el.classList.remove('show'), 6000);
}

// ============ 侧边栏切换 ============

function switchTab(name) {
  if (!state[name]) return;

  for (const item of sidebar.querySelectorAll('.nav-item')) {
    item.classList.toggle('active', item.dataset.converter === name);
  }

  for (const converter of Object.keys(state)) {
    const view = document.getElementById(`view-${converter}`);
    if (view) view.style.display = converter === name ? 'block' : 'none';
  }
}

if (sidebar) {
  sidebar.addEventListener('click', (e) => {
    const item = e.target.closest('.nav-item');
    if (item) switchTab(item.dataset.converter);
  });
}

// ============ 文件选择与拖拽 ============

function handleFileSelect(name, file) {
  const s = state[name];
  if (!file) return;

  const ext = '.' + (file.name.split('.').pop() || '').toLowerCase();
  if (s.extensions.length > 0 && !s.extensions.includes(ext)) {
    showError(name, `文件格式错误：请选择 ${s.extensions.join(' / ')} 格式`);
    return;
  }

  // 清除 URL 输入框
  const urlInput = document.getElementById(`url-input-${name}`);
  if (urlInput) urlInput.value = '';
  const urlClear = document.getElementById(`url-clear-${name}`);
  if (urlClear) urlClear.style.display = 'none';

  s.file = file;
  document.getElementById(`file-name-${name}`).textContent = file.name;
  document.getElementById(`file-size-${name}`).textContent = formatFileSize(file.size);
  document.getElementById(`file-info-${name}`).style.display = 'flex';
  document.getElementById(`convert-${name}`).disabled = false;

  // 隐藏旧的错误提示和转换结果
  const errEl = document.getElementById(`error-${name}`);
  if (errEl) errEl.classList.remove('show');
  const resEl = document.getElementById(`result-${name}`);
  if (resEl) resEl.style.display = 'none';

  const reader = new FileReader();
  reader.onload = (e) => {
    const text = e.target.result;
    if (s.extensions.includes('.json')) {
      try {
        s.data = JSON.parse(text);
      } catch (err) {
        s.data = null;
        showError(name, 'JSON 文件解析失败：' + err.message);
        document.getElementById(`convert-${name}`).disabled = true;
      }
    } else {
      s.data = text.split(/\r?\n/);
    }
  };
  reader.readAsText(file, 'utf-8');
}

function resetConverter(name) {
  const s = state[name];
  s.file = null;
  s.data = null;
  s.lastResult = null;

  const input = document.getElementById(`file-input-${name}`);
  if (input) input.value = '';
  document.getElementById(`file-info-${name}`).style.display = 'none';
  document.getElementById(`result-${name}`).style.display = 'none';

  const urlInput = document.getElementById(`url-input-${name}`);
  const hasUrl = urlInput && Boolean(urlInput.value.trim());
  document.getElementById(`convert-${name}`).disabled = !hasUrl;
}

// ============ 转换逻辑 ============

async function doConvert(name) {
  const s = state[name];
  const urlInput = document.getElementById(`url-input-${name}`);
  const urlVal = urlInput ? urlInput.value.trim() : '';

  if (!s.data && !urlVal) {
    showError(name, '请先输入订阅链接 URL 或选择文件');
    return;
  }

  const btn = document.getElementById(`convert-${name}`);
  const originalBtnHtml = btn.innerHTML;

  try {
    btn.disabled = true;
    btn.innerHTML = '<span>转换中...</span>';

    const reqBody = { converter: name };
    if (urlVal) {
      reqBody.url = urlVal;
    } else {
      reqBody.data = s.data;
    }

    const response = await fetch('/api/convert', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(reqBody),
    });

    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.error || '后端返回异常');
    }

    const result = await response.json();
    if (!result.success) {
      throw new Error(result.error || '转换失败');
    }
    s.lastResult = result;

    const statusEl = document.getElementById(`status-${name}`);
    if (result.format === 'text') {
      // dae 文本模式
      const lines = result.data.split('\n').filter(line => line.trim().length > 0);
      const nodeCount = lines.filter(line => line.includes(': "')).length;
      if (statusEl) statusEl.textContent = `共 ${nodeCount} 个节点`;

      const code = document.getElementById(`code-${name}`);
      if (code) {
        code.textContent = result.data;
        code.style.display = 'block';
      }
    } else {
      // singbox 模式
      const merged = result.data;
      const statsData = getStats(merged);
      if (statusEl) statusEl.textContent = `共 ${statsData.totalNodes} 个节点`;

      // 渲染指标卡片
      renderMetrics(statsData);

      // 渲染地区分布 Pills
      renderCountryPills(statsData);

      // 渲染节点标签预览
      renderTagPreview(merged);
    }

    // 更新在线订阅直链
    const subBox = document.getElementById(`sub-box-${name}`);
    const subUrlInput = document.getElementById(`sub-url-${name}`);
    const subOpenBtn = document.getElementById(`sub-open-${name}`);
    if (subBox && subUrlInput && subOpenBtn) {
      if (urlVal) {
        const subUrl = `${window.location.origin}/sub?converter=${name}&url=${encodeURIComponent(urlVal)}`;
        subUrlInput.value = subUrl;
        subOpenBtn.href = subUrl;
        subBox.style.display = 'flex';
      } else {
        subBox.style.display = 'none';
      }
    }

    document.getElementById(`result-${name}`).style.display = 'block';
    document.getElementById(`result-${name}`).scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    showToast('转换成功', 'success');

  } catch (err) {
    showError(name, '转换处理失败：' + err.message);
    console.error(err);
  } finally {
    btn.disabled = false;
    btn.innerHTML = originalBtnHtml;
  }
}

/**
 * 统计信息提取 (singbox)
 */
function getStats(merged) {
  const GROUP_TYPES = ['selector', 'urltest', 'direct'];
  const outbounds = merged.outbounds || [];

  const nodes = outbounds.filter(o => o && !GROUP_TYPES.includes(o.type));
  const totalNodes = nodes.length;
  const groups = outbounds.filter(o => o && GROUP_TYPES.includes(o.type));

  const countryGroups = {};
  for (const g of groups) {
    if (g && g.tag && g.type === 'urltest') {
      const count = Array.isArray(g.outbounds) ? g.outbounds.length : 0;
      countryGroups[g.tag] = count;
    }
  }

  return {
    totalNodes,
    groupsCount: groups.length,
    countryGroups,
  };
}

/**
 * 渲染指标网格
 */
function renderMetrics(stats) {
  const container = document.getElementById('metrics-singbox');
  if (!container) return;

  const groupCount = Object.keys(stats.countryGroups).length;

  container.innerHTML = `
    <div class="metric-card">
      <div class="metric-label">节点总数</div>
      <div class="metric-value">${stats.totalNodes}</div>
    </div>
    <div class="metric-card">
      <div class="metric-label">测速/特征组</div>
      <div class="metric-value">${groupCount}</div>
    </div>
    <div class="metric-card">
      <div class="metric-label">策略组总数</div>
      <div class="metric-value">${stats.groupsCount}</div>
    </div>
  `;
}

/**
 * 渲染国家/地区标签网格
 */
function renderCountryPills(stats) {
  const container = document.getElementById('country-grid-singbox');
  if (!container) return;

  const sorted = Object.entries(stats.countryGroups).sort((a, b) => b[1] - a[1]);

  let html = '';
  for (const [groupName, count] of sorted) {
    html += `
      <div class="country-pill" title="${groupName}">
        <span>${groupName}</span>
        <span class="count">${count}</span>
      </div>
    `;
  }
  container.innerHTML = html;
}

/**
 * 渲染节点标签预览列表
 */
function renderTagPreview(merged) {
  const container = document.getElementById('tag-preview-singbox');
  if (!container) return;

  const GROUP_TYPES = ['selector', 'urltest', 'direct'];
  const nodes = (merged.outbounds || []).filter(o => o && !GROUP_TYPES.includes(o.type));

  let html = '';
  for (const node of nodes) {
    html += `
      <div class="tag-preview-item">
        <span>${node.tag}</span>
        <span class="tag-type-badge">${node.type || 'outbound'}</span>
      </div>
    `;
  }
  container.innerHTML = html;
}

// ============ 下载与复制 ============

function downloadResult(name) {
  const s = state[name];
  if (!s.lastResult) return;

  const isText = s.lastResult.format === 'text';
  const content = isText ? s.lastResult.data : JSON.stringify(s.lastResult.data, null, 2);
  const ext = isText ? '.dae' : '.json';
  const baseName = s.file ? s.file.name.replace(/\.(json|txt)$/i, '') : 'input';

  const blob = new Blob([content], { type: 'text/plain' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `${baseName}_converted_${getTimestamp()}${ext}`;
  a.click();
  URL.revokeObjectURL(url);
  showToast(`已下载 ${a.download}`, 'info');
}

async function copyResult(name) {
  const s = state[name];
  if (!s.lastResult) return;

  const content = s.lastResult.format === 'text'
    ? s.lastResult.data
    : JSON.stringify(s.lastResult.data, null, 2);

  const btn = document.getElementById(`copy-${name}`);
  try {
    await navigator.clipboard.writeText(content);
    showToast('已复制', 'success');
    if (btn) {
      const orig = btn.textContent;
      btn.textContent = '已复制';
      setTimeout(() => { btn.textContent = orig; }, 1500);
    }
  } catch (err) {
    showError(name, '复制到剪贴板失败：' + err.message);
  }
}

// ============ 事件绑定 ============

for (const name of Object.keys(state)) {
  const drop = document.getElementById(`drop-${name}`);
  const input = document.getElementById(`file-input-${name}`);
  const remove = document.getElementById(`remove-${name}`);
  const convert = document.getElementById(`convert-${name}`);
  const download = document.getElementById(`download-${name}`);
  const copy = document.getElementById(`copy-${name}`);

  if (drop && input) {
    drop.addEventListener('click', () => input.click());
    drop.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        input.click();
      }
    });
    drop.addEventListener('dragover', (e) => {
      e.preventDefault();
      drop.classList.add('dragover');
    });
    drop.addEventListener('dragleave', () => drop.classList.remove('dragover'));
    drop.addEventListener('drop', (e) => {
      e.preventDefault();
      drop.classList.remove('dragover');
      handleFileSelect(name, e.dataTransfer.files[0]);
    });
    input.addEventListener('change', (e) => handleFileSelect(name, e.target.files[0]));
  }

  if (remove) remove.addEventListener('click', () => resetConverter(name));
  if (convert) convert.addEventListener('click', () => doConvert(name));
  if (download) download.addEventListener('click', () => downloadResult(name));
  if (copy) copy.addEventListener('click', () => copyResult(name));

  const urlInput = document.getElementById(`url-input-${name}`);
  const urlClear = document.getElementById(`url-clear-${name}`);

  if (urlInput) {
    urlInput.addEventListener('input', () => {
      const hasUrl = Boolean(urlInput.value.trim());
      if (urlClear) urlClear.style.display = hasUrl ? 'flex' : 'none';
      if (hasUrl) {
        // 清除已选文件预览
        const s = state[name];
        s.file = null;
        s.data = null;
        if (input) input.value = '';
        const fileInfo = document.getElementById(`file-info-${name}`);
        if (fileInfo) fileInfo.style.display = 'none';
        if (convert) convert.disabled = false;
      } else {
        const s = state[name];
        if (convert) convert.disabled = !s.data;
      }
    });

    urlInput.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        if (urlInput.value.trim()) doConvert(name);
      }
    });
  }

  if (urlClear && urlInput) {
    urlClear.addEventListener('click', () => {
      urlInput.value = '';
      urlClear.style.display = 'none';
      const s = state[name];
      if (convert) convert.disabled = !s.data;
      urlInput.focus();
    });
  }

  const subCopy = document.getElementById(`sub-copy-${name}`);
  const subUrlInput = document.getElementById(`sub-url-${name}`);
  if (subCopy && subUrlInput) {
    subCopy.addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(subUrlInput.value);
        showToast('直链已复制到剪贴板', 'success');
      } catch (err) {
        showError(name, '复制直链失败：' + err.message);
      }
    });
  }
}

// 预览 JSON 代码视图切换
const previewSingbox = document.getElementById('preview-singbox');
if (previewSingbox) {
  previewSingbox.addEventListener('click', () => {
    const code = document.getElementById('code-singbox');
    if (!code) return;
    const isVisible = code.style.display === 'block';
    if (isVisible) {
      code.style.display = 'none';
      previewSingbox.textContent = '👁️ 预览 JSON';
    } else {
      if (state.singbox.lastResult && state.singbox.lastResult.data) {
        code.textContent = JSON.stringify(state.singbox.lastResult.data, null, 2);
        code.style.display = 'block';
        previewSingbox.textContent = '✕ 关闭预览';
        code.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
      }
    }
  });
}

// ============ 初始化 ============

initTheme();

// 动态获取后端构建日期
fetch('/api/version')
  .then(res => res.json())
  .then(data => {
    const badge = document.getElementById('buildBadge');
    if (badge && data.build_date) {
      badge.textContent = `Build ${data.build_date}`;
    }
  })
  .catch(() => {});
