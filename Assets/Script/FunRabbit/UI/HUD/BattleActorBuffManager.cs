using UnityEngine;

namespace FunRabbit
{
    // UIHud의 BuffIcons(자식으로 UIBuffIcon들을 둔다) 오브젝트에 붙는다.
    // apUp/deffenseUp 보유 개수는 PlayerContext(itemKey 기준)가 유일한 저장소이고, 이 클래스는
    // 그 값을 아이콘에 반영만 한다(별도 카운트를 들고 있지 않아 자체 저장소와 어긋날 일이 없다).
    public class BattleActorBuffManager : MonoBehaviour
    {
        private UIBuffIcon[] _icons;

        private void Awake()
        {
            _icons = GetComponentsInChildren<UIBuffIcon>(true);

            // 게임 시작 시 이미 보유 중인 개수(재접속 등)를 아이콘에 즉시 반영한다.
            for (int i = 0; i < _icons.Length; i++)
                RefreshIcon(_icons[i].BuffType);
        }

        // 보상 등으로 buffType 아이템을 addCount만큼 얻었을 때 호출한다. PlayerContext 보유량에 반영하고 아이콘을 갱신한다.
        public void AddBuff(BuffType buffType, int addCount)
        {
            PlayerContext.AddItemAmount(ItemKeyForBuffType(buffType), addCount);
            RefreshIcon(buffType);
        }

        // 전투 중 buffType 1개를 소비한다. 보유량이 없으면 아무 일도 하지 않고 false를 반환한다.
        public bool TryConsumeBuff(BuffType buffType)
        {
            bool consumed = PlayerContext.TrySpendItemAmount(ItemKeyForBuffType(buffType), 1);
            if (consumed)
                RefreshIcon(buffType);

            return consumed;
        }

        // PlayerContext의 현재 보유 개수를 buffType에 해당하는 아이콘에 그대로 반영한다.
        private void RefreshIcon(BuffType buffType)
        {
            long amount = PlayerContext.GetItemAmount(ItemKeyForBuffType(buffType));
            FindIcon(buffType)?.SetCount((int)amount);
        }

        private UIBuffIcon FindIcon(BuffType buffType)
        {
            for (int i = 0; i < _icons.Length; i++)
            {
                if (_icons[i].BuffType == buffType)
                    return _icons[i];
            }

            return null;
        }

        private static int ItemKeyForBuffType(BuffType buffType)
        {
            return buffType == BuffType.AttackPowerUp
                ? PlayerContext.ATTACK_POWER_UP_ITEM_KEY
                : PlayerContext.DEFENSE_UP_ITEM_KEY;
        }
    }
}
