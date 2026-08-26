# Sonverter - Singbox 出口节点转换工具（C# + npm 重写版）

这是 [cuddly-winner](cuddly-winner/)（Python + Flask）的 **C# + npm 重写版**。
功能与原版一致：上传 Singbox 节点列表 JSON → 为 tag 添加 emoji 前缀 → 按国家/地区分组生成可直接使用的 outbounds 配置。

原 Python 项目保留在 `cuddly-winner/` 目录中，本目录即为新实现。

## 技术栈

- **后端**：C# / ASP.NET Core Minimal API（.NET 10），转换逻辑 1:1 移植自 Python 版
- **前端**：npm + Vite 管理的静态页面，构建产物输出到 ASP.NET 的 `wwwroot/`，由后端直接托管

## 项目结构

```
├── src/Sonverter/             # C# 后端
│   ├── Program.cs             # CLI + Web 服务入口
│   ├── Converters/            # 转换器（注册中心 / Singbox / 示例）
│   ├── DefaultConfig.cs       # 配置加载（嵌入式 template.json + --config 覆盖）
│   ├── JsonHelper.cs          # 序列化（emoji/中文原样输出）
│   ├── template.json          # 国家映射、分组规则（与原版相同）
│   └── wwwroot/               # 前端构建产物（编译时嵌入二进制，运行时无需此目录）
├── web/                       # npm 前端源码（Vite）
├── cuddly-winner/             # 原 Python 版（已在 .gitignore，仅本机参考）
├── deploy/                    # systemd 服务模板
├── publish.sh                 # 发布单文件二进制
└── Sonverter.slnx             # .NET 解决方案
```

## 快速开始

### 后端（C#，开发模式）

需要 .NET 10 SDK。

```bash
# 命令行转换（注意：dotnet run 的工作目录是项目目录）
cd src/Sonverter
dotnet run -- --input ../../cuddly-winner/input_example.json

# 启动 Web 服务（默认 8080）
dotnet run -- --serve --port 8080
```

> 注意：`dotnet run --project` 的工作目录固定为项目目录，相对路径以
> `src/Sonverter/` 为基准；发布后的二进制则跟随调用时的目录。

浏览器打开 http://localhost:8080，上传 JSON 节点文件 → 开始转换 → 下载结果。

### 前端（npm）

```bash
cd web
npm install
npm run dev        # Vite 开发服务器（/api 代理到 http://localhost:8080）
npm run build      # 构建产物输出到 ../src/Sonverter/wwwroot
```

仓库已附带构建产物，直接跑后端即可使用，不构建前端也能运行。
前端资源会在编译时**嵌入二进制**，发布后只有一个文件，无需附带 `wwwroot/`。

## 命令行用法

```bash
dotnet run -- --input input.json                    # 转换（默认 singbox 转换器）
dotnet run -- --input input.json --output out.json  # 指定输出
dotnet run -- --input input.json --config config.json  # 自定义配置
dotnet run -- --list-converters                     # 列出转换器
dotnet run -- --serve --port 8080                   # 启动 Web 服务
```

## Web API（与原版一致）

| 接口 | 说明 |
| --- | --- |
| `GET /api/health` | 健康检查：`{"status":"ok"}` |
| `GET /api/converters` | 列出可用转换器 |
| `POST /api/convert` | 转换节点配置，请求体为 `{"outbounds":[...]}`，响应 `{"success":true,"data":{...}}` |
| `GET /` | 前端页面 |

## 与 Python 版的差异（有意为之）

1. **CLI 配置行为修复**：原版 CLI 在转换器构造后才 `update` 配置，
   `country_map` 在构造时已缓存，导致 CLI 模式下 `--config` 中的国家映射**不生效**
   （Web 模式正常）。新版 CLI 与 Web 行为一致，配置文件始终生效。
2. **重复 JSON 键**：`template.json` 中 `TW` 出现两次，按 JSON 解析惯例后者覆盖
   （与 Python `json.loads` 一致），新版显式处理了该情况。
3. **输出格式**：UTF-8 无 BOM、缩进 2、中文与 emoji 原样输出，与
   `json.dumps(ensure_ascii=False, indent=2)` 一致。

其余转换逻辑（国家识别三策略、技术词排除、3 节点自动分组、urltest 随机 URL 等）
与原版完全一致，已用原版输出做回归对比验证。

## 编译 Native AOT 二进制（直接部署服务器）

项目默认以 Native AOT 发布：编译为原生机器码的**单文件二进制**，
前端页面与配置也已内嵌进二进制，目标服务器**无需安装 .NET 运行时、无需附带任何其他文件**，
拷贝一个文件即可运行（Linux x64）。

```bash
./publish.sh                          # Native AOT（推荐，产物 dist/Sonverter，单文件）
./publish.sh --framework-dependent    # 备选：框架依赖版（需目标机装 .NET 10）
# 运行：dist/Sonverter --serve --port 8080（在任意目录均可）
```

AOT 编译需要 `clang` 等本机编译工具链。也可直接执行：

```bash
dotnet publish src/Sonverter -c Release -r linux-x64 -p:PublishAot=true -o dist
```

### 服务器部署

```bash
# 本机构建后拷贝单个文件到服务器
scp dist/sonverter-linux-x64.tar.gz user@server:/opt/
cd /opt && tar xzf sonverter-linux-x64.tar.gz

# 服务器上创建 systemd 服务（模板见 deploy/）
sudo cp deploy/sonverter.service /etc/systemd/system/sonverter.service
# 编辑替换 %USER%/%GROUP%/%INSTALL_DIR%/%PORT% 后：
sudo systemctl daemon-reload && sudo systemctl enable --now sonverter

# 验证
curl http://localhost:8080/api/health
```

> Native AOT 在目标机上的 libc 依赖与构建机一致（Linux x64 → Linux x64）。
> 框架依赖模式运行时不在标准路径时需设置 `DOTNET_ROOT`。

配合 `deploy/sonverter.service` 可部署为 systemd 服务（替换 `%USER%`、
`%GROUP%`、`%INSTALL_DIR%`、`%PORT%` 占位符）。

## 许可证

MIT（与原版一致）
