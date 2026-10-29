using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FunRabbit
{
    // Button.onClick alone also accepts a press begun before the button became interactable.
    public sealed class UILoadingStartInput : MonoBehaviour, IPointerDownHandler, IPointerClickHandler, ISubmitHandler
    {
        private Action _onStart;
        private bool _ready;
        private int? _pressedPointer;

        public void Bind(Action onStart) => _onStart = onStart;

        public void SetReady(bool ready)
        {
            _ready = ready;
            _pressedPointer = null;
        }

        private void OnDisable() => SetReady(false);

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_ready && eventData.button == PointerEventData.InputButton.Left)
                _pressedPointer = eventData.pointerId;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || _pressedPointer != eventData.pointerId) return;
            Accept();
        }

        public void OnSubmit(BaseEventData eventData) => Accept();

        private void Accept()
        {
            if (!_ready || !isActiveAndEnabled) return;
            SetReady(false);
            _onStart?.Invoke();
        }
    }
}
