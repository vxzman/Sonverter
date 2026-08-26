# Sonverter

Singbox 出口节点转换工具：上传节点列表 JSON → 自动为 tag 添加 emoji 前缀 → 按国家/地区分组，生成可直接使用的 outbounds 配置。

由 cuddly-winner 使用 **C#（ASP.NET Core）+ npm（Vite）** 完全重构，前端页面与配置已内嵌进二进制，**单文件即可部署**，服务器无需安装 .NET 运行时。

## 环境要求（构建机）

- Linux x64（与目标服务器同架构）
- .NET 10 SDK
- Node.js 20+ 与 npm（用于构建前端）
- clang（Native AOT 编译）

## 1. 克隆

```bash
git clone git@github.com:vxzman/Sonverter.git
cd Sonverter
```

## 2. 构建前端

```bash
cd web
npm install
npm run build      # 产物输出到 ../src/Sonverter/wwwroot，编译时嵌入二进制
cd ..
```

## 3. 编译 Native AOT 二进制

```bash
./publish.sh       # 产物：dist/Sonverter（单文件，约 16MB）
```

等价手动命令：

```bash
dotnet publish src/Sonverter -c Release -r linux-x64 -p:PublishAot=true -o dist
```

## 4. 部署到服务器

本机构建后打包并上传（服务器只需 Linux x64，无需任何运行时）：

```bash
tar czf sonverter-linux-x64.tar.gz -C dist Sonverter
scp sonverter-linux-x64.tar.gz user@服务器IP:/opt/
ssh user@服务器IP
cd /opt && tar xzf sonverter-linux-x64.tar.gz
```

直接运行：

```bash
./Sonverter --serve --port 8080
```

浏览器打开 `http://服务器IP:8080` 即可使用。

## 5. systemd 常驻（可选）

使用仓库内的 `deploy/sonverter.service` 模板，替换 `%USER%`、`%GROUP%`、`%INSTALL_DIR%`、`%PORT%` 占位符：

```bash
sudo cp deploy/sonverter.service /etc/systemd/system/sonverter.service
sudo systemctl daemon-reload
sudo systemctl enable --now sonverter
```

验证服务：

```bash
curl http://localhost:8080/api/health   # 期望：{"status":"ok"}
```

## 命令行用法

```bash
./Sonverter --input nodes.json                    # 转换节点文件
./Sonverter --input nodes.json --output out.json  # 指定输出
./Sonverter --list-converters                     # 列出转换器
```

## 许可证

MIT License，详见 [LICENSE](LICENSE)。
