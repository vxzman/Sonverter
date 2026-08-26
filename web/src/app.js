/**
 * Singbox 出口节点转换工具 - 应用逻辑
 * 前端只负责界面和请求，转换逻辑由 Python 后端处理
 */

// DOM 元素
const uploadArea = document.getElementById('uploadArea');
const fileInput = document.getElementById('fileInput');
const fileInfo = document.getElementById('fileInfo');
const fileName = document.getElementById('fileName');
const fileSize = document.getElementById('fileSize');
const convertBtn = document.getElementById('convertBtn');
const resultArea = document.getElementById('resultArea');
const downloadConverted = document.getElementById('downloadConverted');
const stats = document.getElementById('stats');
const errorMsg = document.getElementById('errorMsg');

// 当前文件
let currentFile = null;
let currentData = null;

/**
 * 格式化文件大小
 */
function formatFileSize(bytes) {
  if (bytes < 1024) return bytes + ' B';
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(2) + ' KB';
  return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
}

/**
 * 显示错误信息
 */
function showError(message) {
  errorMsg.textContent = message;
  errorMsg.classList.add('show');
  setTimeout(() => {
    errorMsg.classList.remove('show');
  }, 5000);
}

/**
 * 处理文件选择
 */
function handleFileSelect(file) {
  if (!file) return;

  if (!file.name.endsWith('.json')) {
    showError('请选择 JSON 格式的文件');
    return;
  }

  currentFile = file;
  fileName.textContent = '📄 ' + file.name;
  fileSize.textContent = formatFileSize(file.size);
  fileInfo.classList.add('show');
  convertBtn.disabled = false;

  // 读取文件内容
  const reader = new FileReader();
  reader.onload = (e) => {
    try {
      currentData = JSON.parse(e.target.result);
    } catch (err) {
      showError('JSON 解析失败：' + err.message);
      currentData = null;
    }
  };
  reader.readAsText(file, 'utf-8');
}

/**
 * 生成带时间戳的文件名
 */
function getTimestampedFilename(baseName, suffix) {
  const now = new Date();
  const timestamp = now.toISOString()
    .replace(/[:.]/g, '-')
    .replace(/T/, '_')
    .slice(0, -5); // 去掉毫秒和时区
  return `${baseName}_${suffix}_${timestamp}.json`;
}

/**
 * 执行转换 - 调用 Python 后端 API
 */
async function doConvert() {
  if (!currentData) {
    showError('请先选择有效的 JSON 文件');
    return;
  }

  try {
    convertBtn.disabled = true;
    convertBtn.textContent = '转换中...';

    // 发送请求到 Python 后端
    const response = await fetch('/api/convert', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(currentData),
    });

    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.error || '转换失败');
    }

    const result = await response.json();
    
    if (!result.success) {
      throw new Error(result.error || '转换失败');
    }

    const merged = result.data;

    // 获取统计信息
    const statsData = getStats(merged);

    // 准备下载 - 添加时间戳
    const inputName = currentFile ? currentFile.name.replace('.json', '') : 'input';
    const timestamp = new Date().toISOString()
      .replace(/[:.]/g, '-')
      .replace(/T/, '_')
      .slice(0, -5);

    // 合并后的文件（分组模板 + 节点列表）
    const convertedBlob = new Blob([JSON.stringify(merged, null, 2)], { type: 'application/json' });
    const convertedUrl = URL.createObjectURL(convertedBlob);
    downloadConverted.href = convertedUrl;
    downloadConverted.download = `${inputName}_converted_${timestamp}.json`;
    
    // 显示统计信息
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
    
    stats.innerHTML = statsHtml;
    
    // 显示结果
    resultArea.classList.add('show');
    
    // 滚动到结果区域
    resultArea.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    
  } catch (err) {
    showError('转换失败：' + err.message);
    console.error(err);
  } finally {
    convertBtn.disabled = false;
    convertBtn.textContent = '开始转换';
  }
}

/**
 * 统计信息
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

// 点击上传区域
uploadArea.addEventListener('click', () => {
  fileInput.click();
});

// 文件选择变化
fileInput.addEventListener('change', (e) => {
  handleFileSelect(e.target.files[0]);
});

// 拖拽事件
uploadArea.addEventListener('dragover', (e) => {
  e.preventDefault();
  uploadArea.classList.add('dragover');
});

uploadArea.addEventListener('dragleave', () => {
  uploadArea.classList.remove('dragover');
});

uploadArea.addEventListener('drop', (e) => {
  e.preventDefault();
  uploadArea.classList.remove('dragover');
  handleFileSelect(e.dataTransfer.files[0]);
});

// 转换按钮点击
convertBtn.addEventListener('click', doConvert);
