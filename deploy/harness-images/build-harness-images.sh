#!/bin/bash
# Build all harness images for AI Orchestrator
# Usage: ./build-harness-images.sh [registry] [tag]
# Example: ./build-harness-images.sh ghcr.io/mariogk v1.0.0

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(dirname "$SCRIPT_DIR")"
REGISTRY="${1:-ghcr.io/mariogk}"
TAG="${2:-latest}"

echo "=========================================="
echo "AI Orchestrator - Harness Image Builder"
echo "=========================================="
echo "Registry: $REGISTRY"
echo "Tag: $TAG"
echo "Script dir: $SCRIPT_DIR"
echo ""

build_image() {
    local dockerfile="$1"
    local image_name="$2"
    local context_dir="${3:-$SCRIPT_DIR}"
    
    echo "Building $image_name..."
    if docker build \
        -f "$context_dir/$dockerfile" \
        -t "$REGISTRY/$image_name:$TAG" \
        -t "$REGISTRY/$image_name:latest" \
        "$context_dir"; then
        echo "✓ Built $REGISTRY/$image_name:$TAG"
    else
        echo "✗ Failed to build $image_name"
        return 1
    fi
    echo ""
}

echo "=== Building base image ==="
build_image "Dockerfile.harness-base" "ai-harness-base"

echo "=== Building harness images ==="
build_image "Dockerfile.harness-codex" "harness-codex"
build_image "Dockerfile.harness-opencode" "harness-opencode"
build_image "Dockerfile.harness-claudecode" "harness-claudecode"
build_image "Dockerfile.harness-zai" "harness-zai"

echo "=== Building all-in-one image ==="
build_image "Dockerfile" "ai-harness" "$DEPLOY_DIR/harness-image"

echo "=== Build Summary ==="
echo "Images built:"
docker images --format "table {{.Repository}}:{{.Tag}}\t{{.Size}}\t{{.CreatedAt}}" | grep -E "(REPOSITORY|$REGISTRY)" | head -20

echo ""
echo "=== Push Commands ==="
echo "To push images to registry:"
echo "  for img in ai-harness-base harness-codex harness-opencode harness-claudecode harness-zai ai-harness; do"
echo "    docker push $REGISTRY/\$img:$TAG"
echo "    docker push $REGISTRY/\$img:latest"
echo "  done"
echo ""
echo "Or use docker compose:"
echo "  docker compose -f $DEPLOY_DIR/docker-compose.yml --profile build-harnesses build"
echo "  docker compose -f $DEPLOY_DIR/docker-compose.yml --profile build build"
