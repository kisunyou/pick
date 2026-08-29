using UnityEditor;
using UnityEngine;

namespace FunRabbit.EditorTools
{
    // 테스트용 스테이지 설정 메뉴 (에디터 전용). GameQuestManager 와 같은 PlayerPrefs 키를 쓴다.
    //   currentStage : 현재 스테이지(1부터), TotalStageCount+1 = 올클리어 단계
    //   bossHp       : 보스 남은 hp - 스테이지를 바꾸면 삭제해 다음 읽기에서 새 스테이지 최대치로 채워지게 한다
    // - 편집 모드: PlayerPrefs 만 갱신 → 다음 플레이부터 적용 (GameMain.ForcedStartStage 와 달리 저장값을 직접 바꾼다)
    // - 플레이 모드: GameQuestManager.SetCurrentStage(stage) 로 즉시 전환(보스 hp 리셋 + 보스 모델 교체) + 인형 풀 재생성
    // 아군 슬롯/대기열(AllySlot*)은 건드리지 않는다 - 스테이지가 바뀌어도 아군은 유지된다(정상 진행과 동일).
    public static class DebugStageMenu
    {
        const string KeyStage = "currentStage";   // GameQuestManager.KEY_STAGE
        const string KeyBossHp = "bossHp";        // GameQuestManager.KEY_BOSS_HP
        const string MenuRoot = "FunRabbit/Debug/스테이지/";

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

        [MenuItem(MenuRoot + "올클리어 단계 (전 스테이지 클리어)", false, 200)]
        static void SetAllClear() => SetStage(GameQuestData.TotalStageCount + 1);

        [MenuItem(MenuRoot + "현재 스테이지 로그", false, 300)]
        static void LogCurrent()
        {
            int saved = PlayerPrefs.GetInt(KeyStage, -1);
            string live = Application.isPlaying && GameQuestManager.IsCheckInstance()
                ? $"{GameQuestManager.Instance.CurrentStage} (bossHp {GameQuestManager.Instance.BossHp}/{GameQuestManager.Instance.MaxBossHp})"
                : "(플레이 중 아님)";
            Debug.Log($"[DebugStage] 저장값={saved} (-1 = 키 없음 → 1), 현재 적용={live}, 총 스테이지={GameQuestData.TotalStageCount}");
        }

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
        [MenuItem(MenuRoot + "올클리어 단계 (전 스테이지 클리어)", true)] static bool VAll() => Check(GameQuestData.TotalStageCount + 1);

        static bool Check(int stage)
        {
            string label = stage > GameQuestData.TotalStageCount ? "올클리어 단계 (전 스테이지 클리어)" : $"스테이지 {stage,2}";
            Menu.SetChecked(MenuRoot + label, GetCurrentStage() == stage);
            return true;
        }

        static int GetCurrentStage()
        {
            if (Application.isPlaying && GameQuestManager.IsCheckInstance())
                return GameQuestManager.Instance.CurrentStage;
            return PlayerPrefs.GetInt(KeyStage, 1);
        }

        static void SetStage(int stage)
        {
            int allClear = GameQuestData.TotalStageCount + 1;
            if (allClear <= 1)
            {
                Debug.LogError("[DebugStage] quest.json 스테이지 데이터를 읽을 수 없습니다.");
                return;
            }
            stage = Mathf.Clamp(stage, 1, allClear);

            if (Application.isPlaying && GameQuestManager.IsCheckInstance())
            {
                // 즉시 전환: 보스 hp 리셋 + 보스 모델 교체(즉시 스폰) + 스테이지 변경 이벤트. 인형 풀도 새 스테이지 기준으로 재생성.
                GameQuestManager.Instance.SetCurrentStage(stage);
                PlayerPrefs.Save();
                if (GameDollCreator.Instance != null)
                    GameDollCreator.Instance.ResetCurrentStage();
                Debug.Log($"[DebugStage] 즉시 전환: 스테이지 {stage}" + (stage == allClear ? " (올클리어 단계)" : ""));
                return;
            }

            PlayerPrefs.SetInt(KeyStage, stage);
            PlayerPrefs.DeleteKey(KeyBossHp);   // 다음 읽기에서 새 스테이지 최대 hp 로 채워진다
            PlayerPrefs.Save();
            Debug.Log($"[DebugStage] 저장: 스테이지 {stage}" + (stage == allClear ? " (올클리어 단계)" : "") + " (다음 플레이부터 적용)");
        }
    }
}
