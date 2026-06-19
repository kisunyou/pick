using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

// EventTrigger가 Attach 되어있는 GameObject에 AddComponent 하세요.
public class UIModelViewController : MonoBehaviour
{
    public float _zoomSensitivity = 0.012f;
    public float _moveSensitivity = 0.003f;
    public Vector2 _zoomRange = new Vector2(2f, 10f);

    GameObject _model = null;

    Dictionary<int, Vector2> _prevPointDic = new Dictionary<int, Vector2>();
    Vector3 _rotationTemp;

    float _zoomPosZ = 0f;
    float _zoomBaseDistance = 0f;
    Vector2 _centerForMove;
    //	bool _changeAnimationFlag = false;

    //	Animator _animator = null;
    //	int _clipPlayIdx = 0;
    //	List<AnimationClip> _clipList = new List<AnimationClip>();

    public GameObject GetModel()
    {
        return _model;
    }

    public void SetModel(GameObject model)
    {
        _model = model;

        if (_model == null)
        {
            return;
        }

        //		_clipPlayIdx = 0;
        //		_clipList.Clear();
        //		_animator = GameUtil.FindAnimator(_model.transform);
        //
        //		if (_animator != null)
        //		{
        //			AnimatorOverrideController animatorOverrideCtrl = _animator.runtimeAnimatorController as AnimatorOverrideController;
        //
        //			if (animatorOverrideCtrl.clips != null)
        //			{
        //				for (int i = 0; i < animatorOverrideCtrl.clips.Length; ++i)
        //				{
        //					if (animatorOverrideCtrl.clips[i].overrideClip != null && animatorOverrideCtrl.clips[i].originalClip != null)
        //					{
        //						if (animatorOverrideCtrl.clips[i].overrideClip.isLooping)
        //						{
        //							_clipList.Add(animatorOverrideCtrl.clips[i].originalClip);
        //						}
        //					}
        //				}
        //			}
        //		}
    }

    void Awake()
    {
        var trigger = this.gameObject.GetComponent<EventTrigger>();

        EventTrigger.Entry entry = new EventTrigger.Entry();

        entry.eventID = EventTriggerType.Drag;
        entry.callback.AddListener((data) =>
        {
            if (_prevPointDic.Count == 1)
            {
                OnDragForSingle((PointerEventData)data);
            }
            else if (_prevPointDic.Count == 2)
            {
                // 줌을 안되도록 막음.
                //OnDragForDouble((PointerEventData)data);
            }
        });
        trigger.triggers.Add(entry);

        entry = new EventTrigger.Entry();
        entry.eventID = EventTriggerType.BeginDrag;
        entry.callback.AddListener((data) => { OnPointDown((PointerEventData)data); });
        trigger.triggers.Add(entry);

        entry = new EventTrigger.Entry();
        entry.eventID = EventTriggerType.PointerUp;
        entry.callback.AddListener((data) => { OnPointUp((PointerEventData)data); });
        trigger.triggers.Add(entry);

        entry = new EventTrigger.Entry();
        entry.eventID = EventTriggerType.PointerClick;
        entry.callback.AddListener((data) => { OnClick((PointerEventData)data); });
        trigger.triggers.Add(entry);
    }

    void OnPointUp(PointerEventData data)
    {
        _prevPointDic.Remove(data.pointerId);
    }

    void OnClick(PointerEventData data)
    {
        //		if (_animator == null || _changeAnimationFlag == false)
        //		{
        //			return;
        //		}
        //
        //		if (_clipList.Count > 0)
        //		{
        //			_clipPlayIdx = (_clipPlayIdx + 1) % _clipList.Count;
        //			_animator.Play(_clipList[_clipPlayIdx].name);
        //		}
    }

    void OnPointDown(PointerEventData data)
    {
        if (_model == null)
        {
            return;
        }

        //		_changeAnimationFlag = true;

        if (_prevPointDic.ContainsKey(data.pointerId))
        {
            _prevPointDic[data.pointerId] = data.position;
        }
        else
        {
            _prevPointDic.Add(data.pointerId, data.position);
        }

        _rotationTemp = _model.transform.rotation.eulerAngles;

        if (_prevPointDic.Count == 2)
        {
            var iter = _prevPointDic.GetEnumerator();

            iter.MoveNext();
            var p1 = iter.Current.Value;
            iter.MoveNext();
            var p2 = iter.Current.Value;

            _zoomPosZ = _model.transform.localPosition.z;
            _zoomBaseDistance = Vector2.Distance(p1, p2);
            _centerForMove = Vector2.Lerp(p1, p2, 0.5f);
        }
    }

    void OnDragForSingle(PointerEventData data)
    {
        if (_model == null || _prevPointDic.ContainsKey(data.pointerId) == false)
        {
            return;
        }

        Vector2 variation = (data.position - _prevPointDic[data.pointerId]);

        _rotationTemp.y = _rotationTemp.y - variation.x;

        _model.transform.rotation = Quaternion.Euler(_rotationTemp);

        _prevPointDic[data.pointerId] = data.position;

        //		_changeAnimationFlag = false;
    }

    void OnDragForDouble(PointerEventData data)
    {
        if (_model == null || _prevPointDic.ContainsKey(data.pointerId) == false)
        {
            return;
        }

        Vector2 p1 = data.position;
        Vector2 p2 = Vector2.zero;

        var iter = _prevPointDic.GetEnumerator();

        while (iter.MoveNext())
        {
            if (iter.Current.Key != data.pointerId)
            {
                p2 = iter.Current.Value;
                break;
            }
        }

        float nowDistance = Vector2.Distance(p1, p2);
        float variation = _zoomBaseDistance - nowDistance;

        Vector2 nowCenter = Vector2.Lerp(p1, p2, 0.5f);

        var moveInterval = (nowCenter - _centerForMove) * _moveSensitivity;
        _model.transform.position += new Vector3(moveInterval.x, moveInterval.y, 0f);
        _centerForMove = nowCenter;

        var monPos = _model.transform.localPosition;
        monPos.z = Mathf.Clamp((_zoomPosZ + variation * _zoomSensitivity), _zoomRange.x, _zoomRange.y);
        _model.transform.localPosition = monPos;

        _prevPointDic[data.pointerId] = p1;

        if (monPos.z <= _zoomRange.x || _zoomRange.y <= monPos.z)
        {
            _zoomPosZ = monPos.z;
            _zoomBaseDistance = Vector2.Distance(p1, p2);
        }

        //		_changeAnimationFlag = false;
    }
}
