# PlayHouse

[![Build Status](https://github.com/user/playhouse/workflows/CI/badge.svg)](https://github.com/user/playhouse/actions)
[![License](https://img.shields.io/badge/License-Apache%202.0%20with%20Commons%20Clause-blue.svg)](LICENSE)

**PlayHouse**는 실시간 멀티플레이어 게임을 위한 고성능 멀티 언어 게임 서버 프레임워크입니다.

## 특징

- **다중 언어 지원**: .NET, Java, C++, Node.js 서버 구현
- **크로스 플랫폼 커넥터**: Unity, Unreal Engine, JavaScript 클라이언트
- **액터 모델**: Stage 기반 액터 아키텍처
- **Protocol Buffers**: 효율적인 바이너리 직렬화
- **고성능**: 저지연 실시간 통신에 최적화

## 프로젝트 구조

```
playhouse/
├── protocol/           # 공유 프로토콜 정의 (protobuf)
├── servers/            # 서버 구현체
│   ├── dotnet/         # .NET 서버
│   ├── java/           # Java 서버
│   ├── cpp/            # C++ 서버
│   └── nodejs/         # Node.js 서버
├── connectors/         # 클라이언트 커넥터
│   ├── csharp/         # C# 커넥터 (Unity, .NET)
│   ├── cpp/            # C++ 커넥터 (Unreal)
│   ├── java/           # Java 커넥터
│   ├── javascript/     # JavaScript 커넥터
│   ├── unity/          # Unity 패키지
│   └── unreal/         # Unreal 플러그인
├── docs/               # 문서
├── examples/           # 샘플 프로젝트
└── tools/              # 개발 도구
```

## 빠른 시작

### .NET 서버

```bash
cd servers/dotnet
dotnet build
dotnet run --project src/PlayHouse
```

### Unity 클라이언트

git URL로 패키지 추가:
```
https://github.com/user/playhouse.git?path=connectors/unity
```

## 문서

- [아키텍처 개요](docs/architecture/overview.md)
- [시작하기](docs/servers/dotnet/getting-started/)
- [API 레퍼런스](docs/api/)

## 라이선스

이 프로젝트는 Apache License 2.0 with Commons Clause로 라이선스됩니다. 자세한 내용은 [LICENSE](LICENSE)를 참조하세요.

**요약**:
- 내부 사용, 수정, 재배포 무료
- 상업적 SaaS/호스팅 서비스는 별도 라이선스 필요
- 상업용 라이선스 문의는 연락 바랍니다

## 기여

기여 가이드라인은 [CONTRIBUTING.md](CONTRIBUTING.md)를 참조하세요.

## 보안

보안 취약점 보고는 [SECURITY.md](SECURITY.md)를 참조하세요.
