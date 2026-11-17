<div align="center">
    <picture>
        <img src="docs/icon.png" width="96" alt="icon">
    </picture>
</div>

# 화면 캡처

- WinForms (.NET 8), 트레이 상주
- 영역, 창, 전체 화면 캡처 (다중 모니터, 고DPI 대응)
- 사각형, 원, 화살표, 펜, 텍스트, 모자이크 그리기 후 복사, 저장, 화면에 고정
- 오프라인 OCR (PaddleOCR), 인식한 글자는 바로 클립보드로 복사됨
- 돋보기에서 색상값 `#RRGGBB` 복사

## 사용법

1. [Releases](../../releases/latest)에서 `ScreenCapture-vX.Y.Z-win-x64.zip`을 받아 압축 풀기. `ScreenCapture.exe`와 `models` 폴더는 같은 폴더에 둬야됨 (`models`가 없으면 OCR이 Windows OCR로 대체됨)
2. `ScreenCapture.exe` 실행. 창 없이 작업 표시줄 트레이에 아이콘만 생김
3. `Ctrl+Shift+A`를 누르거나 트레이 아이콘을 클릭하면 화면이 멈추고 캡처 화면이 뜸
4. 드래그로 영역을 고르거나, 창 위에서 클릭해서 창을 고름. 고른 뒤 아래 툴바로 그리기, 복사, 저장, OCR
5. 결과는 클립보드에 들어가니 원하는 곳에 `Ctrl+V`

단축키를 다른 프로그램이 쓰고 있으면 실행할 때 알려줌. 트레이 메뉴 `Settings...`에서 바꾸면 됨

### 캡처 화면

| 키 | 동작 |
| --- | --- |
| 드래그 / 창 위에서 클릭 | 영역 / 창 선택 |
| 선택 영역 안에서 드래그, 모서리 드래그, 방향키 | 이동, 크기 조절, 1px 이동 |
| `Enter`, 더블클릭, `Ctrl+C` | 이미지를 클립보드로 복사하고 닫기 |
| `Ctrl+S` | 파일로 저장 (기본은 저장 위치를 물어봄) |
| `Ctrl+T` | OCR, 글자를 클립보드로 복사 |
| `C` | 돋보기 가운데 색상을 `#RRGGBB`로 복사하고 닫기 |
| `Ctrl+Z` | 그린 것 되돌리기 |
| `Esc`, 우클릭 | 취소 (우클릭은 선택이 있으면 선택만 해제) |

툴바의 핀 버튼은 캡처를 화면 위에 고정함. 고정한 이미지는 드래그로 이동, 휠로 확대, 더블클릭이나 `Esc`로 닫고 우클릭으로 복사, 저장

### 트레이 메뉴

| 메뉴 | 내용 |
| --- | --- |
| Region capture | 영역 캡처 (트레이 아이콘 클릭과 같음) |
| OCR capture (extract text) | 영역을 고르면 바로 OCR 해서 글자를 복사 |
| Full screen capture | 전체 화면을 복사하고 `사진\Screenshots`에 PNG로 저장 |
| OCR 설정 | 문단으로 합치기, 결과 창 표시, OCR 언어 (선택, `언어 추가…`, `언어 삭제…`) |
| Open screenshots folder | 저장 폴더 열기 |
| Settings... | 저장 폴더, 단축키, 저장 방식, Windows 시작 시 실행 |
| Exit | 종료 |

## OCR

- PaddleOCR PP-OCRv5 모델을 ONNX Runtime(CPU)으로 실행. 한국어(+영어)가 기본 포함
- OCR 할 때만 모델을 메모리에 올리고 끝나면 바로 해제함
- 인식 중에는 트레이 아이콘이 주황색으로 바뀌고, 끝나면 알림 없이 클립보드에 복사됨
- `언어 추가…`로 중국어, 일본어, 영어, 라틴, 키릴, 아랍, 태국어 등을 받을 수 있음 (`%LOCALAPPDATA%\ScreenCapture\models`)
- PaddleOCR가 실패하면 Windows OCR로 대신 인식함

## 요구 사항

- Windows 10 1809 이상 / Windows 11, x64
- 실행 파일에 .NET 런타임이 들어 있어서 별도 설치는 필요 없음

## 빌드

.NET 8 SDK가 필요함. 첫 줄은 OCR 모델 받기, 둘째 줄은 빌드

`powershell`
```powershell
powershell -ExecutionPolicy Bypass -File tools\get-models.ps1
dotnet publish ScreenCapture.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

`publish` 폴더에 `ScreenCapture.exe`와 `models`가 생기고, 그 `ScreenCapture.exe`를 실행

## 설정, 라이선스

- 설정 `%APPDATA%\ScreenCapture\settings.json`, 오류 기록 `%APPDATA%\ScreenCapture\error.log`
- MIT ([LICENSE](LICENSE)). OCR 모델과 ONNX Runtime 라이선스는 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
