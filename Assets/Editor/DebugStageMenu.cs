using UnityEditor;
using UnityEngine;

namespace FunRabbit.EditorTools
{
    // 테스트용 스테이지 설정 메뉴 (에디터 전용). GameQuestManager 와 같은 PlayerPrefs 키를 쓴다.
    // 연속 36스테이지 구조: 1~12 노멀(원본) / 13~24 _g(스탯 x2) / 25~36 _r(스탯 x3).
    //   currentStage : 현재 스테이지(1~36)
    //   bossHp       : 보스 남은 hp - 스테이지를 바꾸면 삭제해 다음 읽기에서 새 스테이지 최대치로 채워지게 한다
    // - 플레이 모드: GameQuestManager.SetCurrentStage(stage) 로 즉시 전환(보스 hp 리셋 + 보스 모델 교체) + 인형 풀 재생성
    // - 편집 모드: PlayerPrefs 만 갱신 → 다음 플레이부터 적용
    // 아군 슬롯/대기열(AllySlot*)은 건드리지 않는다 - 스테이지가 바뀌어도 아군은 유지된다(정상 진행과 동일).
    public static class DebugStageMenu
    {
        const string KeyStage = "currentStage";        // GameQuestManager.KEY_STAGE
        const string KeyBossHp = "bossHp";             // GameQuestManager.KEY_BOSS_HP
        const string KeyLegacyCycle = "currentCycle";  // 구버전 회차 키 - 남아있으면 마이그레이션이 스테이지를 밀어버리므로 제거
        const string KeyMaxCleared = "maxClearedStage"; // GameQuestManager.KEY_MAX_CLEARED_STAGE - 도감 변형(_g/_r) 등급 판정 기준
        const string MenuRoot = "FunRabbit/Debug/스테이지/";

        // ── 노멀 (1~12, 원본 텍스처, 스탯 x1) ─────────────────────────
        [MenuItem(MenuRoot + "스테이지  1", false, 101)] static void S1() => SetStage(1);
        [MenuItem(MenuRoot + "스테이지  2", false, 102)] static void S2() => SetStage(2);
        [MenuItem(MenuRoot + "스테이지  3", false, 103)] static void S3() => SetStage(3);
        [MenuItem(MenuRoot + "스테이지  4", false, 104)] static void S4() => SetStage(4);
        [MenuItem(MenuRoot + "스테이지  5", false, 105)] static void S5() => SetStage(5);
        [MenuItem(MenuRoot + "스테이지  6", false, 106)] static void S6() => SetStage(6);
        [MenuItem(MenuRoot + "스테이지  7", false, 107)] static void S7() => SetStage(7);
        [MenuItem(MenuRoot + "스테이지  8", false, 108)] static void S8() => SetStage(8);
        [MenuItem(MenuRoot + "스테이지  9", false, 109)] static void S9() => SetStage(9);
        [MenuItem(MenuRoot + "스테이지 10", false, 110)] static void S10() => SetStage(10);
        [MenuItem(MenuRoot + "스테이지 11", false, 111)] static void S11() => SetStage(11);
        [MenuItem(MenuRoot + "스테이지 12", false, 112)] static void S12() => SetStage(12);

        // ── _g 구간 (13~24, _g 텍스처, 스탯 x2) ───────────────────────
        [MenuItem(MenuRoot + "스테이지 13 (_g x2)", false, 131)] static void S13() => SetStage(13);
        [MenuItem(MenuRoot + "스테이지 14 (_g x2)", false, 132)] static void S14() => SetStage(14);
        [MenuItem(MenuRoot + "스테이지 15 (_g x2)", false, 133)] static void S15() => SetStage(15);
        [MenuItem(MenuRoot + "스테이지 16 (_g x2)", false, 134)] static void S16() => SetStage(16);
        [MenuItem(MenuRoot + "스테이지 17 (_g x2)", false, 135)] static void S17() => SetStage(17);
        [MenuItem(MenuRoot + "스테이지 18 (_g x2)", false, 136)] static void S18() => SetStage(18);
        [MenuItem(MenuRoot + "스테이지 19 (_g x2)", false, 137)] static void S19() => SetStage(19);
        [MenuItem(MenuRoot + "스테이지 20 (_g x2)", false, 138)] static void S20() => SetStage(20);
        [MenuItem(MenuRoot + "스테이지 21 (_g x2)", false, 139)] static void S21() => SetStage(21);
        [MenuItem(MenuRoot + "스테이지 22 (_g x2)", false, 140)] static void S22() => SetStage(22);
        [MenuItem(MenuRoot + "스테이지 23 (_g x2)", false, 141)] static void S23() => SetStage(23);
        [MenuItem(MenuRoot + "스테이지 24 (_g x2)", false, 142)] static void S24() => SetStage(24);

        // ── _r 구간 (25~36, _r 텍스처, 스탯 x3, 36 클리어 시 25로 반복) ──
        [MenuItem(MenuRoot + "스테이지 25 (_r x3)", false, 161)] static void S25() => SetStage(25);
        [MenuItem(MenuRoot + "스테이지 26 (_r x3)", false, 162)] static void S26() => SetStage(26);
        [MenuItem(MenuRoot + "스테이지 27 (_r x3)", false, 163)] static void S27() => SetStage(27);
        [MenuItem(MenuRoot + "스테이지 28 (_r x3)", false, 164)] static void S28() => SetStage(28);
        [MenuItem(MenuRoot + "스테이지 29 (_r x3)", false, 165)] static void S29() => SetStage(29);
        [MenuItem(MenuRoot + "스테이지 30 (_r x3)", false, 166)] static void S30() => SetStage(30);
        [MenuItem(MenuRoot + "스테이지 31 (_r x3)", false, 167)] static void S31() => SetStage(31);
        [MenuItem(MenuRoot + "스테이지 32 (_r x3)", false, 168)] static void S32() => SetStage(32);
        [MenuItem(MenuRoot + "스테이지 33 (_r x3)", false, 169)] static void S33() => SetStage(33);
        [MenuItem(MenuRoot + "스테이지 34 (_r x3)", false, 170)] static void S34() => SetStage(34);
        [MenuItem(MenuRoot + "스테이지 35 (_r x3)", false, 171)] static void S35() => SetStage(35);
        [MenuItem(MenuRoot + "스테이지 36 (_r x3)", false, 172)] static void S36() => SetStage(36);

        [MenuItem(MenuRoot + "현재 스테이지 로그", false, 300)]
        static void LogCurrent()
        {
            int saved = PlayerPrefs.GetInt(KeyStage, -1);
            string live = Application.isPlaying && GameQuestManager.IsCheckInstance()
                ? $"{GameQuestManager.Instance.CurrentStage} (구간 {GameQuestManager.Instance.CurrentCycle}, 보스 {GameQuestManager.Instance.GetCurrentStageData()?.animalKey}, bossHp {GameQuestManager.Instance.BossHp}/{GameQuestManager.Instance.MaxBossHp})"
                : "(플레이 중 아님)";
            Debug.Log($"[DebugStage] 저장값={saved} (-1 = 키 없음 → 1), 현재 적용={live}, 총 스테이지={GameQuestManager.TotalPlayableStageCount}, 최고클리어={PlayerPrefs.GetInt(KeyMaxCleared, -1)}");
        }

        // ── 체크마크 (현재 스테이지 표시) ─────────────────────────────
        [MenuItem(MenuRoot + "스테이지  1", true)] static bool V1() => Check(1);
        [MenuItem(MenuRoot + "스테이지  2", true)] static bool V2() => Check(2);
        [MenuItem(MenuRoot + "스테이지  3", true)] static bool V3() => Check(3);
        [MenuItem(MenuRoot + "스테이지  4", true)] static bool V4() => Check(4);
        [MenuItem(MenuRoot + "스테이지  5", true)] static bool V5() => Check(5);
        [MenuItem(MenuRoot + "스테이지  6", true)] static bool V6() => Check(6);
        [MenuItem(MenuRoot + "스테이지  7", true)] static bool V7() => Check(7);
        [MenuItem(MenuRoot + "스테이지  8", true)] static bool V8() => Check(8);
        [MenuItem(MenuRoot + "스테이지  9", true)] static bool V9() => Check(9);
        [MenuItem(MenuRoot + "스테이지 10", true)] static bool V10() => Check(10);
        [MenuItem(MenuRoot + "스테이지 11", true)] static bool V11() => Check(11);
        [MenuItem(MenuRoot + "스테이지 12", true)] static bool V12() => Check(12);
        [MenuItem(MenuRoot + "스테이지 13 (_g x2)", true)] static bool V13() => Check(13);
        [MenuItem(MenuRoot + "스테이지 14 (_g x2)", true)] static bool V14() => Check(14);
        [MenuItem(MenuRoot + "스테이지 15 (_g x2)", true)] static bool V15() => Check(15);
        [MenuItem(MenuRoot + "스테이지 16 (_g x2)", true)] static bool V16() => Check(16);
        [MenuItem(MenuRoot + "스테이지 17 (_g x2)", true)] static bool V17() => Check(17);
        [MenuItem(MenuRoot + "스테이지 18 (_g x2)", true)] static bool V18() => Check(18);
        [MenuItem(MenuRoot + "스테이지 19 (_g x2)", true)] static bool V19() => Check(19);
        [MenuItem(MenuRoot + "스테이지 20 (_g x2)", true)] static bool V20() => Check(20);
        [MenuItem(MenuRoot + "스테이지 21 (_g x2)", true)] static bool V21() => Check(21);
        [MenuItem(MenuRoot + "스테이지 22 (_g x2)", true)] static bool V22() => Check(22);
        [MenuItem(MenuRoot + "스테이지 23 (_g x2)", true)] static bool V23() => Check(23);
        [MenuItem(MenuRoot + "스테이지 24 (_g x2)", true)] static bool V24() => Check(24);
        [MenuItem(MenuRoot + "스테이지 25 (_r x3)", true)] static bool V25() => Check(25);
        [MenuItem(MenuRoot + "스테이지 26 (_r x3)", true)] static bool V26() => Check(26);
        [MenuItem(MenuRoot + "스테이지 27 (_r x3)", true)] static bool V27() => Check(27);
        [MenuItem(MenuRoot + "스테이지 28 (_r x3)", true)] static bool V28() => Check(28);
        [MenuItem(MenuRoot + "스테이지 29 (_r x3)", true)] static bool V29() => Check(29);
        [MenuItem(MenuRoot + "스테이지 30 (_r x3)", true)] static bool V30() => Check(30);
        [MenuItem(MenuRoot + "스테이지 31 (_r x3)", true)] static bool V31() => Check(31);
        [MenuItem(MenuRoot + "스테이지 32 (_r x3)", true)] static bool V32() => Check(32);
        [MenuItem(MenuRoot + "스테이지 33 (_r x3)", true)] static bool V33() => Check(33);
        [MenuItem(MenuRoot + "스테이지 34 (_r x3)", true)] static bool V34() => Check(34);
        [MenuItem(MenuRoot + "스테이지 35 (_r x3)", true)] static bool V35() => Check(35);
        [MenuItem(MenuRoot + "스테이지 36 (_r x3)", true)] static bool V36() => Check(36);

        static string Label(int stage)
        {
            if (stage <= 12) return $"스테이지 {stage,2}";
            if (stage <= 24) return $"스테이지 {stage} (_g x2)";
            return $"스테이지 {stage} (_r x3)";
        }

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
                Debug.LogError("[DebugStage] quest.json 스테이지 데이터를 읽을 수 없습니다.");
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
