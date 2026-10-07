#!/usr/bin/env bash
# 发布 Sonverter 后端。
# 默认 Native AOT：编译为原生二进制（无需目标机安装 .NET，体积约 20MB 内）。
# 嵌入构建日期（如 202609201），无需语义化版本号。
# 用法：
#   ./publish.sh
#   ./publish.sh --date 202609201
#   ./publish.sh --framework-dependent
set -euo pipefail
cd "$(dirname "$0")"

# 优先查找 ~/.dotnet
if [[ -d "$HOME/.dotnet" && ":$PATH:" != *":$HOME/.dotnet:"* ]]; then
  export PATH="$HOME/.dotnet:$PATH"
fi

MODE="self-contained"
BUILD_DATE="$(date +%Y%m%d1)"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --framework-dependent)
      MODE="framework-dependent"
      shift
      ;;
    --date|--build-date|-d)
      if [[ $# -lt 2 ]]; then
        echo "错误：--date 需要一个构建日期参数，例如 202609201" >&2
        exit 1
      fi
      BUILD_DATE="$2"
      shift 2
      ;;
    --version|-v)
      if [[ $# -ge 2 ]]; then
        BUILD_DATE="$2"
        shift 2
      else
        shift
      fi
      ;;
    --help|-h)
      echo "用法：$0 [--date 202609201] [--framework-dependent]"
      exit 0
      ;;
    *)
      echo "错误：未知参数 $1" >&2
      echo "用法：$0 [--date 202609201] [--framework-dependent]" >&2
      exit 1
      ;;
  esac
done

BUILD_PROPERTY="-p:BuildDate=$BUILD_DATE"

if command -v npm >/dev/null 2>&1; then
  echo "正在构建前端静态资源..."
  npm --prefix web run build
fi

if [[ "$MODE" == "framework-dependent" ]]; then
  dotnet publish src -c Release "$BUILD_PROPERTY" -p:PublishSingleFile=true --self-contained false -o dist
  echo "提示：框架依赖模式，目标机需安装 .NET 10 运行时（运行时不在标准路径时设置 DOTNET_ROOT）"
else
  dotnet publish src -c Release -r linux-x64 "$BUILD_PROPERTY" -p:PublishAot=true -o dist
fi

if [[ -f "dist/Sonverter" ]]; then
  tar -czf dist/sonverter-linux-x64.tar.gz -C dist Sonverter
fi

echo "构建日期：$BUILD_DATE"
echo "✅ 发布完成：$(pwd)/dist/Sonverter"
echo "运行示例：dist/Sonverter --serve --port 8080 --workdir $(pwd)"
