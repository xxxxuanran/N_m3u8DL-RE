#!/usr/bin/env sh
set -eu

RELEASE_TAG=""

while [ $# -gt 0 ]; do
  case "$1" in
    -ReleaseTag) RELEASE_TAG="$2"; shift 2 ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

get_fallback_suffix() {
  date=$(TZ=Asia/Shanghai date +%Y%m%d)
  version="${PRODUCT_VERSION:-0.0.0}"
  case "$version" in
    v*) ;;
    *) version="v$version" ;;
  esac
  printf '%s+%s' "$date" "$version"
}

get_commit_date() {
  TZ=Asia/Shanghai git log -1 --format=%cd --date=format:%Y%m%d "$1"
}

if ! git_root=$(git rev-parse --show-toplevel 2>/dev/null); then
  get_fallback_suffix
  exit 0
fi

head=$(git rev-parse HEAD)
release_date=$(get_commit_date "$head")

if [ -z "$RELEASE_TAG" ]; then
  if exact_tag=$(git describe --tags --exact-match HEAD 2>/dev/null); then
    RELEASE_TAG=$exact_tag
  fi
fi

if [ -n "$RELEASE_TAG" ]; then
  tag_commit=$(git rev-parse "${RELEASE_TAG}^{commit}")
  if [ "$tag_commit" = "$head" ]; then
    printf '%s+%s' "$release_date" "$RELEASE_TAG"
    exit 0
  fi
fi

short_commit=$(git rev-parse --short HEAD)
printf '%s+%s' "$release_date" "$short_commit"
