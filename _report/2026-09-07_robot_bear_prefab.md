# 로봇 곰 프리팹

- 프리팹: `Assets/Resources/Prefabs/dollPrefabs/doll_bear2_full_prefab.prefab`
- 원본 `doll_bear_full_prefab`을 상속하는 Unity Prefab Variant.
- 전용 메시·머티리얼·1024×512 알베도 텍스처: `Assets/Resources/Model/RobotBear/`
- 셰이더: 원본과 동일한 `Custom/URP/GhibliSoft`. 로봇 재질에 맞춰 머티리얼 색상·반사 강도를 설정했다.
- 디자인: 둥근 모서리의 장갑, 원형 곰 귀, 얼굴 디스플레이, 코와 입 그릴, 가슴 표시등, 분리된 손·발, 뒤쪽 정비 패널과 안테나.
- 뽑기용 고정 메시와 전투용 스킨 메시를 모두 교체했다. 기존 Animator Controller와 11개 뼈대를 재사용한다.
- 정점 10,980개, 삼각형 11,648개, 머티리얼 1개, 형태에 맞춘 충돌체 9개.
- 정지 상태에서 스킨 메시와 고정 메시가 일치하는지 검사했다. 37개 정지/애니메이션 표본으로 렌더링 경계를 계산하고, 공격 애니메이션의 실제 메시 변형도 확인했다.
- 원본 프리팹·모델·머티리얼은 변경하지 않았다. 게임 테이블에 새 인형 종류를 등록하거나 기존 스폰 대상을 바꾸는 작업은 포함하지 않았다.

Unity Project 창에서 새 프리팹을 열어 확인하거나 씬에 드래그해 사용할 수 있다. 재질 색상은 전용 머티리얼에서 조정한다. 메시를 다시 만들기 위한 에디터 제작 코드는 `Assets/Editor/RobotBearBuilder.cs`에 있다. 제작 메뉴는 이미 존재하는 프리팹을 덮어쓰지 않는다.

미리보기: `doll_bear2_robot_preview.png`, `doll_bear2_robot_rear.png`, `doll_bear2_robot_attack.png`.
에디터 렌더링과 구조 검사를 완료했으며, 실기기 뽑기·전투 플레이 검증은 별도로 필요하다.
