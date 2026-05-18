# NTFS Cleanup Candidate Integration

정리 후보·미리보기 기능과 NTFS 실험 진단을 연결하기 전에, 필요한 전제와 단계를 한곳에 정리한 문서입니다. 이 저장소의 **현재 구현**과 **아직 하지 않는 것**을 구분해 둡니다.

## Current state

- 정리 후보 탐지(`CleanupCandidateDetectionService`)와 후보 크기 확인(`ReadOnlyDirectorySizeService`), 정리 미리보기(`CleanupPreviewService`)는 모두 **일반 `Directory` / `FileInfo` 재귀 열거**를 씁니다.
- NTFS 관련 기능(USN 열거, FRN 트리 골격, OpenFileById 샘플 등)은 **실험 패널**에만 있으며, 후보 목록의 크기 숫자나 미리보기 합계에 **자동으로 반영되지 않습니다**.
- `NtfsPathMappingProbeService`는 **경로가 USN 기록으로 만든 FRN 딕셔너리와 맞는지**만 확인하는 **PoC 진단**입니다. 용량 합산은 하지 않습니다.

## Why it is not connected yet

- `USN_RECORD`에는 **파일 크기 필드가 없어서**, USN만으로 후보 폴더 용량을 알 수 없습니다.
- FRN / ParentFRN은 **전체 경로 문자열**이 아니라, 볼륨 안에서의 **부모–자식 식별자**입니다. 사용자에게 보이는 `C:\…` 경로와 바로 대응되지 않습니다.
- 특정 후보 경로를 트리의 노드와 **매핑**한 뒤에야, 그 아래 레코드만 골라 크기를 물을지(예: OpenFileById) 같은 다음 단계를 설계할 수 있습니다.
- 크기 조회 전략은 **OpenFileById**, **raw MFT attribute** 등 여러 갈래가 있고, 각각 권한·성능·정확도 트레이드오프가 다릅니다.
- 최종적으로는 **Directory 기반 합계와의 비교**가 없으면 사용자 신뢰를 주기 어렵습니다.

## Required steps (통합을 염두에 둔 순서)

1. 볼륨별로 USN을 읽어 **FRN → 레코드** 맵을 구성한다(이미 트리 진단에서 부분적으로 수행).
2. **루트 경로**(`C:\` 등)에서 후보 **전체 경로**까지, 이름 세그먼트와 ParentFRN을 따라 올라가며 **동일 경로인지** 확인한다(경로 매핑 PoC).
3. 매핑된 노드를 루트로 한 **하위 서브트리**에 해당하는 USN 레코드만 골라낸다(이번 단계 범위 밖).
4. 파일 크기 조회 전략(OpenFileById 샘플/확장, MFT 등)을 선택한다.
5. **Directory 기반** 후보 크기·미리보기와 **숫자·항목 수**를 비교하는 절차를 마련한다.
6. 실패·부분 스캔·권한 거부 시에는 **기존 Directory 기반 결과만** 쓰는 **fallback**을 유지한다.

## Current decision

- 당장은 **Directory 기반** 정리 후보·미리보기를 **유지**한다.
- NTFS 기반 후보 계산은 **실험 진단**(경로 매핑 PoC 등)으로만 검증하고, UI·문서에 **아직 연결하지 않았음**을 명시한다.
- 삭제·정리 실행·dry-run 결과를 NTFS 결과로 **대체하거나 자동 반영하지 않는다.**

## Related files

- 설계·배경: 이 문서, `docs/NTFS_FAST_SCAN_RESEARCH.md`, `docs/PERFORMANCE_AUDIT_WINDIRSTAT.md`
- 구현: `NtfsPathMappingProbeService`, `NtfsFastScanTreeProbeService`, 정리 후보 관련 서비스(현재 Directory 기반)
