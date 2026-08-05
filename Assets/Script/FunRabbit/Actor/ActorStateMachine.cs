using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    // BattleActor 전용 상태 머신.
    // dev 작업 폴더(TeenyWorld)의 TeenyStateMachine 구조를 pick 규모(상태 3개, 소수 인스턴스)에 맞게
    // key-index 배열 최적화 대신 Dictionary 기반으로 단순화해 가져왔다.
    public class ActorStateMachine
    {
        private readonly Dictionary<int, ActorState> _states = new Dictionary<int, ActorState>();
        private ActorState _currentState;

        public void CreateState(params ActorState[] states)
        {
            foreach (var state in states)
                _states[state.Key] = state;
        }

        public bool ChangeState(int key)
        {
            if (!_states.TryGetValue(key, out ActorState nextState))
            {
                Debug.LogError($"[ActorStateMachine] not found state key : {key}");
                return false;
            }

            if (ReferenceEquals(nextState, _currentState))
                return false;

            _currentState?.LeaveState();
            _currentState = nextState;
            nextState.EnterState();
            return true;
        }

        public void UpdateState(float deltaTime)
        {
            _currentState?.UpdateState(deltaTime);
        }

        public bool IsState(int key)
        {
            return _currentState != null && _currentState.Key == key;
        }

        public ActorState GetCurState() => _currentState;
    }
}
