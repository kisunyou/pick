using System;
using UnityEngine;

public class Utils
{
    
}

public class DataObserver<T> where T : struct
{
    private T _value;

    public T Value
    {
        get => _value;
        set
        {
            _value = value;
            IsValueSet = true;
            _onChangeData?.Invoke(_value);
            //Notify();
        }
    }

    public bool IsValueSet { get; private set; }
    public bool IsExistEvent => _onChangeData != null;

    //public delegate void OnChangeData(T data);
    private Action<T> _onChangeData;

    public void Refresh()
    {
        _onChangeData?.Invoke(Value);
    }

    public void Attach(Action<T> action)
    {
        if (action == null) return;
        _onChangeData += action;
        action.Invoke(Value);
    }
    public void Detach(Action<T> action)
    {
        //if (_onChangeData != null)
        _onChangeData -= action;
    }

    public void ClearEvent()
    {
        _onChangeData = null;
    }

    public void Clear()
    {
        _value = default;
        _onChangeData = null;
        IsValueSet = false;
    }

    /*
    private void Notify()
    {
        _onChangeData?.Invoke(_value);
    }*/

    ~DataObserver()
    {
        _onChangeData = null;
    }
}