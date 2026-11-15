#!/usr/bin/env bash
# Records a smash demo sequence from the Linux player (scripts/build-linux.sh) under Xvfb and
# encodes it to MP4.   tools/smash/record-demo.sh <lamps|barge|wreck|dive|crash20|all> <out.mp4> [perf.txt]
set -euo pipefail
scene="$1"
out="$(realpath -m "$2")"
perf="${3:-}"
repo="$(cd "$(dirname "$0")/../.." && pwd)"
player="$repo/Builds/Linux/Dogpatch.x86_64"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
args=(-smashDemo "$scene" -capture "$work" -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -logFile "$work/player.log")
[[ -n "$perf" ]] && args+=(-perf "$(realpath -m "$perf")")
xvfb-run -a -s "-screen 0 1280x720x24" "$player" "${args[@]}" || true
grep '\[SmashDemo\]\|\[Smash\]\|Exception' "$work/player.log" || true
size="$(cat "$work/size.txt")"
ffmpeg -nostdin -loglevel error -y -f rawvideo -pix_fmt rgb24 -s "$size" -r 30 -i "$work/frames.rgb" -vf vflip \
  -c:v libx264 -pix_fmt yuv420p -crf 20 -movflags +faststart "$out"
echo "wrote $out"
