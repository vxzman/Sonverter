#!/usr/bin/env bash
# 发布 Sonverter 后端。
# 默认 Native AOT：编译为原生二进制（无需目标机安装 .NET，体积约 20MB 内）。
# 备选框架依赖模式（目标机已装 .NET 10 运行时，体积最小）：
#   ./publish.sh --framework-dependent
set -euo pipefail
cd "$(dirname "$0")"

MODE="${1:-self-contained}"
if [[ "$MODE" == "--framework-dependent" ]]; then
  dotnet publish src/Sonverter -c Release -p:PublishSingleFile=true --self-contained false -o dist
  echo "提示：框架依赖模式，目标机需安装 .NET 10 运行时（运行时不在标准路径时设置 DOTNET_ROOT）"
else
  dotnet publish src/Sonverter -c Release -r linux-x64 -p:PublishAot=true -o dist
fi

echo "✅ 发布完成：$(pwd)/dist/Sonverter"
echo "运行示例：dist/Sonverter --serve --port 8080"
