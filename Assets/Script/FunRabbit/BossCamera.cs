using UnityEngine;

namespace FunRabbit
{
    // Stage0 씬에 배치된 보스 카메라. UIHud가 이 카메라의 RenderTexture를 가져가 화면 상단에 표시한다.
    public class BossCamera : InstanceSetter<BossCamera>
    {
        [SerializeField] Camera cam;

        public RenderTexture TargetTexture => cam != null ? cam.targetTexture : null;
        public Camera Cam => cam;

        void Start()
        {
            if (ActorBattleSystem.TryGetSetInstance(out ActorBattleSystem battle))
                battle.RefreshBattleFraming();
        }

        Bounds _battleBounds;
        bool _hasBattleBounds;
        Mesh _framingMesh;

        protected override void OnDestroy()
        {
            if (_framingMesh != null)
            {
                if (Application.isPlaying) Destroy(_framingMesh);
                else DestroyImmediate(_framingMesh);
            }
            base.OnDestroy();
        }

        public void SetViewAspect(float aspect)
        {
            if (cam == null || aspect <= 0 || Mathf.Approximately(cam.aspect, aspect)) return;
            cam.aspect = aspect;
            if (_hasBattleBounds) FitBattleBounds(_battleBounds);
        }

        public void FrameBattle(Transform boss, Transform[] allyAnchors)
        {
            if (cam == null || boss == null) return;
            bool found = false;
            Bounds bounds = default;
            foreach (Renderer renderer in boss.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)) continue;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    // Imported skin bounds can be much larger than the visible doll. Bake once per spawn.
                    if (_framingMesh == null) _framingMesh = new Mesh { name = "Battle framing bounds" };
                    skin.BakeMesh(_framingMesh, true);
                    foreach (Vector3 vertex in _framingMesh.vertices)
                    {
                        Vector3 point = skin.transform.TransformPoint(vertex);
                        if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                        else bounds.Encapsulate(point);
                    }
                }
                else
                {
                    if (!found) { bounds = renderer.bounds; found = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
            }
            if (!found) return;
            Vector3 allySize = Vector3.Max(bounds.size * .75f, new Vector3(4f, 5f, 3f));
            if (allyAnchors != null)
                foreach (Transform anchor in allyAnchors)
                    if (anchor != null)
                        bounds.Encapsulate(new Bounds(anchor.position + Vector3.up * (allySize.y * .5f), allySize));
            FitBattleBounds(bounds);
        }

        // Fit the complete battle with headroom for attacks and health indicators.
        public void FitBattleBounds(Bounds bounds)
        {
            if (cam == null) return;
            _battleBounds = bounds;
            _hasBattleBounds = true;
            Vector3 min = Vector3.one * float.PositiveInfinity;
            Vector3 max = Vector3.one * float.NegativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = cam.transform.InverseTransformPoint(corner);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
            cam.orthographicSize = Mathf.Max(4f, (max.y - min.y) / 1.46f,
                (max.x - min.x) / (1.76f * cam.aspect));
            Vector3 center = (min + max) * .5f;
            cam.transform.position += cam.transform.right * center.x +
                cam.transform.up * (center.y + cam.orthographicSize * .08f);
            var cameraData = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (cameraData == null) return;
            foreach (Camera overlay in cameraData.cameraStack)
            {
                if (overlay == null) continue;
                overlay.orthographicSize = cam.orthographicSize;
                overlay.aspect = cam.aspect;
                overlay.transform.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);
            }
        }


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
