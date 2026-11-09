#!/bin/sh
# Runs every example against the local test API with the site's sample files, in a temporary
# folder. The usage example runs against the mock (the local instance doesn't serve /v2/usage).
# The key is read from ../../conformance/.local-api.env and never printed.
set -eu

here=$(cd "$(dirname "$0")" && pwd)
sdks=$(cd "$here/../.." && pwd)
samples="$sdks/../site/public/samples"
env_file="$sdks/conformance/.local-api.env"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

cp "$samples/docx-to-pdf/input-quarterly-report.docx" "$work/report.docx"
cp "$samples/pdf-to-jpg/input-season-guide.pdf" "$work/season-guide.pdf"
cp "$samples/merge/input-invoice.pdf" "$work/invoice.pdf"
cp "$samples/merge/input-terms.pdf" "$work/terms.pdf"
cp "$samples/merge/input-timesheet.pdf" "$work/timesheet.pdf"
cp "$samples/watermark/input-proposal.pdf" "$work/proposal.pdf"
cp "$samples/png-to-jpg/input-mug-product.png" "$work/logo.png"

dotnet build "$here/PdfConversionApi.Examples" -c Release -o "$work/bin" -v quiet -nologo > /dev/null

PDFCONVERSIONAPI_KEY=$(sed -n 's/^PDFCONVERSIONAPI_LOCAL_KEY=//p' "$env_file")
PDFCONVERSIONAPI_BASE_URL=$(sed -n 's/^PDFCONVERSIONAPI_LOCAL_BASE_URL=//p' "$env_file")
export PDFCONVERSIONAPI_KEY PDFCONVERSIONAPI_BASE_URL

cd "$work"
for name in quickstart one-file pdf-to-jpg merge watermark parameters no-file stored-files errors configuration http-client-factory; do
  echo "== $name"
  dotnet "$work/bin/PdfConversionApi.Examples.dll" "$name"
done

node "$sdks/conformance/mock-server.mjs" --port 0 > "$work/mock.out" &
mock=$!
while ! grep -q LISTENING "$work/mock.out" 2>/dev/null; do sleep 0.1; done
echo "== usage (against the mock)"
PDFCONVERSIONAPI_KEY=test-key PDFCONVERSIONAPI_BASE_URL=$(sed -n 's/^LISTENING //p' "$work/mock.out") \
  dotnet "$work/bin/PdfConversionApi.Examples.dll" usage
kill "$mock"

echo "== files written"
find . -path ./bin -prune -o -type f -newer "$work/bin/PdfConversionApi.Examples.dll" -print | sort
