# Unreal Engine 설치 가이드 (Windows 11 + WSL2 Ubuntu 24.04)

## 목적

- Unreal 커넥터 E2E 테스트를 실행할 수 있는 환경을 확보한다.
- Windows 네이티브 설치를 1순위로 권장한다.
- WSL2 설치는 헤드리스/빌드 목적일 때만 선택한다.

## 1) Windows 네이티브 설치 (권장)

### 장점
- 가장 안정적이고 빠른 테스트 실행
- 에디터 실행/디버깅 가능

### 설치 절차
1. Epic Games Launcher 설치
2. UE 5.3+ 설치
3. `UnrealEditor-Cmd.exe` 경로 확인

예시 경로:
```
C:\Program Files\Epic Games\UE_5.3\Engine\Binaries\Win64\UnrealEditor-Cmd.exe
```

### 테스트 실행 (로컬)
```
UE_EDITOR_CMD="/mnt/c/Program Files/Epic Games/UE_5.3/Engine/Binaries/Win64/UnrealEditor-Cmd.exe" \
UPROJECT_PATH="/mnt/c/Work/MyGame/MyGame.uproject" \
./connectors/unreal/scripts/run-local-e2e.sh
```

## 2) WSL2 Ubuntu 24.04 설치 (헤드리스/빌드 목적)

### 주의사항
- 설치/빌드 시간이 길다
- 디스크 사용량이 크다
- 에디터 GUI 실행은 비효율적

### 개요
- UE 소스 빌드 방식으로만 설치 가능
- Engine 빌드 후 `UnrealEditor-Cmd`를 헤드리스로 실행

### 절차 요약
1. Epic ↔ GitHub 계정 연동
2. UE 소스 클론
3. Dependencies 설치
4. UE 빌드

### 의존성 설치
```
sudo apt update
sudo apt install -y build-essential clang lld cmake ninja-build python3 python3-pip \
  libvulkan1 libvulkan-dev libxkbcommon-x11-0 libxkbcommon-dev libx11-dev libxcb1-dev
```

### 소스 클론
```
# Epic GitHub 접근 권한 필요
# https://github.com/EpicGames/UnrealEngine

git clone --depth=1 -b 5.3 https://github.com/EpicGames/UnrealEngine.git
```

### 빌드
```
cd UnrealEngine
./Setup.sh
./GenerateProjectFiles.sh
make -j$(nproc)
```

### 헤드리스 테스트 실행
```
./Engine/Binaries/Linux/UnrealEditor-Cmd \
  /path/to/MyGame.uproject \
  -ExecCmds="Automation RunTests PlayHouse.*;Quit" \
  -unattended -nopause -nosplash
```

## 3) UE 컨테이너 사용 (대안)

### 특징
- 재현성이 좋지만 느림
- Epic 컨테이너 접근 권한 필요

### 핵심 조건
- Epic ↔ GitHub 연동
- GHCR 로그인

### 실행
```
UE_DOCKER_IMAGE="ghcr.io/epicgames/unreal-engine:5.3" \
UPROJECT_PATH="/mnt/c/Work/MyGame/MyGame.uproject" \
./connectors/unreal/scripts/run-docker-e2e.sh
```

## 권장 선택

1. Windows 네이티브 설치 (가장 빠르고 안정적)
2. UE 컨테이너 (CI/재현성 필요 시)
3. WSL 소스 빌드 (최후의 수단)

## Windows에서 이어서 진행할 작업 (현재 완료/남은 일)

### 완료된 항목

- Unreal 커넥터 코어 로직 구현
- TCP / WS / WSS 전송 구현
- TCP+TLS 전송 구현 (OpenSSL 기반, WITH_SSL 필요)
- 코어 자동화 테스트 추가
- E2E 자동화 테스트 추가 (TCP/WS/WSS/TLS)
- Docker 기반 테스트 문서 및 스크립트 준비

### Windows에서 해야 할 작업

1) **Unreal Engine 설치**
   - UE 5.3+ 설치
   - `UnrealEditor-Cmd.exe` 경로 확인

2) **프로젝트/플러그인 빌드 확인**
   - 플러그인 포함된 프로젝트 빌드
   - 오류 발생 시 Build.cs 모듈 의존성(SSL/WebSockets/Networking/Sockets) 확인

3) **테스트 서버 실행**
   - Docker로 test-server 실행
   - TLS/WSS 활성화 확인

4) **UE Automation 테스트 실행**
   - TCP/WS/WSS/TLS 테스트 실행
   - 로그 확인 및 실패 시 재현

### 실행 커맨드 예시

```
UE_EDITOR_CMD="C:\\Program Files\\Epic Games\\UE_5.3\\Engine\\Binaries\\Win64\\UnrealEditor-Cmd.exe" ^
UPROJECT_PATH="C:\\Work\\MyGame\\MyGame.uproject" ^
./connectors/unreal/scripts/run-local-e2e.sh
```

### 체크리스트 (통과 기준)

- `PlayHouse.E2E.Tcp` 통과
- `PlayHouse.E2E.Ws` 통과
- `PlayHouse.E2E.Wss` 통과
- `PlayHouse.E2E.Tls` 통과
