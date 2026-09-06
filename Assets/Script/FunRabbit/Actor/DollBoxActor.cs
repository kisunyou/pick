using UnityEngine;

namespace FunRabbit
{
    // 인형뽑기 기기(크레인) 모드 인형. 물리 기반으로 크레인에 잡혀 뽑힌다.
    public class DollBoxActor : Actor
    {
        // 실제 인형뽑기 인형처럼 "묵직하게" 가라앉고, 크레인/바닥에서 튕겨 날아가지 않도록
        // 런타임에 물리 감쇠 값을 일괄 적용한다.
        // (인형 프리팹이 11종으로 흩어져 있어, 값이 제각각이 되지 않도록 한 곳에서 관리)
        const float LINEAR_DAMPING = 0.5f;            // 이동 감쇠: 멀리 미끄러지거나 날아가지 않게
        const float ANGULAR_DAMPING = 1.0f;           // 회전 감쇠: 마구 구르지 않고 무게감 있게 정지
        // 겹침(끼임) 해소 속도 상한. 크레인 하강 속도(≈2.94 m/s)보다 작으면 인형이
        // 밀려나는 속도보다 클로가 파고드는 속도가 빨라 겹침(뚫림)이 계속 커진다.
        // 반드시 하강 속도보다 크게 유지할 것. (튕김은 damping 0.5/1.0이 억제)
        const float MAX_DEPENETRATION_VELOCITY = 4f;

        // 인형 공용 물리 재질 (마찰 0.9/0.8, Maximum combine).
        // bear/pig/octopus/random 프리팹은 재질 미할당(엔진 기본 마찰 0.6)이라 doll 재질(0.9 Maximum)
        // 인형과 잡히는 난이도 편차가 있었다 - 재질이 빈 콜라이더에 여기서 채워 전 인형 마찰을 통일한다.
        const string DOLL_PHYSIC_MATERIAL_PATH = "Model/Materials/doll physic material";
        static PhysicsMaterial _dollPhysicMaterial;

        private void Start()
        {
            ApplyDollPhysics();
            StageManager.AddActor(this);
        }

        // 인형 루트의 Rigidbody에 무게감/안정성 위주의 물리 값을 적용한다.
        // Rigidbody가 없는 오브젝트(전시용 프리팹 등)는 그대로 무시한다.
        private void ApplyDollPhysics()
        {
            ApplyMissingPhysicMaterial();

            if (!TryGetComponent(out Rigidbody body))
                return;

            body.linearDamping = LINEAR_DAMPING;
            body.angularDamping = ANGULAR_DAMPING;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // 크레인 집게가 빠르게 닫히거나 하강할 때 인형을 뚫고 지나가는(터널링) 현상을
            // 막기 위해, 가장 터널링에 강한 ContinuousSpeculative로 설정한다.
            // (크레인 바디 쪽은 CraneTransform 생성자에서 동일하게 설정)
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.maxDepenetrationVelocity = MAX_DEPENETRATION_VELOCITY;
        }

        // 물리 재질이 비어 있는 자식 콜라이더 전부에 인형 공용 재질을 할당한다.
        // (이미 재질이 있는 콜라이더는 그대로 둔다 - 동일한 doll 재질이 할당돼 있다)
        private void ApplyMissingPhysicMaterial()
        {
            if (_dollPhysicMaterial == null)
                _dollPhysicMaterial = Resources.Load<PhysicsMaterial>(DOLL_PHYSIC_MATERIAL_PATH);

            if (_dollPhysicMaterial == null)
            {
                Debug.LogWarning($"[DollBoxActor] 인형 물리 재질 로드 실패: {DOLL_PHYSIC_MATERIAL_PATH}");
                return;
            }

            foreach (Collider dollCollider in GetComponentsInChildren<Collider>(true))
            {
                if (dollCollider.sharedMaterial == null)
                    dollCollider.sharedMaterial = _dollPhysicMaterial;
            }
        }
    }
}
