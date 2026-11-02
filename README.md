# AutoKeyPress · 가상 HID 키보드 개선판

개별 키 반복과 문장·여러 키 반복을 한 화면에서 설정하고, 라이브 키보드로 동작을 확인합니다. 설정은 실행 파일 옆 `settings.json`에 저장되며 가져오기·내보내기를 지원합니다.

## 다운로드와 실행

**[Windows 64비트 실행본 다운로드](https://github.com/gittaeo/AutokeyPress/releases/latest/download/AutoKeyPress-win-x64.zip)** · [릴리스 목록](https://github.com/gittaeo/AutokeyPress/releases)

1. 위 ZIP을 다운로드한 뒤 쓰기 가능한 폴더에 **전체 압축 해제**합니다. GitHub의 `Code → Download ZIP`은 소스 코드이므로 실행 파일이 없습니다.
2. `Start.cmd`를 더블클릭하고 Windows 관리자 권한 요청을 허용합니다.
3. 처음 사용하는 PC에서는 포함된 서명된 USB/IP 드라이버를 설치합니다. 재부팅 안내가 나오면 Windows를 재부팅한 뒤 `Start.cmd`를 다시 실행하세요. 자동 재부팅은 하지 않습니다.
4. 앱 상단 **HID 연결**을 누릅니다. 연결 완료 후 반복할 키·주기를 설정하세요.
5. 대상 창으로 이동한 뒤 지정한 전체 시작 단축키로 실행합니다. **Esc**로 중지합니다. 시작 단축키와 반복할 키는 다르게 지정하세요.

Windows 10 64비트(1903 이상) 또는 Windows 11 x64용입니다. .NET 런타임을 포함하므로 .NET·Python·Arduino·라즈베리파이를 별도로 설치하거나 연결할 필요가 없습니다. 드라이버 설치와 실행에는 관리자 권한이 필요합니다. 앱 자체는 코드 서명이 없어 Windows에서 알 수 없는 게시자로 표시될 수 있습니다.

## 입력 방식과 제한

`AutoKeyPress → 로컬 VIIPER 서버 → USBIP-WIN2 가상 USB 호스트 → Windows HID 키보드`

별도 물리 키보드 없이 가상 HID 장치를 만듭니다. SendInput으로 자동 대체하지 않으며, 드라이버·장치 연결이 없으면 실행을 차단합니다. 보조 서버는 이 PC의 루프백 주소만 사용합니다. 앱 종료 시 자신이 만든 연결과 보조 서버를 정리합니다.

문장 입력은 영문·숫자·기호를 키 위치와 Shift 조합으로 전송합니다. 대상 창의 입력기를 영문으로 두세요. 한글 등 Unicode 문자열 직접 입력은 지원하지 않습니다. 가상 HID도 가상 장치이므로 모든 프로그램에서 물리 키보드와 동일하게 처리된다고 보장하지 않습니다.

## 검증

설정 저장·복원, HID 매핑·동시 누름·해제, 중지 정리와 화면 배치 등 코드 검사 43개를 통과했습니다. 개발 PC에서 서명된 드라이버 설치와 Windows 가상 HID 키보드 등록, 중립 보고서 전송 및 장치 정리를 확인했습니다. 다른 PC와 모든 대상 앱에서의 동작은 별도 확인이 필요합니다.

## 소스와 빌드

Windows에서 .NET 8 SDK를 설치한 뒤 `powershell -ExecutionPolicy Bypass -File .\build.ps1`을 실행합니다. 배포 ZIP은 `dist/AutoKeyPress-win-x64.zip`에 생성됩니다.

검사: `dotnet run --project tests/SettingsChecks.csproj -c Release`

개인 설정과 설치 로그는 배포하지 않습니다. 업데이트할 때는 기존 `settings.json`을 별도로 보관한 뒤 새 실행 폴더로 옮길 수 있습니다.

## 포함된 외부 구성요소

- [VIIPER v0.9.0](https://github.com/Alia5/VIIPER/releases/tag/v0.9.0): 별도 프로세스로 실행, GPL-3.0. 라이선스와 해당 버전 소스 ZIP은 `HidRuntime`에 포함합니다.
- [usbip-win2 v0.9.8.1](https://github.com/vadimgrn/usbip-win2/releases/tag/v.0.9.8.1): 서명된 드라이버 설치 프로그램, 라이선스와 해당 버전 소스 ZIP을 포함합니다.
- 구성요소 SHA-256은 `HidRuntime/package-manifest.json`에 기록되어 있습니다. 드라이버 설치 전 해시와 서명자를 검사합니다.
