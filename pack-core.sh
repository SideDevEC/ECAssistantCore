#!/bin/bash
# pack-core.sh — Build Core, create NuGet package with native runtime files, publish to local feed.
# Usage: ./pack-core.sh

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
NUGET_FEED="$(cd "$SCRIPT_DIR/.." && pwd)/nuget-feed"
NUGET_CACHE="${NUGET_PACKAGES:-$HOME/.nuget/packages}"

echo "=== Building ECAssistant.Core ==="
cd "$SCRIPT_DIR"
dotnet build ECAssistantCore.sln -c Release
dotnet pack ECAssistant.Core.csproj -c Release --no-build

NUPKG="$SCRIPT_DIR/bin/Release/ECAssistant.Core.1.0.0.nupkg"
TMP_DIR="$SCRIPT_DIR/bin/Release/nupkg-tmp"

echo "=== Adding native runtime files to package ==="
rm -rf "$TMP_DIR"
mkdir -p "$TMP_DIR"
cd "$TMP_DIR"
unzip -q "$NUPKG"

# Copy native runtime files from LLamaSharp backend NuGet packages
for pkg in llamasharp.backend.cpu llamasharp.backend.vulkan llamasharp.backend.vulkan.linux llamasharp.backend.vulkan.windows llamasharp.backend.cuda12.linux; do
    SRC="$NUGET_CACHE/$pkg/0.27.0/LLamaSharpRuntimes"
    if [ -d "$SRC" ]; then
        echo "  Adding native files from $pkg"
        mkdir -p "runtimes"
        cp -r "$SRC"/* "runtimes/"
    fi
done

# Remove content/ folder that has stray native files (from IncludeContentInPack)
rm -rf content contentFiles 2>/dev/null

# Repackage
echo "=== Repacking NuGet package ==="
zip -q -r "$NUPKG.new" .
mv "$NUPKG.new" "$NUPKG"

echo "=== Publishing to local feed ==="
mkdir -p "$NUGET_FEED"
rm -f "$NUGET_FEED/ECAssistant.Core.1.0.0.nupkg"
cp "$NUPKG" "$NUGET_FEED/"

# Clean up
rm -rf "$TMP_DIR"

echo ""
echo "Done! Package published to: $NUGET_FEED/ECAssistant.Core.1.0.0.nupkg"
echo "Consumers: dotnet restore will pick it up via nuget.config local source."