/**
 * Sonverter 出口节点转换工具 - 应用逻辑
 * 前端只负责界面和请求，转换逻辑由 C# 后端处理。
 * WinUI Mica 设计：标题栏 + 侧边栏切换 + 每转换器独立视图与状态。
 */

// ============ 主题切换 ============

const themeBtn = document.getElementById('themeBtn');

function applyTheme(theme) {
  if (theme === 'dark') {
    document.documentElement.setAttribute('data-theme', 'dark');
  } else {
    document.documentElement.removeAttribute('data-theme');
  }
}

function initTheme() {
  const saved = localStorage.getItem('sonverter-theme');
  const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
  applyTheme(saved || (prefersDark ? 'dark' : 'light'));
}

themeBtn.addEventListener('click', () => {
  const next = document.documentElement.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
  applyTheme(next);
  localStorage.setItem('sonverter-theme', next);
});

// ============ 状态 ============

// 每个转换器独立状态：singbox -> { file, data, lastResult }
const state = {};
const sidebar = document.getElementById('sidebar');

// 从静态视图收集转换器（.content > .card，id 为 view-{name}）
for (const view of document.querySelectorAll('.content > .card')) {
  const name = view.id.replace(/^view-/, '');
  const input = document.getElementById(`file-input-${name}`);
  state[name] = {
    file: null,
    data: null,          // singbox: JSON 对象；dae: 每行一个订阅 URL 的数组
    lastResult: null,    // { format, data }
    extensions: (input?.accept || '').split(',').map(s => s.trim().toLowerCase()),
  };
}

// ============ 工具函数 ============

function formatFileSize(bytes) {
  if (bytes < 1024) return bytes + ' B';
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(2) + ' KB';
  return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
}

function getTimestamp() {
  return new Date().toISOString()
    .replace(/[:.]/g, '-')
    .replace(/T/, '_')
    .slice(0, -5); // 去掉毫秒和时区
}

function showError(name, message) {
  const el = document.getElementById(`error-${name}`);
  if (!el) return;
  el.textContent = message;
  el.classList.add('show');
  setTimeout(() => el.classList.remove('show'), 5000);
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

sidebar.addEventListener('click', (e) => {
  const item = e.target.closest('.nav-item');
  if (item) switchTab(item.dataset.converter);
});

// ============ 文件选择 ============

function handleFileSelect(name, file) {
  const s = state[name];
  if (!file) return;

  const ext = '.' + (file.name.split('.').pop() || '').toLowerCase();
  if (s.extensions.length > 0 && !s.extensions.includes(ext)) {
    showError(name, `请选择 ${s.extensions.join(' / ')} 格式的文件`);
    return;
  }

  s.file = file;
  document.getElementById(`file-name-${name}`).textContent = file.name;
  document.getElementById(`file-size-${name}`).textContent = '(' + formatFileSize(file.size) + ')';
  document.getElementById(`file-info-${name}`).style.display = 'flex';
  document.getElementById(`convert-${name}`).disabled = false;

  // 读取文件内容：singbox 解析 JSON，dae 按行拆分订阅 URL
  const reader = new FileReader();
  reader.onload = (e) => {
    const text = e.target.result;
    if (s.extensions.includes('.json')) {
      try {
        s.data = JSON.parse(text);
      } catch (err) {
        s.data = null;
        showError(name, 'JSON 解析失败：' + err.message);
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
  document.getElementById(`convert-${name}`).disabled = true;
}

// ============ 转换 ============

async function doConvert(name) {
  const s = state[name];
  if (!s.data) {
    showError(name, '请先选择有效的文件');
    return;
  }

  const btn = document.getElementById(`convert-${name}`);
  try {
    btn.disabled = true;
    btn.textContent = '转换中...';

    const response = await fetch('/api/convert', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ converter: name, data: s.data }),
    });

    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.error || '转换失败');
    }

    const result = await response.json();
    if (!result.success) {
      throw new Error(result.error || '转换失败');
    }
    s.lastResult = result;

    const status = document.getElementById(`status-${name}`);
    if (result.format === 'text') {
      // 文本类转换器（dae）：代码区展示配置内容
      const nodeCount = result.data.split('\n').filter(line => line.includes(': "')).length;
      status.textContent = `✔ 转换完成（${nodeCount} 个节点）`;
      const code = document.getElementById(`code-${name}`);
      code.textContent = result.data;
      code.style.display = 'block';
      const stats = document.getElementById(`stats-${name}`);
      if (stats) stats.style.display = 'none';
    } else {
      // JSON 类转换器（singbox）：统计信息
      const merged = result.data;
      const statsData = getStats(merged);
      status.textContent = `✔ 转换完成（${statsData.totalNodes} 个节点）`;

      let statsHtml = '<div class="stats-row"><span class="stats-label">总节点数</span><span class="stats-value">' + statsData.totalNodes + '</span></div>';

      // 按节点数排序显示国家
      const sortedCountries = Object.entries(statsData.countryGroups)
        .sort((a, b) => b[1] - a[1]);

      for (const [country, count] of sortedCountries) {
        statsHtml += '<div class="stats-row"><span class="stats-label">' + country + '</span><span class="stats-value">' + count + ' 个</span></div>';
      }

      if (statsData.newCountries.length > 0) {
        statsHtml += '<div class="stats-row"><span class="stats-label">新增国家</span><span class="stats-value">' + statsData.newCountries.join(', ') + '</span></div>';
      }

      statsHtml += '<div class="stats-row"><span class="stats-label">其他国家节点</span><span class="stats-value">' + statsData.otherCount + ' 个</span></div>';

      const stats = document.getElementById(`stats-${name}`);
      if (stats) {
        stats.innerHTML = statsHtml;
        stats.style.display = 'block';
      }
      const code = document.getElementById(`code-${name}`);
      if (code) code.style.display = 'none';
    }

    document.getElementById(`result-${name}`).style.display = 'block';
    document.getElementById(`result-${name}`).scrollIntoView({ behavior: 'smooth', block: 'nearest' });

  } catch (err) {
    showError(name, '转换失败：' + err.message);
    console.error(err);
  } finally {
    btn.disabled = false;
    btn.textContent = '开始转换';
  }
}

/**
 * 统计信息（singbox）
 */
function getStats(merged) {
  const GROUP_TYPES = ['selector', 'urltest', 'direct'];
  const outbounds = merged.outbounds || [];

  // 节点 = 非分组类型的 outbounds
  const nodes = outbounds.filter(o => o && !GROUP_TYPES.includes(o.type));
  const totalNodes = nodes.length;
  const countryGroups = {};

  for (const node of nodes) {
    if (node && node.tag) {
      const parts = node.tag.split(' ');
      const label = parts[0]; // 获取 emoji+国家部分
      countryGroups[label] = (countryGroups[label] || 0) + 1;
    }
  }

  const defaultLabels = [
    "🇺🇸美国", "🇸🇬新加坡", "🇯🇵日本", "🇰🇷韩国",
    "🇭🇰中国香港", "🇹🇼中国台湾", "🇦🇺澳大利亚"
  ];

  // 获取实际存在的 urltest 国家（排除默认的）
  const newCountries = outbounds
    .filter(o => o.type === 'urltest' && !defaultLabels.includes(o.tag))
    .map(o => o.tag);

  // 获取其他国家节点数
  const otherTag = "🗺️其他国家";
  const otherOutbound = outbounds.find(o => o.type === 'selector' && o.tag === otherTag);
  const otherCount = otherOutbound ? (otherOutbound.outbounds || []).length : 0;

  return {
    totalNodes,
    countryGroups,
    newCountries,
    otherCount
  };
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
    btn.textContent = '✅ 已复制';
    setTimeout(() => {
      btn.textContent = '📋 复制内容';
    }, 1500);
  } catch (err) {
    showError(name, '复制失败：' + err.message);
  }
}

// ============ 事件绑定（每个视图） ============

for (const name of Object.keys(state)) {
  const drop = document.getElementById(`drop-${name}`);
  const input = document.getElementById(`file-input-${name}`);
  const remove = document.getElementById(`remove-${name}`);
  const convert = document.getElementById(`convert-${name}`);
  const download = document.getElementById(`download-${name}`);
  const copy = document.getElementById(`copy-${name}`);

  // 拖拽区：点击 / 键盘 / 拖放
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

  remove.addEventListener('click', () => resetConverter(name));

  convert.addEventListener('click', () => doConvert(name));
  download.addEventListener('click', () => downloadResult(name));
  copy.addEventListener('click', () => copyResult(name));
}

// ============ 初始化 ============

initTheme();
