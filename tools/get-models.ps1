# Downloads the PaddleOCR ONNX models (RapidAI/RapidOCR on ModelScope, Apache-2.0) into ..\models and verifies SHA-256.
$ErrorActionPreference = 'Stop'
$dest = Join-Path (Split-Path $PSScriptRoot -Parent) 'models'
New-Item -ItemType Directory -Force $dest | Out-Null
$base = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/master'
$files = @(
    @{ Name = 'det.onnx';        Url = "$base/onnx/PP-OCRv5/det/ch_PP-OCRv5_det_mobile.onnx";        Sha = '4d97c44a20d30a81aad087d6a396b08f786c4635742afc391f6621f5c6ae78ae' },
    @{ Name = 'rec_korean.onnx'; Url = "$base/onnx/PP-OCRv5/rec/korean_PP-OCRv5_rec_mobile.onnx";    Sha = 'cd6e2ea50f6943ca7271eb8c56a877a5a90720b7047fe9c41a2e541a25773c9b' },
    @{ Name = 'korean_dict.txt'; Url = "$base/paddle/PP-OCRv5/rec/korean_PP-OCRv5_rec_mobile/ppocrv5_korean_dict.txt"; Sha = 'a88071c68c01707489baa79ebe0405b7beb5cca229f4fc94cc3ef992328802d7' }
)
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
foreach ($f in $files) {
    $path = Join-Path $dest $f.Name
    if (-not (Test-Path $path) -or (Get-FileHash $path -Algorithm SHA256).Hash -ne $f.Sha.ToUpper()) {
        Write-Host "Downloading $($f.Name)..."
        Invoke-WebRequest -Uri $f.Url -OutFile $path -UseBasicParsing
    }
    $hash = (Get-FileHash $path -Algorithm SHA256).Hash
    if ($hash -ne $f.Sha.ToUpper()) { Remove-Item $path; throw "SHA-256 mismatch for $($f.Name): $hash" }
    Write-Host "OK  $($f.Name)  $((Get-Item $path).Length) bytes"
}
