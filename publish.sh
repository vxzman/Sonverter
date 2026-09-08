#!/usr/bin/env bash
# 发布 Sonverter 后端。
# 默认 Native AOT：编译为原生二进制（无需目标机安装 .NET，体积约 20MB 内）。
# 备选框架依赖模式（目标机已装 .NET 10 运行时，体积最小）：
#   ./publish.sh --version 1.0.1
#   ./publish.sh --framework-dependent --version 1.0.1
set -euo pipefail
cd "$(dirname "$0")"

MODE="self-contained"
VERSION="1.0.0"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --framework-dependent)
      MODE="framework-dependent"
      shift
      ;;
    --version)
      if [[ $# -lt 2 ]]; then
        echo "错误：--version 需要一个版本号，例如 1.0.1" >&2
        exit 1
      fi
      VERSION="$2"
      shift 2
      ;;
    --help|-h)
      echo "用法：$0 [--version VERSION] [--framework-dependent]"
      exit 0
      ;;
    *)
      echo "错误：未知参数 $1" >&2
      echo "用法：$0 [--version VERSION] [--framework-dependent]" >&2
      exit 1
      ;;
  esac
done

if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]]; then
  echo "错误：版本号必须符合 SemVer，例如 1.0.1 或 1.1.0-beta.1" >&2
  exit 1
fi

VERSION_PROPERTY="-p:VersionPrefix=$VERSION"
if [[ "$MODE" == "framework-dependent" ]]; then
  dotnet publish src/Sonverter -c Release "$VERSION_PROPERTY" -p:PublishSingleFile=true --self-contained false -o dist
  echo "提示：框架依赖模式，目标机需安装 .NET 10 运行时（运行时不在标准路径时设置 DOTNET_ROOT）"
else
  dotnet publish src/Sonverter -c Release -r linux-x64 "$VERSION_PROPERTY" -p:PublishAot=true -o dist
fi

echo "版本：$VERSION"
echo "✅ 发布完成：$(pwd)/dist/Sonverter"
echo "运行示例：dist/Sonverter --serve --port 8080"
