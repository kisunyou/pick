using UnityEditor;
using UnityEngine;

namespace FunRabbit.EditorTools
{
    // 테스트용 스테이지 설정 메뉴 (에디터 전용). GameQuestManager 와 같은 PlayerPrefs 키를 쓴다.
    // 연속 28스테이지 (2026-11-05 판 재배치·8종 제외). 메뉴 이름의 동물은 actor.json 순서 기준 -
    // 판 구성을 바꾸면 Names 와 메뉴 항목을 함께 고친다. 28판을 깨면 1판으로 돌아가 반복하고, 회차마다 보스 능력치가 x1 -> x3 -> x6 ... 로 오른다.
    //   currentStage : 현재 스테이지(1~28)
    //   bossHp       : 보스 남은 hp - 스테이지를 바꾸면 삭제해 다음 읽기에서 새 스테이지 최대치로 채워지게 한다
    // - 플레이 모드: GameQuestManager.SetCurrentStage(stage) 로 즉시 전환(보스 hp 리셋 + 보스 모델 교체) + 인형 풀 재생성
    // - 편집 모드: PlayerPrefs 만 갱신 → 다음 플레이부터 적용
    // 아군 슬롯/대기열(AllySlot*)은 건드리지 않는다 - 스테이지가 바뀌어도 아군은 유지된다(정상 진행과 동일).
    public static class DebugStageMenu
    {
        const string KeyStage = "currentStage";        // GameQuestManager.KEY_STAGE
        const string KeyBossHp = "bossHp";             // GameQuestManager.KEY_BOSS_HP
        const string KeyLegacyCycle = "currentCycle";  // 구버전 회차 키 - 남아있으면 마이그레이션이 스테이지를 밀어버리므로 제거
        const string KeyMaxCleared = "maxClearedStage"; // GameQuestManager.KEY_MAX_CLEARED_STAGE - 도감 변형(_b/_r) 등급 판정 기준
        const string MenuRoot = "FunRabbit/Debug/스테이지/";

        // ── 1~10판 ─────────────────────────
        [MenuItem(MenuRoot + "스테이지  1 돼지", false, 101)] static void S1() => SetStage(1);
        [MenuItem(MenuRoot + "스테이지  2 소", false, 102)] static void S2() => SetStage(2);
        [MenuItem(MenuRoot + "스테이지  3 양", false, 103)] static void S3() => SetStage(3);
        [MenuItem(MenuRoot + "스테이지  4 문어", false, 104)] static void S4() => SetStage(4);
        [MenuItem(MenuRoot + "스테이지  5 오리", false, 105)] static void S5() => SetStage(5);
        [MenuItem(MenuRoot + "스테이지  6 레서판다", false, 106)] static void S6() => SetStage(6);
        [MenuItem(MenuRoot + "스테이지  7 나무늘보", false, 107)] static void S7() => SetStage(7);
        [MenuItem(MenuRoot + "스테이지  8 라마", false, 108)] static void S8() => SetStage(8);
        [MenuItem(MenuRoot + "스테이지  9 여우", false, 109)] static void S9() => SetStage(9);
        [MenuItem(MenuRoot + "스테이지 10 하마", false, 110)] static void S10() => SetStage(10);

        // ── 11~19판 ─────────────────────────
        [MenuItem(MenuRoot + "스테이지 11 개구리", false, 131)] static void S11() => SetStage(11);
        [MenuItem(MenuRoot + "스테이지 12 카피바라", false, 132)] static void S12() => SetStage(12);
        [MenuItem(MenuRoot + "스테이지 13 사슴", false, 133)] static void S13() => SetStage(13);
        [MenuItem(MenuRoot + "스테이지 14 너구리", false, 134)] static void S14() => SetStage(14);
        [MenuItem(MenuRoot + "스테이지 15 말", false, 135)] static void S15() => SetStage(15);
        [MenuItem(MenuRoot + "스테이지 16 원숭이", false, 136)] static void S16() => SetStage(16);
        [MenuItem(MenuRoot + "스테이지 17 강아지", false, 137)] static void S17() => SetStage(17);
        [MenuItem(MenuRoot + "스테이지 18 사자", false, 138)] static void S18() => SetStage(18);
        [MenuItem(MenuRoot + "스테이지 19 코뿔소", false, 139)] static void S19() => SetStage(19);

        // ── 20~28판 (28 클리어 시 1판으로, 회차 +1) ─────────────────────────
        [MenuItem(MenuRoot + "스테이지 20 코알라", false, 161)] static void S20() => SetStage(20);
        [MenuItem(MenuRoot + "스테이지 21 판다", false, 162)] static void S21() => SetStage(21);
        [MenuItem(MenuRoot + "스테이지 22 곰", false, 163)] static void S22() => SetStage(22);
        [MenuItem(MenuRoot + "스테이지 23 얼룩말", false, 164)] static void S23() => SetStage(23);
        [MenuItem(MenuRoot + "스테이지 24 멧돼지", false, 165)] static void S24() => SetStage(24);
        [MenuItem(MenuRoot + "스테이지 25 고릴라", false, 166)] static void S25() => SetStage(25);
        [MenuItem(MenuRoot + "스테이지 26 북극곰", false, 167)] static void S26() => SetStage(26);
        [MenuItem(MenuRoot + "스테이지 27 호랑이", false, 168)] static void S27() => SetStage(27);
        [MenuItem(MenuRoot + "스테이지 28 코끼리", false, 169)] static void S28() => SetStage(28);

        // ── 회차 (보스 배수 확인용): 고르면 현재 판의 보스를 그 회차 배수로 다시 채운다 ──
        const string KeyLoop = "stageLoop";   // GameQuestManager.KEY_LOOP
        [MenuItem(MenuRoot + "회차/1회차 (x1)", false, 250)] static void L1() => SetLoop(0);
        [MenuItem(MenuRoot + "회차/2회차 (x3)", false, 251)] static void L2() => SetLoop(1);
        [MenuItem(MenuRoot + "회차/3회차 (x6)", false, 252)] static void L3() => SetLoop(2);
        [MenuItem(MenuRoot + "회차/5회차 (x12)", false, 253)] static void L5() => SetLoop(4);
        [MenuItem(MenuRoot + "회차/10회차 (x27)", false, 254)] static void L10() => SetLoop(9);

        static void SetLoop(int loop)
        {
            PlayerPrefs.SetInt(KeyLoop, loop);
            PlayerPrefs.Save();
            SetStage(GetCurrentStage());
            Debug.Log($"[DebugStage] {loop + 1}회차 (보스 배수 x{GameQuestManager.BossStatMultiplier})");
        }

        [MenuItem(MenuRoot + "현재 스테이지 로그", false, 300)]
        static void LogCurrent()
        {
            int saved = PlayerPrefs.GetInt(KeyStage, -1);
            string live = Application.isPlaying && GameQuestManager.IsCheckInstance()
                ? $"{GameQuestManager.Instance.CurrentStage} ({GameQuestManager.Instance.CurrentCycle}회차 x{GameQuestManager.BossStatMultiplier}, 보스 {GameQuestManager.Instance.GetCurrentStageData()?.animalKey}, bossHp {GameQuestManager.Instance.BossHp}/{GameQuestManager.Instance.MaxBossHp})"
                : "(플레이 중 아님)";
            Debug.Log($"[DebugStage] 저장값={saved} (-1 = 키 없음 → 1), 현재 적용={live}, 총 스테이지={GameQuestManager.TotalPlayableStageCount}, 최고클리어={PlayerPrefs.GetInt(KeyMaxCleared, -1)}");
        }

        // ── 체크마크 (현재 스테이지 표시) ─────────────────────────────
        [MenuItem(MenuRoot + "스테이지  1 돼지", true)] static bool V1() => Check(1);
        [MenuItem(MenuRoot + "스테이지  2 소", true)] static bool V2() => Check(2);
        [MenuItem(MenuRoot + "스테이지  3 양", true)] static bool V3() => Check(3);
        [MenuItem(MenuRoot + "스테이지  4 문어", true)] static bool V4() => Check(4);
        [MenuItem(MenuRoot + "스테이지  5 오리", true)] static bool V5() => Check(5);
        [MenuItem(MenuRoot + "스테이지  6 레서판다", true)] static bool V6() => Check(6);
        [MenuItem(MenuRoot + "스테이지  7 나무늘보", true)] static bool V7() => Check(7);
        [MenuItem(MenuRoot + "스테이지  8 라마", true)] static bool V8() => Check(8);
        [MenuItem(MenuRoot + "스테이지  9 여우", true)] static bool V9() => Check(9);
        [MenuItem(MenuRoot + "스테이지 10 하마", true)] static bool V10() => Check(10);
        [MenuItem(MenuRoot + "스테이지 11 개구리", true)] static bool V11() => Check(11);
        [MenuItem(MenuRoot + "스테이지 12 카피바라", true)] static bool V12() => Check(12);
        [MenuItem(MenuRoot + "스테이지 13 사슴", true)] static bool V13() => Check(13);
        [MenuItem(MenuRoot + "스테이지 14 너구리", true)] static bool V14() => Check(14);
        [MenuItem(MenuRoot + "스테이지 15 말", true)] static bool V15() => Check(15);
        [MenuItem(MenuRoot + "스테이지 16 원숭이", true)] static bool V16() => Check(16);
        [MenuItem(MenuRoot + "스테이지 17 강아지", true)] static bool V17() => Check(17);
        [MenuItem(MenuRoot + "스테이지 18 사자", true)] static bool V18() => Check(18);
        [MenuItem(MenuRoot + "스테이지 19 코뿔소", true)] static bool V19() => Check(19);
        [MenuItem(MenuRoot + "스테이지 20 코알라", true)] static bool V20() => Check(20);
        [MenuItem(MenuRoot + "스테이지 21 판다", true)] static bool V21() => Check(21);
        [MenuItem(MenuRoot + "스테이지 22 곰", true)] static bool V22() => Check(22);
        [MenuItem(MenuRoot + "스테이지 23 얼룩말", true)] static bool V23() => Check(23);
        [MenuItem(MenuRoot + "스테이지 24 멧돼지", true)] static bool V24() => Check(24);
        [MenuItem(MenuRoot + "스테이지 25 고릴라", true)] static bool V25() => Check(25);
        [MenuItem(MenuRoot + "스테이지 26 북극곰", true)] static bool V26() => Check(26);
        [MenuItem(MenuRoot + "스테이지 27 호랑이", true)] static bool V27() => Check(27);
        [MenuItem(MenuRoot + "스테이지 28 코끼리", true)] static bool V28() => Check(28);

        // 메뉴 표시용 동물 이름 (actor.json 판 순서)
        static readonly string[] Names =
        {
            "돼지", "소", "양", "문어", "오리", "레서판다", "나무늘보", "라마", "여우", "하마",
            "개구리", "카피바라", "사슴", "너구리", "말", "원숭이", "강아지", "사자", "코뿔소",
            "코알라", "판다", "곰", "얼룩말", "멧돼지", "고릴라", "북극곰", "호랑이", "코끼리",
        };

        static string Label(int stage) => $"스테이지 {stage,2} {Names[stage - 1]}";

        static bool Check(int stage)
        {
            Menu.SetChecked(MenuRoot + Label(stage), GetCurrentStage() == stage);
            return true;
        }

        static int GetCurrentStage()
        {
            if (Application.isPlaying && GameQuestManager.IsCheckInstance())
                return GameQuestManager.Instance.CurrentStage;
            return PlayerPrefs.GetInt(KeyStage, 1);
        }

        // 스테이지를 고르면 바로 그 단계로 리셋한다 (플레이 중이면 보스/인형 즉시 재구성).
        static void SetStage(int stage)
        {
            int total = GameQuestManager.TotalPlayableStageCount;
            if (total <= 0)
            {
                Debug.LogError("[DebugStage] actor.json 스테이지 데이터를 읽을 수 없습니다.");
                return;
            }
            stage = Mathf.Clamp(stage, 1, total);

            // 구버전 회차 키가 남아있으면 CurrentStage 마이그레이션이 스테이지를 밀어버린다 - 항상 제거
            PlayerPrefs.DeleteKey(KeyLegacyCycle);

            if (Application.isPlaying && GameQuestManager.IsCheckInstance())
            {
                // 즉시 전환: 보스 hp 리셋(구간 배수 반영) + 보스 모델 교체(구간 텍스처 반영) + 인형 풀 재생성
                GameQuestManager.Instance.SetCurrentStage(stage);
                // 최고 클리어 기록도 자연 진행과 동일한 "직전 스테이지까지 클리어" 상태로 되돌린다 (도감 등급 판정)
                GameQuestManager.Instance.OverrideMaxClearedStage(stage - 1);
                PlayerPrefs.Save();
                if (GameDollCreator.Instance != null)
                    GameDollCreator.Instance.ResetCurrentStage();
                Debug.Log($"[DebugStage] 즉시 전환: {Label(stage)}");
                return;
            }

            PlayerPrefs.SetInt(KeyStage, stage);
            PlayerPrefs.SetInt(KeyMaxCleared, stage - 1);   // 자연 진행과 동일한 클리어 기록 (도감 등급 판정)
            PlayerPrefs.DeleteKey(KeyBossHp);   // 다음 읽기에서 새 스테이지 최대 hp(구간 배수 포함)로 채워진다
            PlayerPrefs.Save();
            Debug.Log($"[DebugStage] 저장: {Label(stage)} (다음 플레이부터 적용)");
        }
    }
}
