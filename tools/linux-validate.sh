#!/usr/bin/env bash
# Linux validation leg, runnable locally via Docker (mirrors the CI jobs):
#   docker run --rm -v "<repo>:/src:ro" mcr.microsoft.com/dotnet/sdk:10.0 bash /src/tools/linux-validate.sh
# Covers: full test suite (incl. fontconfig/DejaVu discovery), qpdf structural checks,
# and the NativeAOT/JIT byte-parity gate.
set -euo pipefail

echo "== apt packages =="
apt-get update -qq
apt-get install -y -qq qpdf fontconfig fonts-dejavu-core clang zlib1g-dev > /dev/null

echo "== copy source (bin/obj excluded) =="
mkdir -p /work
tar -C /src --exclude='./.git' --exclude='./artifacts' --exclude='*/bin' --exclude='*/obj' -cf - . | tar -C /work -xf -
cd /work

echo "== test suite =="
dotnet test -c Release

echo "== qpdf structural checks =="
dotnet run --project tests/Charta.Smoke -c Release -- /tmp/out > /tmp/jit.txt
cat /tmp/jit.txt
for f in /tmp/out/*.pdf; do
  echo "qpdf: $f"
  qpdf --check "$f" > /dev/null
done

echo "== PDF/A-2b sample generated (validate with veraPDF; see the pdfa-check CI job) =="
[ -f /tmp/out/pdfa-sample.pdf ] && echo "pdfa-sample.pdf present"

echo "== NativeAOT parity =="
dotnet publish tests/Charta.Smoke -c Release -r linux-x64 -o /tmp/aot
/tmp/aot/Charta.Smoke /tmp/aot-out > /tmp/aot.txt
diff /tmp/jit.txt /tmp/aot.txt
echo "AOT/JIT byte parity: OK"

echo "== linux validation PASSED =="
