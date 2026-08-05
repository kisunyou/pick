using UnityEngine;

namespace FunRabbit
{
    // Stage0 씬에 배치된 보스 카메라. UIHud가 이 카메라의 RenderTexture를 가져가 화면 상단에 표시한다.
    public class BossCamera : InstanceSetter<BossCamera>
    {
        [SerializeField] Camera cam;

        public RenderTexture TargetTexture => cam != null ? cam.targetTexture : null;
        public Camera Cam => cam;

        // ally/보스는 GameCameraManager가 관리하는 메인/도감 카메라가 아니라, 이 별도의 bossCamera(orthographic,
        // Stage0의 격리된 다이오라마 위치)로 촬영해 RenderTexture로 UIHud.bossCamView(RawImage)에 보여주는
        // 구조다. 그래서 화면 좌표(screen point) 기준 변환은 Overlay 캔버스 가정이 깨져 엉뚱한 값이 나올 수 있어,
        // 순수 Transform 좌표 변환만으로 계산한다: 1) bossCamera 뷰포트 좌표(0~1) 계산 -> 2) bossCamView의
        // 로컬 rect 안에서 그 비율 지점 -> 3) 월드 좌표로 변환한다 (UIActorHPGage/BattleActorDamageControl이 사용).
        public static bool TryConvertWorldToHudPoint(Vector3 worldPosition, out Vector3 hudWorldPoint)
        {
            hudWorldPoint = default;

            if (!TryGetSetInstance(out BossCamera bossCamera) || bossCamera.Cam == null)
                return false;

            RectTransform bossCamViewRect = UIHud.Instance != null ? UIHud.Instance.BossCamViewRect : null;
            if (bossCamViewRect == null)
                return false;

            Vector3 viewportPoint = bossCamera.Cam.WorldToViewportPoint(worldPosition);

            Rect localRect = bossCamViewRect.rect;
            Vector2 localPoint = new Vector2(
                Mathf.Lerp(localRect.xMin, localRect.xMax, viewportPoint.x),
                Mathf.Lerp(localRect.yMin, localRect.yMax, viewportPoint.y));

            hudWorldPoint = bossCamViewRect.TransformPoint(localPoint);
            return true;
        }
    }
}
