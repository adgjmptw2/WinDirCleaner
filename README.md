# WinDirCleaner

Windows에서 드라이브 저장소를 **읽기만** 해서 보여 주는 WPF 앱입니다. 지우거나 정리해 주는 기능은 없고, dry-run 같은 것도 없습니다.

앱 안에 **데모 모드**가 있어서 실제 디스크 대신 샘플만 띄울 수 있습니다. 일반 상세 분석, **정리 후보 미리보기**, NTFS 실험 진단은 화면에서 구역이 나뉘어 있으며, NTFS 진단이 상세 분석을 대신하지는 않습니다. NTFS가 뭘 하고 안 하는지는 [docs/NTFS_FAST_SCAN_RESEARCH.md](docs/NTFS_FAST_SCAN_RESEARCH.md) 쪽을 보면 됩니다.

## 현재 기능(일반)

- 드라이브 이름, 유형, 용량 표시
- 고정·이동식 드라이브 루트의 **바로 아래** 항목 집계(순차 분석 기본, 선택 시 제한적 병렬)
- 분석 취소
- 항목별 파일 수, 폴더 수, 크기 표시
- 처리 시간이 긴 항목 요약

일반 분석은 `Directory` / `FileInfo` 순회입니다. 큰 드라이브에서는 시간이 걸릴 수 있습니다.

## 정리 후보 미리보기

- **권장 / 선택 / 위험**으로 나눈 정리 후보 목록을 **UI·정책 기반 프리뷰**로 보여 줍니다(현재는 **정적 샘플**이며 실제 스캔 결과가 아님).
- **삭제·정리 실행·dry-run은 없습니다.** 위험 분류 항목은 **보호됨**으로 표시되고 체크할 수 없습니다.
- 체크한 항목 수와(미리보기에서 크기가 있는 경우)합계 크기만 하단에 집계합니다. “삭제 가능” 같은 표현은 쓰지 않습니다.

자세한 분류 정책은 [docs/SAFETY_POLICY.md](docs/SAFETY_POLICY.md)를 참고하세요.

## 실험 기능(NTFS 진단)

메뉴의 **진단용** 도구입니다. **기본 상세 분석을 바꾸거나 대체하지 않습니다.** 화면에는 **진단 요약 카드**와 세 구역으로 나뉘어, 각각의 목적·해석 문구가 함께 표시됩니다.

1. **레코드 열거** — USN 레코드를 얼마나 빠르게 읽는지
2. **트리 골격** — FRN / ParentFRN으로 연결 가능한지
3. **파일 크기 샘플** — `OpenFileById`로 일부 파일만 골라 크기 조회 가능성·속도·실패 비율(기본 500, 최대 50,000 벤치마크)

- 트리 진단의 `FileRecords`와 샘플의 `FilesPerSecond`로 **선형 추정**한 “전체 조회에 가까운 시간”을 보여 줄 수 있으나, **실제 전 볼륨 집계나 전체 조회 실행 결과가 아닙니다.**

자세한 범위·한계는 [docs/NTFS_FAST_SCAN_RESEARCH.md](docs/NTFS_FAST_SCAN_RESEARCH.md), 성능·지표 해석은 [docs/PERFORMANCE_AUDIT_WINDIRSTAT.md](docs/PERFORMANCE_AUDIT_WINDIRSTAT.md)를 참고하세요.

## WinDirStat과의 관계

[WinDirStat](https://windirstat.net/)은 별도의 **GPL-2.0** 프로젝트입니다. 이 저장소는 WinDirStat **코드를 복사하거나 포팅하지 않았으며**, UI·아키텍처·성능 관점의 **참고·비교**만 문서에 남깁니다.

## 안전 원칙

- 파일을 만들거나, 수정하거나, 이동하거나, 삭제하지 않습니다.
- 네트워크 전송·텔레메트리·백그라운드 상주·시작 프로그램 등록은 하지 않습니다.
- [docs/SAFETY_POLICY.md](docs/SAFETY_POLICY.md), [SECURITY.md](SECURITY.md) 참고

## 제공하지 않는 기능

- 파일 삭제, 삭제 미리보기(dry-run), 정리 후보 자동 분류·일괄 삭제
- 전체 트리 시각화, 최근 폴더 저장
- NTFS/USN 결과를 일반 상세 분석 결과로 합치기
- 볼륨 전체 OpenFileById 크기 스캔, NTFS 기반 폴더별 용량 집계

## 로드맵

[ROADMAP.md](ROADMAP.md)

## 개발

```bash
dotnet restore WinDirCleaner.sln
dotnet build WinDirCleaner.sln -c Release
dotnet test WinDirCleaner.sln -c Release --no-build
```

## 문서

- [안전 정책](docs/SAFETY_POLICY.md)
- [성능 메모](docs/PERFORMANCE_AUDIT_WINDIRSTAT.md)
- [NTFS fast scan 메모](docs/NTFS_FAST_SCAN_RESEARCH.md)
- [스크린샷](docs/screenshots/README.md)
