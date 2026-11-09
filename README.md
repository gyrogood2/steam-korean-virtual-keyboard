# Steam 한글 가상 키보드

<p align="center"><img src="docs/icon.png" width="120" alt="Steam Korean Virtual Keyboard"></p>

<p align="center">Steam 가상 키보드에서 <b>한글을 보고 입력</b>할 수 있게 해줍니다.</p>

## 이게 뭔가요?

Steam에서 글자를 입력하면 **가상 키보드**가 뜹니다.
이 키보드는 영문 자판만 표시해서, 한글을 쓰려면 자모 위치를 외워야 했습니다.

이 프로그램을 켜면 키보드가 이렇게 바뀝니다.

![한글이 표시된 가상 키보드](docs/keyboard-ko.png)

<sub>실제 가상 키보드에 한글 자모가 표시된 모습 (사용 중인 스킨)</sub>

- 자판에 한글이 보입니다 (`ㅂㅈㄷㄱ…`), Shift를 누르면 `ㄲㄸㅃㅆ`
- **R1** = 한/영 전환
- **L1** = 표기만 바꿔서 화면과 실제를 맞춤
- 스페이스바 오른쪽에 현재 상태(`한`/`영`)가 표시됩니다

가상 키보드가 지원되는 곳에서 작동합니다.

Steam 프로그램 파일은 건드리지 않습니다. 켜져 있는 동안 화면만 바꿔 줍니다.

## 시작하기 전에

설치하면 Steam 폴더에 **표시 파일 하나**(`.cef-enable-remote-debugging`)를 추가합니다.
Steam은 이 파일이 있으면 켜질 때 **다른 프로그램이 접근할 수 있는 문**을 열어 둡니다.

덕분에 **평소처럼 Steam을 켜도** 이 프로그램이 바로 붙습니다.
(바탕화면의 `Steam (Korean)` 바로가기로 켜도 똑같이 동작합니다.)

> 포터블은 첫 실행 때 관리자 권한을 한 번만 요청합니다.
> 이 파일은 Steam 프로그램 자체를 수정하지 않습니다. 지우면 원래대로 돌아갑니다.

## 설치

### 릴리스에서 설치 (권장)

1. [Releases](../../releases)에서 최신 `SteamKoreanKeyboard-*.zip` 다운로드
2. 압축을 풀고 `install.cmd` 실행 → UAC 창에서 **예**
3. Steam을 평소처럼 실행하고, 가상 키보드를 열어 사용

설치 위치: `C:\Program Files (x86)\Steam\steam-osk-korean`

### 포터블 (파일 하나)

1. `SteamKoreanKeyboard.exe` 실행
2. 처음 한 번 관리자 권한 요청 → **예**
3. Steam을 완전히 종료했다가 다시 실행
4. 트레이 메뉴에서 **자동** 선택

## 사용법

### 트레이 메뉴

| 항목 | 설명 |
|---|---|
| **자동** | 창마다 한/영 상태를 따라갑니다 (메모장·탐색기 같은 앱은 읽지 못하는 경우도 있습니다) |
| **수동** | 상태를 따라가지 않고, 직접 바꿉니다 |
| **Off** | 잠시 끄고 원래 키보드로 돌아갑니다 |
| **Windows 시작 시 실행** | 컴퓨터를 켤 때 자동으로 실행 |
| **종료** | 프로그램 종료 |

### 키

| 키 | 동작 |
|---|---|
| **R1** | 한/영 전환 |
| **L1** | 표시 전환 (표기만) |
| **L2** | Shift (쌍자음·쌍모음) |
| **CapsLock** | 한글: 쌍자음 잠금 · 영문: 대문자 |

### 스페이스바

스페이스바 오른쪽에 키 역할이 표시됩니다.

```
L1 표시 전환   R1 한/영   한
```

## 한/영 표기가 반대일 때

한/영 상태를 100% 자동으로 맞추는 것은 불가능합니다.

- 한/영 상태는 **각 프로그램**이 가지고 있어서 밖에서 정확히 읽을 수 없습니다
- 작업표시줄 입력 표시기나 `Win+Space`로 바꾸면 이 프로그램은 알 수 없습니다

그래서 **L1(표시 전환)** 이 있습니다.
표기가 실제와 반대일 때 L1을 한 번 누르면 **입력은 그대로 두고 표기만** 뒤집혀서 화면과 실제가 맞습니다.

| 키 | 바꾸는 것 |
|---|---|
| **R1** | 실제 한/영 상태 + 표기 |
| **L1** | 표기만 |

## 삭제

`Steam\steam-osk-korean\uninstall.cmd` 실행.

- 설치 폴더, 바탕화면 바로가기, 시작 프로그램 등록을 지웁니다
- Steam 폴더의 `.cef-enable-remote-debugging` 표시 파일도 지웁니다
- **Steam 프로그램 자체는 건드리지 않습니다**

## 만든 방식 (개발자용)

<details>
<summary>펼치기</summary>

### 동작 원리

```
Steam 가상 키보드 (웹 페이지)
  ├─ CSS/JS 주입: 자모 표시, L1/R1, 스페이스 표시
  └─ 글자 입력 → 로컬 서버(8793) → 키 이벤트 전송
        ▲
SteamKoreanKeyboard.exe (C#)
  ├─ Steam의 디버그 포트(8080)로 접속
  ├─ 키 입력 가로채기 (ControllerKeyboardSendText)
  └─ 한/영 상태 추적 + 전환
```

- Steam 파일 수정 없음, 후킹·드라이버·서비스 없음
- 네트워크 통신 없음 (127.0.0.1 로컬만)
- 로컬 API: `/state`, `/hangul`, `/flip`, `/key` (실행할 때마다 새로 만드는 토큰 필요)
- Steam 폴더의 `.cef-enable-remote-debugging` 표시 파일이 디버그 포트(8080)를 엽니다

> **보안 참고**
> 이 표시 파일은 Steam의 디버그 포트를 **127.0.0.1(내 PC)** 에만 엽니다. 외부 네트워크에서는 접근할 수 없습니다.
> 같은 PC의 다른 프로그램은 Steam 화면에 접근할 수 있게 되므로, 신뢰할 수 없는 프로그램을 쓰는 PC라면 파일을 지우고 Steam을 재시작하세요.
> 앱의 로컬 API는 실행 시 발급되는 랜덤 토큰으로 보호됩니다.

### 기술 요구 사항

- Windows 10 / 11 (.NET Framework 4.8 내장)
- Steam 디버그 포트(8080)가 열려 있어야 함
- Windows 한국어 IME (2벌식)

### 빌드

```bat
build.cmd     :: exe 빌드
release.cmd   :: 릴리스 zip 생성
```

- .NET Framework 4.x의 `csc.exe`만 사용합니다 (외부 패키지 없음)
- 아이콘 재생성: `python tools/make-icon.py`
- README 미리보기: `preview.cmd` (브라우저로 열림)

### 파일 구성

| 파일 | 설명 |
|---|---|
| `SteamKoreanKeyboard.exe` | 엔진 + 트레이 UI (payload 내장) |
| `payload.js` | 가상 키보드 주입 스크립트 (exe에 내장, 같은 폴더에 있으면 우선 사용) |
| `start.cmd` | Steam을 디버그 포트로 실행 + 앱 시작 (설치형 바로가기용) |
| `install.cmd` / `uninstall.cmd` | 설치 / 삭제 |
| `app.ico` | 아이콘 (빌드 시 exe에 포함) |
| `docs/icon.png` | README용 아이콘 |
| `tools/make-icon.py` | 아이콘 재생성 스크립트 |
| `release.ps1` | 릴리스 zip 생성 |

</details>

## 라이선스

MIT License — [LICENSE](LICENSE)

이 프로젝트는 Valve Corporation과 무관하며, Valve의 공식 소프트웨어가 아닙니다.
Steam 및 관련 상표는 Valve Corporation의 자산입니다.
