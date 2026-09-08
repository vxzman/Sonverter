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
./publish.sh --version 1.0.1       # 产物：dist/Sonverter（单文件，约 16MB）
```

等价手动命令：

```bash
dotnet publish src/Sonverter -c Release -r linux-x64 -p:PublishAot=true -o dist
```

发布脚本会把版本号写入程序集和二进制元数据：

```bash
./publish.sh --version 1.0.1
./publish.sh --version 1.1.0-beta.1
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
./Sonverter --version                         # 显示版本与编译信息
./Sonverter --input nodes.json                    # 转换节点文件（singbox）
./Sonverter --input nodes.txt --converter dae     # 订阅 URL 列表转 dae node 配置
./Sonverter --input nodes.json --output out.json  # 指定输出
./Sonverter --list-converters                     # 列出转换器
```

### 版本规划

项目版本遵循语义化版本（Semantic Versioning）`MAJOR.MINOR.PATCH`：

- `MAJOR`：不兼容的命令行参数、配置格式或输出格式变更
- `MINOR`：向后兼容的新功能或新转换器
- `PATCH`：向后兼容的问题修复、性能优化和文档更新

当前版本为 `1.0.1`。预发布版本使用 `-alpha.N`、`-beta.N` 或 `-rc.N` 后缀，例如 `1.1.0-beta.1`。

`--version` 会显示版本号、UTC 编译时间、.NET 版本、运行时、操作系统、平台架构和进程架构。Web 服务同时提供 `GET /api/version`。

### dae 转换器

输入为每行一个订阅 URL 的 `.txt` 文件（`协议://具体配置#备注`），输出 `node { tag: "url" #备注 }`：

```
node {
    # HTTPS/VMess/VLESS/Shadowsocks/Trojan/Tuic/Juicity/Hysteria2 等格式
    hk-hy2-02: "hysteria2://user:password@host:20399/?insecure=0" #HK2-HY2
    other-vless-01: "vless://..." #CF电信优选1
}
```

- tag 规则：`国家简写-协议简写-序号`（如 `hk-hy2-02`），国家信息取自备注开头，提取不到时用 `other`（如 `other-vless-01`）
- 序号优先取备注中的数字，取不到时按同组递增，序号固定两位数
- dae 的 tag 只支持英文，URL 中的中文/emoji 备注会解码后作为行尾注释保留

## 许可证

MIT License，详见 [LICENSE](LICENSE)。
