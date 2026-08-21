#requires -Version 7
<#
.SYNOPSIS
  Fetch the pinned in-process embedder asset (bge-small-en-v1.5, int8 ONNX) for OmnisRouter.

.DESCRIPTION
  Downloads the pinned ONNX model + BERT vocab into models/bge-small-en-v1.5/ and verifies the model's
  SHA-256 so the embedder — and therefore the routing model built from it — is reproducible. The model
  binary is git-ignored (34 MB); the vocab is tracked. Run once after cloning. The Docker build fetches
  the same pinned asset (identical URL + SHA-256) directly in deploy/Dockerfile, so container images
  ship the embedder without this script. The app auto-detects the asset at models/bge-small-en-v1.5/
  and uses the real ONNX embedder when present, falling back to the deterministic HashingEmbedder otherwise.
#>
[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..' 'models' 'bge-small-en-v1.5')
)
$ErrorActionPreference = 'Stop'

# Pinned asset (int8 quantized ONNX port of BAAI/bge-small-en-v1.5) + its exact hash.
$ModelUrl = 'https://huggingface.co/Xenova/bge-small-en-v1.5/resolve/main/onnx/model_quantized.onnx'
$ModelSha = '6c9c6101a956d62dfb5e7190c538226c0c5bb9cb27b651234b6df063ee7dbfe4'
$VocabUrl = 'https://huggingface.co/BAAI/bge-small-en-v1.5/resolve/main/vocab.txt'

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$modelPath = Join-Path $OutDir 'model.onnx'
$vocabPath = Join-Path $OutDir 'vocab.txt'

if (-not (Test-Path $vocabPath)) {
    Write-Host "Downloading vocab.txt ..."
    Invoke-WebRequest -Uri $VocabUrl -OutFile $vocabPath
}

if (Test-Path $modelPath) {
    $existing = (Get-FileHash -Algorithm SHA256 -Path $modelPath).Hash.ToLowerInvariant()
    if ($existing -eq $ModelSha) {
        Write-Host "model.onnx already present and matches the pinned SHA-256. Nothing to do."
        return
    }
    Write-Warning "Existing model.onnx SHA-256 mismatch; re-downloading."
}

Write-Host "Downloading model.onnx (~34 MB) ..."
Invoke-WebRequest -Uri $ModelUrl -OutFile $modelPath

$actual = (Get-FileHash -Algorithm SHA256 -Path $modelPath).Hash.ToLowerInvariant()
if ($actual -ne $ModelSha) {
    Remove-Item $modelPath -Force
    throw "SHA-256 mismatch for model.onnx (expected $ModelSha, got $actual). Deleted."
}

Write-Host "OK: embedder asset verified at $OutDir"
