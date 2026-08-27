using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace FunRabbit
{
    // ally/보스(BattleActor)가 데미지를 입으면 그 위치에 데미지 숫자(UIDamageText)를 띄운다.
    // UIDamageText는 풀링해 재사용하며, 아래->위 이동 + 페이드아웃을 DOTween으로 재생한다.
    // 컬렉션(도감) 화면에서는 전투 정보를 노출하지 않으므로(UIHud.SetActiveHud의 bossBattle 숨김과 동일 기준)
    // GameStatus.COLLECTION 동안은 새 데미지 숫자를 띄우지 않고, 진입 시점에 재생 중이던 숫자도 즉시 회수한다.
    public class BattleActorDamageControl : Singleton<BattleActorDamageControl>
    {
        private const string PrefabPath = "UI2/Prefabs/Damage/UIDamageText";

        private const float MoveUpDistance = 60f; // 위로 이동하는 거리 (캔버스 단위)
        private const float PlayDuration = 0.8f;  // 이동 + 페이드 전체 재생 시간(초)

        private readonly Stack<UIDamageText> _pool = new Stack<UIDamageText>();
        // 현재 재생 중인 데미지 숫자와 그 트윈 (도감 진입 시 일괄 중단/회수용)
        private readonly Dictionary<UIDamageText, Sequence> _playing = new Dictionary<UIDamageText, Sequence>();
        private GameObject _prefab;

        // 전투 정보를 숨겨야 하는 상태(컬렉션)인지
        private static bool IsBattleHudHidden
            => GameMain.IsCheckInstance() && GameMain.Instance.CurrentStatus == GameStatus.COLLECTION;

        protected override void Awake()
        {
            base.Awake();
            GameMain.SubscribeStatus(OnChangedGameStatus);
        }

        protected override void OnDestroy()
        {
            GameMain.UnsubscribeStatus(OnChangedGameStatus);
            base.OnDestroy();
        }

        // 컬렉션(도감) 진입 시 재생 중인 데미지 숫자를 모두 중단하고 풀로 되돌린다
        private void OnChangedGameStatus(GameStatus status)
        {
            if (status != GameStatus.COLLECTION || _playing.Count == 0)
                return;

            // ReturnToPool이 _playing을 수정하므로 복사본으로 순회
            var playing = new List<KeyValuePair<UIDamageText, Sequence>>(_playing);
            _playing.Clear();
            foreach (var pair in playing)
            {
                pair.Value?.Kill();
                ReturnToPool(pair.Key);
            }
        }

        // ally/보스가 데미지를 입었을 때 호출한다. worldPosition은 보스 다이오라마(Stage0) 기준 3D 좌표.
        // scale: 공격력 강화/방어력 강화 버프가 적용된 데미지면 1이 아닌 값(예: 1.15/0.7)을 넘겨 텍스트 크기를 강조한다.
        // delta: 버프로 증감된 양(예: +5/-9). 0이 아니면 "damage(+5)"/"damage(-9)" 형식으로 표시한다.
        public void ShowDamage(Vector3 worldPosition, int damage, float scale = 1f, int delta = 0)
        {
            // 컬렉션(도감) 화면에서는 전투 정보를 표시하지 않는다 (전투 자체는 백그라운드에서 계속 진행)
            if (IsBattleHudHidden)
                return;

            UIDamageText damageText = GetPooledInstance();
            if (damageText == null)
                return;

            damageText.SetDamage(damage, delta);
            damageText.SetWorldPosition(worldPosition);
            damageText.SetScale(scale);

            PlayAnimation(damageText);
        }

        private UIDamageText GetPooledInstance()
        {
            if (_pool.Count > 0)
            {
                UIDamageText pooled = _pool.Pop();
                pooled.gameObject.SetActive(true);
                return pooled;
            }

            return CreateInstance();
        }

        private UIDamageText CreateInstance()
        {
            if (_prefab == null)
                _prefab = Resources.Load<GameObject>(PrefabPath);

            if (_prefab == null || UIHud.Instance == null)
            {
                Debug.LogError($"[BattleActorDamageControl] 생성 실패 - prefab: {_prefab != null}, UIHud: {UIHud.Instance != null}");
                return null;
            }

            GameObject instance = Instantiate(_prefab, UIHud.Instance.transform);
            return instance.GetComponent<UIDamageText>();
        }

        private void PlayAnimation(UIDamageText damageText)
        {
            RectTransform rectTransform = damageText.RectTransform;

            TextMeshProUGUI text = damageText.Text;
            if (text != null)
                text.alpha = 1f;

            Vector2 endAnchoredPos = rectTransform.anchoredPosition + Vector2.up * MoveUpDistance;

            Sequence sequence = DOTween.Sequence();
            sequence.Join(rectTransform.DOAnchorPos(endAnchoredPos, PlayDuration).SetEase(Ease.OutQuad));
            if (text != null)
                sequence.Join(text.DOFade(0f, PlayDuration).SetEase(Ease.InQuad));

            sequence.OnComplete(() => ReturnToPool(damageText));
            _playing[damageText] = sequence;
        }

        private void ReturnToPool(UIDamageText damageText)
        {
            if (damageText == null)
                return;

            _playing.Remove(damageText);
            damageText.gameObject.SetActive(false);
            _pool.Push(damageText);
        }
    }
}
