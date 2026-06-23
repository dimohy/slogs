# slogs

`slogs`는 빠른 읽기, Markdown 글쓰기, 작성자 탐색, 개인 지식 흐름을 중심에 둔 한국어 개발자 블로그 서비스입니다.

영문 문서: [README.en.md](README.en.md)

## 소개

GitHub 저장소 소개 문구:

> Markdown 글쓰기, 개인 Slogger 홈, 태그/시리즈 탐색, 소셜 읽기 흐름, AI Agent용 LLM Wiki를 갖춘 한국어 개발자 블로그 서비스.

추천 주제:

`blazor`, `dotnet`, `markdown`, `blog`, `postgresql`, `pgvector`, `llm-wiki`, `mcp`, `korean`

## 주요 기능

- 최신, 트렌딩, 추천, 팔로우 피드 기반의 글 탐색
- 실시간 미리보기, 이미지 삽입, 태그, 시리즈, 게시 전 저장, 게시 후 리비전을 지원하는 Markdown 작성/편집
- 공개 프로필, 글 아카이브, 팔로워/팔로잉을 포함한 Slogger 개인 홈
- 댓글, 답글, 좋아요, 북마크, 팔로우 중심의 읽기 상호작용
- 내 글, 북마크, 좋아요, 설정, LLM Wiki를 관리하는 인증 사용자 영역
- AI Agent가 글 발행, 이미지 업로드, 글 삭제, LLM Wiki 기억 관리를 수행할 수 있는 Slogs MCP 도구
- PostgreSQL `pgvector`와 서버 로컬 EmbeddingGemma 모델을 사용하는 LLM Wiki 검색/회상

## 기술 스택

- .NET 11 Preview, C# preview
- ASP.NET Core, Blazor `InteractiveAuto`
- Entity Framework Core, Npgsql
- PostgreSQL 16, `pgvector`
- 브라우저 Markdown 작성/미리보기용 Vditor
- 로컬 LLM Wiki 임베딩을 위한 Ollama 기반 EmbeddingGemma
- 로컬 PostgreSQL과 EmbeddingGemma 컨테이너 실행을 위한 Podman

## 저장소 구조

- `src/Slogs`: ASP.NET Core 서버, REST API, EF Core 데이터 계층, 인증, MCP 도구
- `src/Slogs.Client`: Blazor 클라이언트 UI와 라우트 페이지
- `src/Slogs.Shared`: 공유 계약, API 클라이언트, 인증 상태, Markdown/SEO 헬퍼
- `src/Slogs.NativeAotProbe`: 실험용 NativeAOT 게시 검증 프로젝트
- `scripts`: 로컬 서비스, 배포, Slogs MCP 프롬프트 동기화 스크립트
- `infra/postgres`: PostgreSQL과 EmbeddingGemma용 Podman compose 서비스
- `artifacts`: 로컬 검증 결과와 생성 산출물. Git에는 포함하지 않습니다.

## 로컬 개발

전제조건:

- PowerShell
- `global.json`과 일치하는 .NET 11 Preview SDK
- Podman
- LLM Wiki 임베딩 서비스 실행 시 NVIDIA GPU와 CDI 지원

PostgreSQL 시작:

```powershell
scripts/Start-Postgres.ps1
```

앱 실행:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/Slogs/Slogs.csproj --launch-profile https
```

HTTPS 실행 프로필은 다음 주소를 사용합니다.

- `https://localhost:5000`
- `http://localhost:5001`

LLM Wiki 의미 기반 검색 또는 회상을 테스트할 때는 EmbeddingGemma를 시작합니다.

```powershell
scripts/Start-EmbeddingGemma.ps1
```

개발 DB 연결은 `src/Slogs/appsettings.Development.json`에 설정되어 있습니다.

## 빌드와 검증

솔루션 빌드:

```powershell
dotnet build Slogs.slnx -warnaserror
```

주요 동작 확인 경로:

- 공개 경로: `/`, `/recent`, `/trending`, `/recommended`, `/tag`, `/series`
- 인증 경로: `/login`, `/register`, `/me`, `/write`
- LLM Wiki 경로: `/me/llm-wiki`

## 배포

일반 배포에서는 WebAssembly AOT를 기본으로 사용하지 않습니다.

```powershell
scripts/Deploy-Slogs.ps1
```

명시적인 AOT 배포가 필요할 때만 `-WasmAot`를 사용합니다.

```powershell
scripts/Deploy-Slogs.ps1 -WasmAot
```

## 라이선스

이 프로젝트는 Apache License 2.0으로 배포됩니다. 자세한 내용은 [LICENSE](LICENSE)를 참고하세요.
