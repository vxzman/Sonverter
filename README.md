# Sonverter

Singbox 出口节点转换与分组工具：上传节点列表 JSON → 自动识别国家/地区，生成规范化单个国旗中文标识（如 `🇭🇰中国香港 01`、`🇩🇪德国 01`），消除原始 tag 中的重复国家名称与符号，并按地区整理到测速分组与选择器中。

项目采用 **C#（ASP.NET Core Native AOT）+ Web（Vite）** 构建，静态资源与配置文件已完全内嵌进二进制，**单个二进制文件即可独立运行**，目标机无需安装 .NET 运行时或 Node.js 环境。

---

## 核心特性

- **节点标签规范化**：
  - 单一国家/地区标识符：统一格式为 `[Emoji国旗][中文名称] [序号/后缀]`（例如 `🇭🇰中国香港 01`、`🇩🇪德国 02 电信2x`）。
  - 港澳台一律规范化为 `🇭🇰中国香港`、`🇲🇴中国澳门`、`🇹🇼中国台湾`。
  - 自动消除多重国旗、重复中英文国名、国家代码（如 `HK`、`DE`）及多余括号标点。
- **纯英文紧凑日志系统**：
  - 日志输出全部为纯英文单行格式，无中文及表情符号，彻底杜绝终端或系统日志乱码。
  - 深度屏蔽 ASP.NET Core 内部多行诊断噪音，单行涵盖客户端 IP、方法、路径、节点数及耗时，完美适配 `systemd` 与 `journalctl`。
- **工作目录参数支持**：
  - 运行时支持 `--workdir <目录>`（`-w`、`--dir`），工作目录下若存在 `template.json` 将自动作为覆盖配置加载。
- **极简现代化 Web 面板**：
  - 去除繁琐宣传文案，仅保留关键功能与节点预览，支持明亮/暗黑主题切换。

---

## 构建机环境要求

- Linux x64（与目标服务器同架构）
- .NET 10 SDK
- Node.js 20+ 与 npm（用于编译前端界面）
- clang（用于 Native AOT 编译）

---

## 构建与发布

### 一键构建与打包

```bash
git clone git@github.com:vxzman/Sonverter.git
cd Sonverter

# 一键构建（自动编译前端并发布 Native AOT，默认以当天日期作为构建标识，如 202609201）
./publish.sh

# 指定自定义构建日期
./publish.sh --date 202609201
```

发布完成后产物位于 `dist/` 目录：
- `dist/Sonverter`：单文件独立运行的原生可执行程序（约 16MB）
- `dist/sonverter-linux-x64.tar.gz`：便携打包归档文件（约 7MB）

---

## 部署运行

### 1. 手动运行

将 `dist/Sonverter` 上传至目标服务器即可直接启动：

```bash
# 启动 Web 服务，默认端口 8080
./Sonverter --serve --port 8080

# 指定工作目录启动（优先加载指定目录下的 template.json）
./Sonverter --serve --port 9850 --workdir /etc/sonverter
```

浏览器访问 `http://服务器IP:端口` 即可打开 Web 控制面板。

### 2. systemd 服务常驻

使用仓库中的 `deploy/sonverter.service` 模板：

```ini
[Unit]
Description=Sonverter - Singbox Outbound Node Conversion Tool
After=network.target

[Service]
Type=simple
User=root
Group=root
WorkingDirectory=/usr/local/bin
ExecStart=/usr/local/bin/Sonverter --serve --port 9850 --workdir /usr/local/bin
Restart=on-failure
RestartSec=5

NoNewPrivileges=true
PrivateTmp=true

StandardOutput=journal
StandardError=journal
SyslogIdentifier=sonverter

[Install]
WantedBy=multi-user.target
```

```bash
sudo cp deploy/sonverter.service /etc/systemd/system/sonverter.service
sudo systemctl daemon-reload
sudo systemctl enable --now sonverter
```

查看服务状态与纯净单行日志：

```bash
systemctl status sonverter
journalctl -u sonverter -f
```

---

## 命令行参数

```bash
Sonverter - Singbox / Dae Outbound Node Conversion Tool

Usage:
  --input, -i <file>        Input file path
  --converter, -c <name>    Converter name (default: singbox, available: singbox, dae, example)
  --output, -o <file>       Output file path
  --config, -f <file>       Config file path (default: template.json)
  --workdir, -w, --dir <dir> Working directory (default: current directory)
  --serve                   Start web server
  --port, -p <port>         Web server port (default: 8080)
  --debug                   Enable debug logging
  --version, -v             Display version and build info
  --list-converters         List all available converters
  --help, -h                Display help
```

### CLI 转换示例

```bash
# singbox 格式转换
./Sonverter --input nodes.json --output out.json

# dae 格式转换（订阅链接文本转 dae node 配置）
./Sonverter --input nodes.txt --converter dae --output dae_nodes.dae

# 查看版本与构建信息
./Sonverter --version
```

---

## 转换器说明

### 1. singbox 转换器
- 输入：含有 `outbounds` 节点数组的 `.json` 配置文件。
- 输出：按国家/地区规范重整 tag，并自动生成分组选择器、测速组及最终规则节点的完整配置。
- 特殊规范：港澳台一律归一化为 `🇭🇰中国香港`、`🇲🇴中国澳门`、`🇹🇼中国台湾`。

### 2. dae 转换器
- 输入：每行一个订阅/节点链接的 `.txt` 文件（`协议://具体配置#备注`）。
- 输出：`node { tag: "url" #备注 }`，tag 自动提取为 `国家-协议-序号`（如 `hk-hy2-01`），URL 中的中文/Emoji 备注解码后作为行尾注释保留。

---

## 许可证

MIT License，详见 [LICENSE](LICENSE)。
