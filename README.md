# 화면 캡처

- WinForms (.NET 8), 트레이 상주
- 영역, 창, 전체 화면 캡처 (다중 모니터, 고DPI 대응)
- 사각형, 원, 화살표, 펜, 텍스트, 모자이크 그리기 후 복사, 저장, 화면에 고정
- 오프라인 OCR (PaddleOCR), 인식한 글자는 바로 클립보드로 복사됨
- 돋보기에서 색상값 `#RRGGBB` 복사

## 단축키

| 키 | 동작 |
| --- | --- |
| `Ctrl+Shift+A`, 트레이 아이콘 클릭 | 영역 캡처 시작 (단축키는 설정에서 변경) |
| 드래그 / 창 위에서 클릭 | 영역 / 창 선택 |
| `Enter`, 더블클릭, `Ctrl+C` | 클립보드로 복사 |
| `Ctrl+S` | 파일로 저장 |
| `Ctrl+T` | OCR, 글자를 클립보드로 복사 |
| `C` | 커서 위치 색상을 `#RRGGBB`로 복사하고 닫기 |
| `Ctrl+Z` | 그린 것 되돌리기 |
| 방향키 | 선택 영역 1px 이동 |
| `Esc`, 우클릭 | 취소 |

고정한 이미지는 드래그로 이동, 휠로 확대, 더블클릭이나 `Esc`로 닫음

## OCR

- PaddleOCR PP-OCRv5 모델을 ONNX Runtime(CPU)으로 실행. 한국어(+영어)가 기본 포함
- OCR 할 때만 모델을 메모리에 올리고 끝나면 바로 해제함
- 인식 중에는 트레이 아이콘이 주황색으로 바뀌고, 끝나면 알림 없이 클립보드에 복사됨
- 트레이 메뉴 `OCR 설정`에서 문단으로 합치기, 결과 창 표시, OCR 언어 선택
- `언어 추가…`로 중국어, 일본어, 영어, 라틴, 키릴, 아랍, 태국어 등을 받을 수 있음 (`%LOCALAPPDATA%\ScreenCapture\models`)
- PaddleOCR가 실패하면 Windows OCR로 대신 인식함

## 요구 사항

- Windows 10 1809 이상 / Windows 11, x64
- 실행 파일에 .NET 런타임이 들어 있어서 별도 설치는 필요 없음
- `ScreenCapture.exe` 옆에 `models` 폴더가 있어야 PaddleOCR가 동작함

## 빌드

.NET 8 SDK가 필요함. OCR 모델은 레포에 없어서 먼저 받아야됨 (SHA-256 검증)

`powershell`
```powershell
powershell -ExecutionPolicy Bypass -File tools\get-models.ps1
dotnet publish ScreenCapture.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

결과물은 `publish\ScreenCapture.exe`와 `publish\models\`

## 설정

- 캡처 파일은 기본으로 `사진\Screenshots`에 저장됨
- 설정 `%APPDATA%\ScreenCapture\settings.json`, 오류 기록 `%APPDATA%\ScreenCapture\error.log`
