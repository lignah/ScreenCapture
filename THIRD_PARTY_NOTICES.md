# Third-party notices

ScreenCapture itself is MIT-licensed (see `LICENSE`). Release builds bundle or download the components below.

| Component | Used for | License | Source |
| --- | --- | --- | --- |
| PaddleOCR PP-OCRv5 / PP-OCRv4 models (det, rec, dictionaries) | OCR models in `models\` and downloadable languages | Apache-2.0 | https://github.com/PaddlePaddle/PaddleOCR |
| RapidOCR ONNX conversions of the PaddleOCR models | The `.onnx` files actually downloaded (ModelScope `RapidAI/RapidOCR`) | Apache-2.0 | https://github.com/RapidAI/RapidOCR |
| ONNX Runtime (Microsoft.ML.OnnxRuntime, .Managed) 1.32.0 | Running the OCR models on the CPU | MIT | https://github.com/microsoft/onnxruntime |
| .NET 8 runtime, Windows Forms, System.Numerics.Tensors, System.Memory | Self-contained runtime inside `ScreenCapture.exe` | MIT | https://github.com/dotnet/runtime, https://github.com/dotnet/winforms |
| C#/WinRT runtime (WinRT.Runtime) | Windows OCR fallback interop | MIT | https://github.com/microsoft/CsWinRT |
| Microsoft.Windows.SDK.NET projection | Windows OCR fallback interop | Windows SDK license | https://aka.ms/WinSDKLicenseURL |

ONNX Runtime ships its own third-party notices; release zips include them as `onnxruntime-ThirdPartyNotices.txt`.

## Apache License 2.0 (PaddleOCR models, RapidOCR conversions)

The model files are redistributed unmodified. Full license text: https://www.apache.org/licenses/LICENSE-2.0

    Copyright (c) PaddlePaddle Authors. Licensed under the Apache License, Version 2.0.
    Copyright (c) RapidAI. Licensed under the Apache License, Version 2.0.
    Unless required by applicable law or agreed to in writing, software distributed under the License
    is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
    or implied. See the License for the specific language governing permissions and limitations.

## MIT License (ONNX Runtime, .NET, C#/WinRT)

    Copyright (c) Microsoft Corporation.

    Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
    associated documentation files (the "Software"), to deal in the Software without restriction,
    including without limitation the rights to use, copy, modify, merge, publish, distribute,
    sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all copies or
    substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
    NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
    NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
    DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT
    OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
