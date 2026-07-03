using UnityEngine;

namespace FunRabbit
{
    public class Actor : MonoBehaviour
    {
        // 실제 인형뽑기 인형처럼 "묵직하게" 가라앉고, 크레인/바닥에서 튕겨 날아가지 않도록
        // 런타임에 물리 감쇠 값을 일괄 적용한다.
        // (인형 프리팹이 11종으로 흩어져 있어, 값이 제각각이 되지 않도록 한 곳에서 관리)
        const float LINEAR_DAMPING = 0.5f;            // 이동 감쇠: 멀리 미끄러지거나 날아가지 않게
        const float ANGULAR_DAMPING = 1.0f;           // 회전 감쇠: 마구 구르지 않고 무게감 있게 정지
        const float MAX_DEPENETRATION_VELOCITY = 2f;  // 겹침(끼임) 해소 시 폭발적으로 튕겨 나가는 속도 제한

        public DollData Data { get; set; }

        private void Start()
        {
            ApplyDollPhysics();
            StageManager.AddActor(this);
        }

        // 인형 루트의 Rigidbody에 무게감/안정성 위주의 물리 값을 적용한다.
        // Rigidbody가 없는 오브젝트(전시용 프리팹 등)는 그대로 무시한다.
        private void ApplyDollPhysics()
        {
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

        private void OnDestroy()
        {
            StageManager.RemoveActor(this);
        }
    }
}
